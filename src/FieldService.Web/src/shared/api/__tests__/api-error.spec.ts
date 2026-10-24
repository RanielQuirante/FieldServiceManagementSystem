import { describe, expect, it } from 'vitest'

import {
  ApiError,
  apiErrorFromResponse,
  firstFieldError,
  isApiError,
  networkError,
  unmappedFieldErrors,
  unwrap,
  unwrapEmpty,
  unwrapResponse,
} from '../api-error'

const correlationId = 'dd85f06c-7606-4dfc-a702-fa39647047d8'

function response(status: number, headers: Record<string, string> = {}): Response {
  return new Response(null, { status, headers })
}

describe('apiErrorFromResponse', () => {
  it('reads the status, code, title, detail, field errors and correlation id of a Problem Details body', () => {
    const error = apiErrorFromResponse(response(400), {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more fields are invalid.',
      status: 400,
      detail: 'Check the job fields.',
      instance: '/api/jobs',
      errors: {
        siteAddress: ['Enter the site address.'],
        scheduledEnd: ['The scheduled end must be after the scheduled start.'],
      },
      code: 'request.validation-failed',
      correlationId,
      traceId: '00-eafe895eb8119e6e5d06463590b2ef81-37a0025aa1f2e29f-00',
    })

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      status: 400,
      code: 'request.validation-failed',
      title: 'One or more fields are invalid.',
      detail: 'Check the job fields.',
      correlationId,
      retryAfterSeconds: null,
    })
    expect(error.fieldErrors).toEqual({
      siteAddress: ['Enter the site address.'],
      scheduledEnd: ['The scheduled end must be after the scheduled start.'],
    })
    expect(error.isNetworkError).toBe(false)
  })

  it('takes the correlation id from the response header when the body has none', () => {
    const error = apiErrorFromResponse(response(404, { 'X-Correlation-ID': correlationId }), {
      code: 'job.not-found',
    })

    expect(error.correlationId).toBe(correlationId)
  })

  it('reads Retry-After in whole seconds of at least one', () => {
    expect(apiErrorFromResponse(response(429, { 'Retry-After': '42' }), {}).retryAfterSeconds).toBe(
      42,
    )
    expect(apiErrorFromResponse(response(429, { 'Retry-After': '0' }), {}).retryAfterSeconds).toBe(
      1,
    )
    expect(
      apiErrorFromResponse(response(429, { 'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT' }), {})
        .retryAfterSeconds,
    ).toBeNull()
  })

  it('turns a body that is not Problem Details into a generic error', () => {
    const error = apiErrorFromResponse(
      response(502, { 'X-Correlation-ID': correlationId }),
      '<html>Bad gateway</html>',
    )

    expect(error).toMatchObject({
      status: 502,
      code: null,
      title: null,
      detail: null,
      correlationId,
      retryAfterSeconds: null,
    })
    expect(error.fieldErrors).toEqual({})
  })

  it('keeps only string messages from a malformed errors member', () => {
    const error = apiErrorFromResponse(response(400), {
      code: 'request.validation-failed',
      errors: { title: ['Enter a title.', 7], priority: 'Choose a priority.', page: [] },
    })

    expect(error.fieldErrors).toEqual({ title: ['Enter a title.'] })
  })

  it('keeps response text out of the error message', () => {
    const error = apiErrorFromResponse(response(400), {
      code: 'request.validation-failed',
      detail: 'dana.whitfield@fieldservice.example is not allowed here.',
    })

    expect(error.message).toBe('Request failed with status 400 (request.validation-failed)')
    expect(apiErrorFromResponse(response(500), 'stack trace text').message).toBe(
      'Request failed with status 500',
    )
  })
})

describe('networkError', () => {
  it('describes a request that got no response', () => {
    const error = networkError()

    expect(error.status).toBe(0)
    expect(error.isNetworkError).toBe(true)
    expect(error.code).toBeNull()
    expect(error.correlationId).toBeNull()
    expect(error.message).toBe('The request did not reach the server')
  })
})

describe('field error helpers', () => {
  const errors = {
    siteAddress: ['Enter the site address.'],
    'answers[2].note': ['Describe the fault.'],
  }

  it('reads the first message for a field', () => {
    expect(firstFieldError(errors, 'siteAddress')).toBe('Enter the site address.')
    expect(firstFieldError(errors, 'title')).toBeNull()
  })

  it('lists the messages no input shows', () => {
    expect(unmappedFieldErrors(errors, ['siteAddress'])).toEqual(['Describe the fault.'])
  })
})

describe('isApiError', () => {
  it('recognizes only ApiError instances', () => {
    expect(isApiError(networkError())).toBe(true)
    expect(isApiError(new Error('Request failed'))).toBe(false)
    expect(isApiError({ status: 400, code: 'request.validation-failed' })).toBe(false)
  })
})

describe('unwrap', () => {
  it('returns the data of a successful response', async () => {
    await expect(
      unwrap(Promise.resolve({ data: { id: 'a' }, response: response(200) })),
    ).resolves.toEqual({
      id: 'a',
    })
  })

  it('throws an ApiError for an error response', async () => {
    const pending = Promise.resolve({
      error: { code: 'auth.invalid-credentials', correlationId },
      response: response(401),
    })

    await expect(unwrap(pending)).rejects.toMatchObject({
      status: 401,
      code: 'auth.invalid-credentials',
      correlationId,
    })
  })

  it('throws an ApiError for an error response without a body', async () => {
    await expect(unwrapEmpty(Promise.resolve({ response: response(403) }))).rejects.toMatchObject({
      status: 403,
      code: null,
    })
  })

  it('turns a failed fetch into a network ApiError', async () => {
    const error: unknown = await unwrap(Promise.reject(new TypeError('fetch failed'))).catch(
      (reason: unknown) => reason,
    )

    expect(isApiError(error) && error.isNetworkError).toBe(true)
  })

  it('rethrows errors that are not network failures', async () => {
    const failure = new SyntaxError('Unexpected token')

    await expect(unwrap(Promise.reject(failure))).rejects.toBe(failure)
  })

  it('resolves an empty success', async () => {
    await expect(unwrapEmpty(Promise.resolve({ response: response(204) }))).resolves.toBeUndefined()
  })

  it('hands back the response of an accepted request so its headers can be read', async () => {
    const accepted = response(202, { 'X-Correlation-ID': correlationId })

    const result = await unwrapResponse(Promise.resolve({ response: accepted }))

    expect(result).toBe(accepted)
    expect(result.headers.get('X-Correlation-ID')).toBe(correlationId)
  })

  it('throws the ApiError of a refused request instead of its response', async () => {
    const refused = response(429, { 'Retry-After': '30', 'X-Correlation-ID': correlationId })

    await expect(
      unwrapResponse(
        Promise.resolve({ error: { code: 'rate-limit.exceeded' }, response: refused }),
      ),
    ).rejects.toMatchObject({ status: 429, retryAfterSeconds: 30, correlationId })
    await expect(
      unwrapResponse(Promise.reject(new TypeError('fetch failed'))),
    ).rejects.toMatchObject({ status: 0 })
  })
})
