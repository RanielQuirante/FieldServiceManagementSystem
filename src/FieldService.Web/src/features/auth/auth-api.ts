import { apiClient } from '@/shared/api/api-client'
import { unwrap, unwrapEmpty } from '@/shared/api/api-error'
import type { components } from '@/shared/api/schema.gen'

export type SessionResponse = components['schemas']['SessionResponse']

export type SessionUser = components['schemas']['SessionUser']

export type UserRole = components['schemas']['UserRole']

export function signIn(email: string, password: string): Promise<SessionResponse> {
  return unwrap(apiClient.POST('/api/auth/sign-in', { body: { email, password, client: 'Web' } }))
}

export function refreshSession(): Promise<SessionResponse> {
  return unwrap(apiClient.POST('/api/auth/refresh'))
}

export function signOut(): Promise<void> {
  return unwrapEmpty(apiClient.POST('/api/auth/sign-out'))
}
