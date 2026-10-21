import createClient from 'openapi-fetch'

import { authFetch } from './auth-fetch'
import type { paths } from './schema.gen'

export const apiClient = createClient<paths>({
  baseUrl: window.location.origin,
  fetch: authFetch,
})
