import { describe, expect, it } from 'vitest'

import { fallbackRedirect, redirectFromQuery, safeRedirect } from '../redirect'

const origin = window.location.origin

describe('safeRedirect', () => {
  it.each(['/jobs/abc', '/jobs', '/', '/jobs?status=OnHold&page=2', '/jobs/new#schedule'])(
    'accepts the same-origin path %s',
    (value) => {
      expect(safeRedirect(value)).toBe(value)
    },
  )

  it.each([
    ['a protocol-relative URL', '//evil.example'],
    ['a protocol-relative URL with a path', '//evil.example/jobs'],
    ['a backslash after the slash', '/\\evil.example'],
    ['an absolute URL on another origin', 'https://evil.example'],
    ['an absolute URL on this origin', `${origin}/jobs`],
    ['a javascript URL', 'javascript:alert(1)'],
    ['an empty value', ''],
    ['a relative path without a leading slash', 'jobs/abc'],
    ['a path behind a leading space', ' /jobs'],
    ['a leading backslash', '\\evil.example'],
    ['two leading backslashes', '\\\\evil.example'],
    ['a leading backslash before a slash', '\\/evil.example'],
  ])('rejects %s', (_description, value) => {
    expect(safeRedirect(value)).toBe(fallbackRedirect)
  })

  it.each([
    '/..//evil.example',
    '/.//evil.example',
    '/%2e%2e//evil.example',
    '/./\\evil.example',
    '/jobs/..//evil.example/x',
    '/%2E%2E//evil.example',
    '/.%2e//evil.example',
    '/.\\/evil.example',
    '/jobs/../..//evil.example',
    '/jobs/%2e%2e/%2e%2e//evil.example',
  ])('rejects a value whose dot segments normalize to a protocol-relative path: %s', (value) => {
    expect(safeRedirect(value)).toBe(fallbackRedirect)
    expect(safeRedirect(value, 'https://admin.fieldservice.example')).toBe(fallbackRedirect)
  })

  it('never returns a path that leaves the origin, whatever dot segments and slashes it gets', () => {
    const prefixes = ['/', '/.', '/..', '/%2e', '/%2E%2e', '/jobs/..', '/jobs/../..', '/./.']
    const separators = ['//', '/\\', '\\/', '\\\\', '///']
    const targets = ['evil.example', 'evil.example/jobs?page=2#notes']
    for (const prefix of prefixes) {
      for (const separator of separators) {
        for (const target of targets) {
          const result = safeRedirect(`${prefix}${separator}${target}`)

          expect(result).toMatch(/^\/(?![/\\])/)
          expect(new URL(result, origin).origin).toBe(origin)
        }
      }
    }
  })

  it.each([
    ['/jobs/./abc', '/jobs/abc'],
    ['/%2e%2e/jobs', '/jobs'],
    ['/jobs/../../jobs/abc?page=2', '/jobs/abc?page=2'],
  ])('accepts %s, whose dot segments keep a single leading slash, as %s', (value, expected) => {
    expect(safeRedirect(value)).toBe(expected)
  })

  it.each([
    ['a tab', '/\tevil.example'],
    ['a line feed', '/jobs\n'],
    ['a carriage return', '/jobs\r/x'],
    ['a null character', '/jobs\u0000'],
    ['the delete character', '/jobs\u007f'],
    ['a C1 control character', '/jobs\u0085'],
  ])('rejects a value with %s', (_description, value) => {
    expect(safeRedirect(value)).toBe(fallbackRedirect)
  })

  it.each([
    ['undefined', undefined],
    ['null', null],
    ['a number', 42],
    ['an array', ['/jobs']],
    ['an object', { path: '/jobs' }],
  ])('rejects %s', (_description, value) => {
    expect(safeRedirect(value)).toBe(fallbackRedirect)
  })

  it('returns the normalized path, query and fragment', () => {
    expect(safeRedirect('/jobs/../technicians?search=Elm Street')).toBe(
      '/technicians?search=Elm%20Street',
    )
  })

  it('compares against the origin it is given', () => {
    expect(safeRedirect('/jobs', 'https://admin.fieldservice.example')).toBe('/jobs')
    expect(
      safeRedirect('//admin.fieldservice.example/jobs', 'https://admin.fieldservice.example'),
    ).toBe(fallbackRedirect)
  })

  it('falls back to the dashboard path', () => {
    expect(fallbackRedirect).toBe('/')
  })
})

describe('redirectFromQuery', () => {
  it('reads a single query value', () => {
    expect(redirectFromQuery('/jobs/abc')).toBe('/jobs/abc')
  })

  it('reads the first of repeated query values', () => {
    expect(redirectFromQuery(['/jobs', '//evil.example'])).toBe('/jobs')
    expect(redirectFromQuery(['//evil.example', '/jobs'])).toBe(fallbackRedirect)
  })

  it('falls back when the query value normalizes to a protocol-relative path', () => {
    expect(redirectFromQuery('/..//evil.example')).toBe(fallbackRedirect)
    expect(redirectFromQuery(['/./\\evil.example', '/jobs'])).toBe(fallbackRedirect)
  })

  it('falls back when the query has no usable value', () => {
    expect(redirectFromQuery(undefined)).toBe(fallbackRedirect)
    expect(redirectFromQuery(null)).toBe(fallbackRedirect)
    expect(redirectFromQuery([])).toBe(fallbackRedirect)
  })
})
