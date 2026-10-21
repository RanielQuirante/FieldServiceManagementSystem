import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

const importSource = /(?:\bfrom\s*|\bimport\s*\(?\s*)(['"])([^'"]+)\1/g

function applicationFiles(): string[] {
  return readdirSync(sourceRoot, { recursive: true, encoding: 'utf8' })
    .map((path) => path.split(sep).join('/'))
    .filter((path) => /\.(?:ts|vue)$/.test(path))
    .filter((path) => !path.split('/').includes('__tests__'))
    .filter((path) => !path.startsWith('shared/testing/'))
    .filter((path) => path !== 'shared/api/schema.gen.ts')
}

function importsTestingFolder(file: string): boolean {
  const source = readFileSync(join(sourceRoot, file), 'utf8')
  return Array.from(source.matchAll(importSource), (match) => match[2] ?? '').some((specifier) => {
    if (specifier.startsWith('@/')) {
      return specifier.startsWith('@/shared/testing')
    }
    if (!specifier.startsWith('.')) {
      return false
    }
    const target = resolve(dirname(join(sourceRoot, file)), specifier)
    return target.startsWith(join(sourceRoot, 'shared', 'testing'))
  })
}

describe('the testing folder', () => {
  it('finds the application source files', () => {
    expect(applicationFiles()).toEqual(
      expect.arrayContaining([
        'app/main.ts',
        'shared/api/auth-fetch.ts',
        'features/auth/session-store.ts',
      ]),
    )
  })

  it('is imported by no application file', () => {
    expect(applicationFiles().filter(importsTestingFolder)).toEqual([])
  })

  it('detects an import of the testing folder', () => {
    const probe = "import { stubFetch } from '@/shared/testing/fetch-stub'"

    expect(Array.from(probe.matchAll(importSource), (match) => match[2])).toEqual([
      '@/shared/testing/fetch-stub',
    ])
  })
})
