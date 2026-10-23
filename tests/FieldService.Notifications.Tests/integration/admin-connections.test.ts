import assert from 'node:assert/strict'
import { after, before, describe, test } from 'node:test'
import { eventTypes, freshEnvelope, readClientFixture } from '../support/fixtures.ts'
import {
  allowedOrigin,
  closeClients,
  connectAdministrator,
  connected,
  foreignOrigin,
  openClient,
  openManagerWithoutNamespace,
  openUntypedClient,
  outcomeOf,
  postEnvelope,
  received,
  recordEvents,
  refused,
  startServer,
  ticketFor,
  type ClientSocket,
  type TestServer,
} from '../support/server.ts'
import { epochSeconds } from '../support/tickets.ts'

function codeOf(error: { readonly data?: unknown }): unknown {
  const data = error.data
  return typeof data === 'object' && data !== null && 'code' in data ? data.code : undefined
}

describe('administrator sockets', () => {
  let server: TestServer
  const sockets: ClientSocket[] = []

  before(async () => {
    server = await startServer()
  })

  after(async () => {
    closeClients(sockets)
    await server.close()
  })

  async function administrator(origin?: string): Promise<ClientSocket> {
    const socket = await connectAdministrator(server, origin === undefined ? {} : { origin })
    sockets.push(socket)
    return socket
  }

  function client(options: Parameters<typeof openClient>[1]): ClientSocket {
    const socket = openClient(server, options)
    sockets.push(socket)
    return socket
  }

  test('two administrator sockets receive each event type with the client payload', async () => {
    const first = await administrator(allowedOrigin)
    const second = await administrator()
    const refusedSocket = client({ auth: {} })
    const refusedEvents = recordEvents(refusedSocket)
    await refused(refusedSocket)

    for (const type of eventTypes) {
      const { id, envelope } = freshEnvelope(type)
      const deliveries = [received(first, type), received(second, type)]

      const response = await postEnvelope(server, envelope)
      assert.deepEqual(await outcomeOf(response), { outcome: 'broadcast' })

      const expected = { ...readClientFixture(type), eventId: id }
      for (const payload of await Promise.all(deliveries)) {
        assert.deepEqual(payload, expected)
      }
    }
    assert.deepEqual(refusedEvents, [], 'a socket refused in the handshake receives nothing')
    closeClients([first, second])
  })

  test('a handshake without a ticket is refused with ticket_missing', async () => {
    for (const auth of [{}, { ticket: '' }, { ticket: null }, { ticket: 42 }]) {
      const error = await refused(client({ auth }))

      assert.equal(error.message, 'ticket_missing')
      assert.equal(codeOf(error), 'ticket_missing')
    }
  })

  test('an expired ticket is refused with ticket_invalid', async () => {
    const issuedAt = epochSeconds(server.clock.now()) - 120
    const ticket = await ticketFor(server, { issuedAt, expiresAt: issuedAt + 60 })

    assert.equal(codeOf(await refused(client({ auth: { ticket } }))), 'ticket_invalid')
  })

  test('a wrong-audience ticket is refused with ticket_invalid', async () => {
    const ticket = await ticketFor(server, { audience: 'fieldservice-api' })

    assert.equal(codeOf(await refused(client({ auth: { ticket } }))), 'ticket_invalid')
  })

  test('a reused ticket is refused with ticket_invalid', async () => {
    const ticket = await ticketFor(server)
    const first = client({ auth: { ticket } })
    await connected(first)

    assert.equal(codeOf(await refused(client({ auth: { ticket } }))), 'ticket_invalid')
    first.close()
  })

  test('a Technician ticket is refused with forbidden', async () => {
    const ticket = await ticketFor(server, { role: 'Technician' })

    const error = await refused(client({ auth: { ticket } }))

    assert.equal(error.message, 'forbidden')
    assert.equal(codeOf(error), 'forbidden')
  })

  test('a browser Origin outside SOCKET_ALLOWED_ORIGINS is refused before the ticket is read', async () => {
    const ticket = await ticketFor(server)

    const error = await refused(client({ auth: { ticket }, origin: foreignOrigin }))

    assert.equal(codeOf(error), undefined, 'an Origin refusal carries no code')
    const allowed = client({ auth: { ticket }, origin: 'http://127.0.0.1:5173' })
    await connected(allowed)
    allowed.close()
  })

  for (const origin of [
    'null',
    'http://localhost:5174',
    'http://localhost:5173/',
    'https://localhost:5173',
  ]) {
    test(`the Origin ${origin} is refused`, async () => {
      const ticket = await ticketFor(server)

      const error = await refused(client({ auth: { ticket }, origin }))

      assert.equal(codeOf(error), undefined)
    })
  }

  test('a client without an Origin is decided by its ticket alone', async () => {
    const withTicket = await administrator()
    assert.ok(withTicket.connected)
    withTicket.close()

    assert.equal(codeOf(await refused(client({ auth: {} }))), 'ticket_missing')
  })

  test('any namespace other than the default one is refused', async () => {
    const ticket = await ticketFor(server)

    const error = await refused(client({ auth: { ticket }, namespace: '/admin' }))

    assert.equal(error.message, 'Invalid namespace')
  })

  test('an HTTP long-polling request is refused by engine.io', async () => {
    const response = await fetch(`${server.baseUrl}/socket.io/?EIO=4&transport=polling`)

    assert.equal(response.status, 400)
    await response.arrayBuffer()
  })

  test('client-to-server events are ignored', async () => {
    const listener = await administrator()
    const listenerEvents = recordEvents(listener)
    const ticket = await ticketFor(server)
    const sender = openUntypedClient(server, { auth: { ticket } })
    await new Promise<void>((resolve, reject) => {
      sender.once('connect', resolve)
      sender.once('connect_error', reject)
    })

    try {
      const { envelope } = freshEnvelope('job.created')
      sender.emit('job.created', readClientFixture('job.created'))
      sender.emit('anything', { note: 'ignored' })

      const delivery = received(listener, 'job.created')
      assert.deepEqual(await outcomeOf(await postEnvelope(server, envelope)), {
        outcome: 'broadcast',
      })
      assert.equal((await delivery).eventId, envelope.id)
      assert.deepEqual(
        listenerEvents.map((event) => event.name),
        ['job.created'],
        'only the server broadcast reached the other socket',
      )
      assert.ok(sender.connected, 'the sender stays connected')
    } finally {
      sender.close()
      listener.close()
    }
  })

  test('a frame above 4 KB closes the socket', async () => {
    const ticket = await ticketFor(server)
    const sender = openUntypedClient(server, { auth: { ticket } })
    await new Promise<void>((resolve, reject) => {
      sender.once('connect', resolve)
      sender.once('connect_error', reject)
    })

    try {
      const closed = new Promise<string>((resolve) => {
        sender.once('disconnect', resolve)
      })
      sender.emit('note', 'x'.repeat(5_000))

      assert.equal(await closed, 'transport close')
    } finally {
      sender.close()
    }
  })

  test('an accepted socket joins administrators, the only room besides its own', async () => {
    const socket = await administrator()
    const serverSocket = server.io.sockets.sockets.get(socket.id ?? '')

    assert.ok(serverSocket !== undefined)
    assert.deepEqual([...serverSocket.rooms].sort(), ['administrators', serverSocket.id].sort())
    socket.close()
    await disconnectedOnServer(server, serverSocket.id)
  })
})

async function disconnectedOnServer(server: TestServer, socketId: string): Promise<void> {
  const deadline = Date.now() + 5_000
  while (server.io.sockets.sockets.has(socketId)) {
    if (Date.now() > deadline) {
      throw new Error('timed out waiting for the server to drop the socket')
    }
    await new Promise((resolve) => setTimeout(resolve, 10))
  }
}

describe('connections that never join a namespace', () => {
  let server: TestServer

  before(async () => {
    server = await startServer()
  })

  after(async () => {
    await server.close()
  })

  test('are closed after the 10-second connectTimeout', { timeout: 20_000 }, async () => {
    const manager = openManagerWithoutNamespace(server)
    const startedAt = Date.now()
    try {
      await new Promise<void>((resolve, reject) => {
        manager.open((error) => {
          if (error === undefined) {
            resolve()
          } else {
            reject(error)
          }
        })
      })
      const reason = await new Promise<string>((resolve) => {
        manager.once('close', resolve)
      })
      const elapsed = Date.now() - startedAt

      assert.equal(reason, 'transport close')
      assert.ok(elapsed >= 9_500, `closed after ${elapsed} ms`)
      assert.ok(elapsed < 15_000, `closed after ${elapsed} ms`)
    } finally {
      manager.engine?.close()
    }
  })
})

describe('refused handshakes', () => {
  test('leave no socket and no room member on the server', async () => {
    const server = await startServer()
    try {
      for (const auth of [{}, { ticket: await ticketFor(server, { role: 'Technician' }) }]) {
        const socket = openClient(server, { auth })
        await refused(socket)
        socket.close()
      }

      assert.equal(server.io.sockets.sockets.size, 0)
      assert.equal(server.io.sockets.adapter.rooms.get('administrators'), undefined)
    } finally {
      await server.close()
    }
  })
})
