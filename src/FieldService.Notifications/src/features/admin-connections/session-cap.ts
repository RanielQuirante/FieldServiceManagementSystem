import type { AdminSocket } from '../../shared/socket-types.ts'

export type CancelSessionCap = () => void

export function startSessionCap(socket: AdminSocket, maxSeconds: number): CancelSessionCap {
  const timer = setTimeout(() => {
    socket.disconnect(true)
  }, maxSeconds * 1000)
  timer.unref()
  return () => {
    clearTimeout(timer)
  }
}
