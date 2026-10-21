export interface GrantedLock {
  readonly name: string
  readonly mode: LockMode
}

type LockCallback<T> = (lock: Lock | null) => T

export class InMemoryLockManager {
  readonly granted: GrantedLock[] = []

  private readonly queues = new Map<string, Promise<void>>()

  request<T>(name: string, callback: LockCallback<T>): Promise<Awaited<T>>
  request<T>(name: string, options: LockOptions, callback: LockCallback<T>): Promise<Awaited<T>>
  request<T>(
    name: string,
    optionsOrCallback: LockOptions | LockCallback<T>,
    maybeCallback?: LockCallback<T>,
  ): Promise<Awaited<T>> {
    const callback = typeof optionsOrCallback === 'function' ? optionsOrCallback : maybeCallback
    const options = typeof optionsOrCallback === 'function' ? {} : optionsOrCallback
    if (callback === undefined) {
      return Promise.reject(new TypeError('A lock request needs a callback'))
    }
    const lock: GrantedLock = { name, mode: options.mode ?? 'exclusive' }
    const previous = this.queues.get(name) ?? Promise.resolve()
    const run = previous.then(async (): Promise<Awaited<T>> => {
      this.granted.push(lock)
      return await callback(lock)
    })
    this.queues.set(
      name,
      run.then(
        () => undefined,
        () => undefined,
      ),
    )
    return run
  }

  grantsOf(name: string): number {
    return this.granted.filter((lock) => lock.name === name).length
  }
}

let installed: InMemoryLockManager | null = null

export function installLockManager(): InMemoryLockManager {
  installed = new InMemoryLockManager()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: installed })
  return installed
}

export function removeLockManager(): void {
  installed = null
  Reflect.deleteProperty(navigator, 'locks')
}

export function lockManager(): InMemoryLockManager {
  if (installed === null) {
    throw new Error('No lock manager is installed')
  }
  return installed
}
