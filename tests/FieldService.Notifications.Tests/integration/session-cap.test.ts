import assert from 'node:assert/strict'
import { after, before, describe, test } from 'node:test'
import {
  closeClients,
  connectAdministrator,
  disconnected,
  startServer,
  type TestServer,
} from '../support/server.ts'

async function waitForLog(server: TestServer, message: string, count: number): Promise<void> {
  const deadline = Date.now() + 3_000
  while (server.logs.all(message).length < count) {
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for ${count} ${message} entries`)
    }
    await new Promise((resolve) => setTimeout(resolve, 10))
  }
}

describe('session cap', () => {
  let server: TestServer

  before(async () => {
    server = await startServer({ SOCKET_SESSION_MAX_SECONDS: '1' })
  })

  after(async () => {
    await server.close()
  })

  test(
    'disconnects the socket with io server disconnect within 3 s and logs the duration',
    { timeout: 10_000 },
    async () => {
      const socket = await connectAdministrator(server)
      const socketId = socket.id
      const reason = disconnected(socket, 3_000)
      server.clock.advance(1_000)

      try {
        assert.equal(await reason, 'io server disconnect')
        await waitForLog(server, 'socket.disconnected', 1)
        const entry = server.logs
          .all('socket.disconnected')
          .find((line) => line.socketId === socketId)
        assert.ok(entry !== undefined)
        assert.equal(server.logs.levelOf(entry), 'info')
        assert.equal(entry.reason, 'server namespace disconnect')
        assert.equal(entry.durationMs, 1_000)
        assert.equal(typeof entry.userId, 'string')
      } finally {
        closeClients([socket])
      }
    },
  )

  test(
    'a socket that leaves first is logged once, with its own reason',
    { timeout: 10_000 },
    async () => {
      const before = server.logs.all('socket.disconnected').length
      const socket = await connectAdministrator(server)
      const socketId = socket.id
      socket.disconnect()

      await waitForLog(server, 'socket.disconnected', before + 1)
      await new Promise((resolve) => setTimeout(resolve, 1_500))

      const entries = server.logs
        .all('socket.disconnected')
        .filter((line) => line.socketId === socketId)
      assert.equal(entries.length, 1)
      assert.equal(entries[0]?.reason, 'client namespace disconnect')
    },
  )
})

describe('session cap at the default', () => {
  test('keeps the socket connected well before 900 s', async () => {
    const server = await startServer()
    const socket = await connectAdministrator(server)
    try {
      await new Promise((resolve) => setTimeout(resolve, 1_200))

      assert.ok(socket.connected)
    } finally {
      closeClients([socket])
      await server.close()
    }
  })
})
