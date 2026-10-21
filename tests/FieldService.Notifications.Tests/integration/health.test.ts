import assert from 'node:assert/strict'
import { after, before, describe, test } from 'node:test'
import { startServer, type TestServer } from '../support/server.ts'

interface Problem {
  readonly type: string
  readonly title: string
  readonly status: number
  readonly code: string
  readonly correlationId?: string
}

async function problemOf(response: Response): Promise<Problem> {
  assert.equal(response.headers.get('content-type'), 'application/problem+json')
  return (await response.json()) as Problem
}

function assertCommonHeaders(response: Response): void {
  assert.equal(response.headers.get('x-content-type-options'), 'nosniff')
  assert.equal(response.headers.get('cache-control'), 'no-store')
}

describe('health routes and routing', () => {
  let server: TestServer

  before(async () => {
    server = await startServer()
  })

  after(async () => {
    await server.close()
  })

  for (const path of ['/health/live', '/health/ready']) {
    test(`GET ${path} answers 200 Healthy as text/plain`, async () => {
      const response = await fetch(`${server.baseUrl}${path}`)

      assert.equal(response.status, 200)
      assert.equal(response.headers.get('content-type'), 'text/plain')
      assertCommonHeaders(response)
      assert.equal(await response.text(), 'Healthy')
    })
  }

  test('a query string does not change the matched route', async () => {
    const response = await fetch(`${server.baseUrl}/health/live?probe=1`)

    assert.equal(response.status, 200)
    assert.equal(await response.text(), 'Healthy')
  })

  test('an unknown path answers 404 resource.not-found as a problem', async () => {
    const response = await fetch(`${server.baseUrl}/health/unknown`)

    assert.equal(response.status, 404)
    assertCommonHeaders(response)
    assert.deepEqual(await problemOf(response), {
      type: 'about:blank',
      title: 'Not Found',
      status: 404,
      code: 'resource.not-found',
    })
  })

  test('a wrong method answers 405 request.method-not-allowed with Allow: GET', async () => {
    const response = await fetch(`${server.baseUrl}/health/live`, { method: 'POST' })

    assert.equal(response.status, 405)
    assert.equal(response.headers.get('allow'), 'GET')
    assertCommonHeaders(response)
    assert.deepEqual(await problemOf(response), {
      type: 'about:blank',
      title: 'Method Not Allowed',
      status: 405,
      code: 'request.method-not-allowed',
    })
  })

  test('a problem echoes a well-formed X-Correlation-ID and drops a malformed one', async () => {
    const echoed = await fetch(`${server.baseUrl}/missing`, {
      headers: { 'x-correlation-id': 'trace-0001' },
    })
    const dropped = await fetch(`${server.baseUrl}/missing`, {
      headers: { 'x-correlation-id': 'not valid!' },
    })

    assert.equal((await problemOf(echoed)).correlationId, 'trace-0001')
    assert.equal((await problemOf(dropped)).correlationId, undefined)
  })

  test('routing rejections are answered without an error log entry', () => {
    const failures = server.logs.entries.filter((entry) => server.logs.levelOf(entry) === 'error')

    assert.deepEqual(failures, [])
  })
})
