import { createServer, type AddressInfo } from 'node:net'

export interface AddressSource {
  address(): AddressInfo | string | null
}

export function boundPort(server: AddressSource): number {
  const address = server.address()
  if (address === null || typeof address === 'string') {
    throw new Error('the server is not listening on a TCP port')
  }
  return address.port
}

export function findFreePort(): Promise<number> {
  return new Promise<number>((resolve, reject) => {
    const probe = createServer()
    probe.once('error', reject)
    probe.listen(0, '127.0.0.1', () => {
      const port = boundPort(probe)
      probe.close(() => {
        resolve(port)
      })
    })
  })
}
