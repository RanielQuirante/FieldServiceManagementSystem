export interface VisibleTimeout {
  cancel(): void
}

function pageHidden(): boolean {
  return document.visibilityState === 'hidden'
}

export function createVisibleTimeout(callback: () => void, milliseconds: number): VisibleTimeout {
  let remaining = milliseconds
  let startedAt = 0
  let timer: ReturnType<typeof setTimeout> | undefined
  let finished = false

  function finish(): void {
    finished = true
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
    document.removeEventListener('visibilitychange', onVisibilityChange)
  }

  function run(): void {
    if (finished || timer !== undefined || pageHidden()) {
      return
    }
    startedAt = Date.now()
    timer = setTimeout(() => {
      timer = undefined
      finish()
      callback()
    }, remaining)
  }

  function pause(): void {
    if (timer === undefined) {
      return
    }
    clearTimeout(timer)
    timer = undefined
    remaining = Math.max(remaining - (Date.now() - startedAt), 0)
  }

  function onVisibilityChange(): void {
    if (pageHidden()) {
      pause()
    } else {
      run()
    }
  }

  document.addEventListener('visibilitychange', onVisibilityChange)
  run()

  return { cancel: finish }
}
