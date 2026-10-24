import type { App } from 'vue'
import type { Router } from 'vue-router'

import { reportError, type ErrorSource } from '@/features/diagnostics/error-reporter'

export type RemoveErrorHandling = () => void

export function installErrorHandling(
  app: App,
  router: Router,
  target: Window = window,
): RemoveErrorHandling {
  const handle = (error: unknown, source: ErrorSource, route: string): void => {
    reportError(error, source, route)

    if (import.meta.env.DEV) {
      console.error(`[${source}]`, error)
    }
  }

  app.config.errorHandler = (error) => {
    handle(error, 'app.errorHandler', router.currentRoute.value.path)
  }

  const onUnhandledRejection = (event: PromiseRejectionEvent): void => {
    handle(event.reason, 'window.unhandledrejection', router.currentRoute.value.path)
  }

  target.addEventListener('unhandledrejection', onUnhandledRejection)

  const removeRouterErrorHandler = router.onError((error, to) => {
    handle(error, 'router.onError', to.path)
  })

  return () => {
    target.removeEventListener('unhandledrejection', onUnhandledRejection)
    removeRouterErrorHandler()
    app.config.errorHandler = undefined
  }
}
