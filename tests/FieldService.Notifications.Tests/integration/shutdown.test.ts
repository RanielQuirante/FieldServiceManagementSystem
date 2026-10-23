import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { closeClients, connectAdministrator, disconnected, startServer } from '../support/server.ts'

describe('close()', () => {
  test('disconnects every socket, stops listening and logs each disconnect', async () => {
    const server = await startServer()
    const sockets = [await connectAdministrator(server), await connectAdministrator(server)]
    const socketIds = sockets.map((socket) => socket.id)
    const reasons = sockets.map((socket) => disconnected(socket))
    server.clock.advance(2_500)

    await server.close()

    try {
      for (const reason of await Promise.all(reasons)) {
        assert.equal(reason, 'transport close')
      }
      assert.equal(server.httpServer.listening, false)
      await assert.rejects(fetch(`${server.baseUrl}/health/live`))

      const entries = server.logs.all('socket.disconnected')
      assert.deepEqual(entries.map((entry) => entry.socketId).sort(), [...socketIds].sort())
      for (const entry of entries) {
        assert.equal(entry.reason, 'server shutting down')
        assert.equal(entry.durationMs, 2_500)
      }
    } finally {
      closeClients(sockets)
    }
  })

  test('releases the port, so a second server can bind it', async () => {
    const first = await startServer()
    const socket = await connectAdministrator(first)
    await first.close()
    closeClients([socket])

    const second = await startServer({ PORT: String(first.port) })
    try {
      const response = await fetch(`${second.baseUrl}/health/ready`)
      assert.equal(response.status, 200)
      assert.equal(await response.text(), 'Healthy')
    } finally {
      await second.close()
    }
  })
})
