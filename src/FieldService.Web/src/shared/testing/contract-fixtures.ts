import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

export const clientFixtureFolder = resolve(packageRoot, '../../docs/contracts/events/client')

export interface ClientFixture {
  readonly name: string
  readonly file: string
  readonly payload: Record<string, unknown>
}

export function readClientFixtures(): ClientFixture[] {
  return readdirSync(clientFixtureFolder)
    .filter((file) => file.endsWith('.json'))
    .sort()
    .map((file) => ({
      name: file.slice(0, -'.json'.length),
      file,
      payload: JSON.parse(readFileSync(join(clientFixtureFolder, file), 'utf8')) as Record<
        string,
        unknown
      >,
    }))
}

export function readClientFixture(name: string): Record<string, unknown> {
  const fixture = readClientFixtures().find((candidate) => candidate.name === name)
  if (fixture === undefined) {
    throw new Error(`No client fixture named ${name}`)
  }
  return fixture.payload
}
