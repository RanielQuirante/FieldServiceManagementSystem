import { flushPromises } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, type Router } from 'vue-router'

import { createAppRouter, installNavigationGuards, routes } from '@/app/router'
import {
  accessToken,
  demoAdministratorEmail,
  password,
  sessionResponse,
  sessionResponseBody,
} from '@/features/auth/__tests__/session-fixtures'
import { useSessionStore } from '@/features/auth/session-store'
import { sessionRefreshLockName } from '@/features/auth/session-sync'
import {
  deferred,
  json,
  networkFailure,
  noContent,
  problem,
  stubFetch,
  text,
  type Reply,
  type RouteTable,
} from '@/shared/testing/fetch-stub'
import { lockManager } from '@/shared/testing/web-locks'

import { apiClient } from '../api-client'
import { isApiError, unwrap } from '../api-error'
import { authFetch, connectSessionAccess } from '../auth-fetch'
import type { components } from '../schema.gen'

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/

const templates = 'GET /api/checklist-templates'

const createJob = 'POST /api/jobs'

const jobRequest: components['schemas']['CreateJobRequest'] = {
  title: 'Exterior signage lights out',
  description: 'Exterior signage lights are out along the north entrance.',
  priority: 'Normal',
  customerName: 'Riverside Shopping Center',
  siteAddress: '500 Riverside Boulevard',
  contactName: 'Grace Liu',
  contactPhone: '(555) 014-8812',
  scheduledStart: '2026-10-22T13:00:00.000Z',
  scheduledEnd: '2026-10-22T15:00:00.000Z',
  checklistTemplateId: '01a0f6b1-3a10-7c41-9d6e-4b8a2f5c7e10',
}

function acceptOnly(token: string, reply: Reply): Reply {
  return (request) =>
    request.headers.get('Authorization') === `Bearer ${token}`
      ? reply(request)
      : problem(401, 'auth.unauthenticated')(request)
}

async function signInWith(token: string, routeTable: RouteTable) {
  const stub = stubFetch({ 'POST /api/auth/sign-in': json(sessionResponse(token)), ...routeTable })
  await useSessionStore().signIn(demoAdministratorEmail, password())
  return stub
}

function protectedRouter(): Router {
  const router = createAppRouter(createMemoryHistory(), [
    ...routes,
    {
      path: '/jobs',
      name: 'jobs',
      component: { render: () => null },
      meta: { requiresAdministrator: true, title: 'Jobs' },
    },
  ])
  installNavigationGuards(router)
  return router
}

describe('authFetch request headers', () => {
  it('adds the bearer token and a new correlation id to each API request', async () => {
    const token = accessToken()
    const stub = await signInWith(token, { [templates]: json({ items: [] }) })

    await apiClient.GET('/api/checklist-templates')
    await apiClient.GET('/api/checklist-templates')

    const [first, second] = stub.requestsTo(templates)
    expect(first?.headers.get('Authorization')).toBe(`Bearer ${token}`)
    expect(second?.headers.get('Authorization')).toBe(`Bearer ${token}`)
    expect(first?.headers.get('X-Correlation-ID')).toMatch(uuid)
    expect(second?.headers.get('X-Correlation-ID')).toMatch(uuid)
    expect(first?.headers.get('X-Correlation-ID')).not.toBe(second?.headers.get('X-Correlation-ID'))
  })

  it('sends sign-in without a bearer token or the anti-forgery header', async () => {
    const stub = await signInWith(accessToken(), {})

    const [signIn] = stub.requestsTo('POST /api/auth/sign-in')
    expect(signIn?.headers.get('Authorization')).toBeNull()
    expect(signIn?.headers.get('X-FieldService-Csrf')).toBeNull()
    expect(signIn?.headers.get('X-Correlation-ID')).toMatch(uuid)
    expect(signIn?.headers.get('Content-Type')).toBe('application/json')
  })

  it('sends refresh and sign-out with the anti-forgery header and no bearer token or body', async () => {
    const stub = await signInWith(accessToken(), {
      'POST /api/auth/refresh': json(sessionResponse()),
      'POST /api/auth/sign-out': noContent(),
    })

    await useSessionStore().refreshSession()
    await useSessionStore().signOut()

    for (const request of [
      ...stub.requestsTo('POST /api/auth/refresh'),
      ...stub.requestsTo('POST /api/auth/sign-out'),
    ]) {
      expect(request.headers.get('X-FieldService-Csrf')).toBe('1')
      expect(request.headers.get('Authorization')).toBeNull()
      expect(request.headers.get('X-Correlation-ID')).toMatch(uuid)
      expect(request.body).toBe('')
    }
    expect(stub.requests).toHaveLength(3)
  })

  it('leaves requests outside the API untouched', async () => {
    const token = accessToken()
    const stub = await signInWith(token, { 'GET /assets/logo.svg': text('<svg></svg>', 200) })

    await authFetch(new Request('https://tiles.example/assets/logo.svg'))
    await authFetch(new Request(`${window.location.origin}/assets/logo.svg`))

    for (const request of stub.requestsTo('GET /assets/logo.svg')) {
      expect(request.headers.get('Authorization')).toBeNull()
      expect(request.headers.get('X-Correlation-ID')).toBeNull()
    }
  })

  it('sends no bearer token before a session exists', async () => {
    const stub = stubFetch({ [templates]: problem(401, 'auth.unauthenticated') })

    const response = await authFetch(
      new Request(`${window.location.origin}/api/checklist-templates`),
    )

    expect(response.status).toBe(401)
    expect(stub.requestsTo(templates)[0]?.headers.get('Authorization')).toBeNull()
  })
})

describe('authFetch on 401', () => {
  it('refreshes once under the Web Lock for two concurrent 401s and retries each request once', async () => {
    const first = accessToken()
    const renewed = accessToken()
    const refresh = deferred()
    const stub = await signInWith(first, {
      'POST /api/auth/refresh': refresh.reply,
      [templates]: acceptOnly(renewed, json({ items: [] })),
    })

    const one = apiClient.GET('/api/checklist-templates')
    const two = apiClient.GET('/api/checklist-templates')
    await refresh.requested
    await flushPromises()
    refresh.resolve(sessionResponseBody(renewed))
    const results = await Promise.all([one, two])

    expect(results.map(({ response }) => response.status)).toEqual([200, 200])
    expect(stub.requestsTo('POST /api/auth/refresh')).toHaveLength(1)
    expect(lockManager().grantsOf(sessionRefreshLockName)).toBe(1)
    const calls = stub.requestsTo(templates)
    expect(calls.map((call) => call.headers.get('Authorization'))).toEqual([
      `Bearer ${first}`,
      `Bearer ${first}`,
      `Bearer ${renewed}`,
      `Bearer ${renewed}`,
    ])
    expect(new Set(calls.map((call) => call.headers.get('X-Correlation-ID'))).size).toBe(4)
  })

  it('retries with the newer token when another request already refreshed the session', async () => {
    const sent = accessToken()
    const newer = accessToken()
    let current = sent
    const renewAccessToken = vi.fn(async () => accessToken())
    connectSessionAccess({ currentAccessToken: () => current, renewAccessToken })
    const stub = stubFetch({
      [templates]: [
        (request) => {
          current = newer
          return problem(401, 'auth.unauthenticated')(request)
        },
        json({ items: [] }),
      ],
    })

    const response = await authFetch(
      new Request(`${window.location.origin}/api/checklist-templates`),
    )

    expect(response.status).toBe(200)
    expect(renewAccessToken).not.toHaveBeenCalled()
    expect(stub.requestsTo(templates)[1]?.headers.get('Authorization')).toBe(`Bearer ${newer}`)
  })

  it('retries only once when the retried request is also refused', async () => {
    const stub = await signInWith(accessToken(), {
      'POST /api/auth/refresh': json(sessionResponse()),
      [templates]: problem(401, 'auth.unauthenticated'),
    })

    const { response } = await apiClient.GET('/api/checklist-templates')

    expect(response.status).toBe(401)
    expect(stub.requestsTo(templates)).toHaveLength(2)
    expect(stub.requestsTo('POST /api/auth/refresh')).toHaveLength(1)
  })

  it('replays the request body on the retry', async () => {
    const renewed = accessToken()
    const stub = await signInWith(accessToken(), {
      'POST /api/auth/refresh': json(sessionResponse(renewed)),
      [createJob]: acceptOnly(renewed, json({ id: 'created' }, 201)),
    })

    const { response } = await apiClient.POST('/api/jobs', { body: jobRequest })

    expect(response.status).toBe(201)
    const [refused, retried] = stub.requestsTo(createJob)
    expect(JSON.parse(retried?.body ?? '')).toEqual(jobRequest)
    expect(retried?.body).toBe(refused?.body)
  })

  it('clears the session and routes to sign-in when the refresh is refused', async () => {
    const stub = stubFetch({
      'POST /api/auth/refresh': [json(sessionResponse()), problem(401, 'auth.session-expired')],
      [templates]: problem(401, 'auth.unauthenticated'),
    })
    const router = protectedRouter()
    await router.push('/jobs')
    const session = useSessionStore()
    expect(session.isSignedIn).toBe(true)

    const { response } = await apiClient.GET('/api/checklist-templates')

    expect(response.status).toBe(401)
    expect(session.isSignedIn).toBe(false)
    expect(session.endReason).toBe('expired')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('sign-in'))
    expect(router.currentRoute.value.query.redirect).toBe('/jobs')
    expect(stub.requestsTo(templates)).toHaveLength(1)
  })

  it('keeps the session and returns the 401 when the refresh cannot reach the API', async () => {
    const stub = await signInWith(accessToken(), {
      'POST /api/auth/refresh': networkFailure(),
      [templates]: problem(401, 'auth.unauthenticated'),
    })

    const { response } = await apiClient.GET('/api/checklist-templates')

    expect(response.status).toBe(401)
    expect(useSessionStore().isSignedIn).toBe(true)
    expect(stub.requestsTo(templates)).toHaveLength(1)
  })
})

describe('API errors through the client', () => {
  it('turns Problem Details into an ApiError with status, code, field errors and correlation id', async () => {
    const stub = await signInWith(accessToken(), {
      [createJob]: problem(400, 'request.validation-failed', {
        title: 'One or more fields are invalid.',
        errors: { siteAddress: ['Enter the site address.'] },
      }),
    })

    const error: unknown = await unwrap(apiClient.POST('/api/jobs', { body: jobRequest })).catch(
      (reason: unknown) => reason,
    )

    expect(isApiError(error)).toBe(true)
    expect(error).toMatchObject({
      status: 400,
      code: 'request.validation-failed',
      fieldErrors: { siteAddress: ['Enter the site address.'] },
      correlationId: stub.requestsTo(createJob)[0]?.headers.get('X-Correlation-ID'),
    })
  })

  it('turns a body that is not JSON into a generic ApiError', async () => {
    const stub = await signInWith(accessToken(), {
      [templates]: text('<html>Bad gateway</html>', 502),
    })

    const error: unknown = await unwrap(apiClient.GET('/api/checklist-templates')).catch(
      (reason: unknown) => reason,
    )

    expect(error).toMatchObject({
      status: 502,
      code: null,
      fieldErrors: {},
      correlationId: stub.requestsTo(templates)[0]?.headers.get('X-Correlation-ID'),
    })
  })

  it('turns an unreachable API into a network ApiError', async () => {
    await signInWith(accessToken(), { [templates]: networkFailure() })

    const error: unknown = await unwrap(apiClient.GET('/api/checklist-templates')).catch(
      (reason: unknown) => reason,
    )

    expect(isApiError(error) && error.isNetworkError).toBe(true)
  })
})
