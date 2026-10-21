import type { LocationQueryValue } from 'vue-router'

export const fallbackRedirect = '/'

function hasControlCharacter(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index)
    if (code <= 0x1f || (code >= 0x7f && code <= 0x9f)) {
      return true
    }
  }
  return false
}

function startsWithSingleSlash(value: string): boolean {
  const second = value.charAt(1)
  return value.startsWith('/') && second !== '/' && second !== '\\'
}

function resolveSameOrigin(value: string, origin: string): URL | null {
  if (!startsWithSingleSlash(value) || hasControlCharacter(value)) {
    return null
  }
  let target: URL
  try {
    target = new URL(value, origin)
  } catch {
    return null
  }
  return target.origin === origin ? target : null
}

export function safeRedirect(value: unknown, origin: string = window.location.origin): string {
  if (typeof value !== 'string') {
    return fallbackRedirect
  }
  const target = resolveSameOrigin(value, origin)
  if (target === null) {
    return fallbackRedirect
  }
  const path = `${target.pathname}${target.search}${target.hash}`
  const resolvedAgain = resolveSameOrigin(path, origin)
  if (resolvedAgain === null || resolvedAgain.href !== target.href) {
    return fallbackRedirect
  }
  return path
}

export function redirectFromQuery(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
): string {
  return safeRedirect(Array.isArray(value) ? value[0] : value)
}
