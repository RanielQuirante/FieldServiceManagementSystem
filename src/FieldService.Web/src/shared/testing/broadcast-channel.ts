export interface InProcessBroadcastChannel extends EventTarget {
  readonly name: string
  onmessage: ((event: MessageEvent) => void) | null
  postMessage(message: unknown): void
  close(): void
}

export interface InProcessBroadcastChannelClass {
  new (name: string): InProcessBroadcastChannel
  readonly posted: { readonly name: string; readonly data: unknown }[]
}

export function createInProcessBroadcastChannel(): InProcessBroadcastChannelClass {
  const members = new Map<string, Set<Channel>>()
  const posted: { readonly name: string; readonly data: unknown }[] = []

  class Channel extends EventTarget implements InProcessBroadcastChannel {
    static readonly posted = posted

    readonly name: string

    onmessage: ((event: MessageEvent) => void) | null = null

    private closed = false

    constructor(name: string) {
      super()
      this.name = name
      const peers = members.get(name) ?? new Set<Channel>()
      peers.add(this)
      members.set(name, peers)
    }

    postMessage(message: unknown): void {
      if (this.closed) {
        throw new DOMException('The channel is closed', 'InvalidStateError')
      }
      const data = structuredClone(message)
      posted.push({ name: this.name, data })
      for (const peer of members.get(this.name) ?? []) {
        if (peer !== this) {
          queueMicrotask(() => peer.deliver(data))
        }
      }
    }

    close(): void {
      this.closed = true
      members.get(this.name)?.delete(this)
    }

    private deliver(data: unknown): void {
      if (this.closed) {
        return
      }
      const event = new MessageEvent('message', { data })
      this.onmessage?.(event)
      this.dispatchEvent(event)
    }
  }

  return Channel
}
