import { sendText } from '../../shared/http/responses.ts'
import type { Router } from '../../shared/http/router.ts'

export interface Readiness {
  isReady(): boolean
}

export function registerHealthRoutes(router: Router, readiness: Readiness): void {
  router.get('/health/live', (_request, response) => {
    sendText(response, 200, 'Healthy')
  })

  router.get('/health/ready', (_request, response) => {
    if (readiness.isReady()) {
      sendText(response, 200, 'Healthy')
    } else {
      sendText(response, 503, 'Unhealthy')
    }
  })
}
