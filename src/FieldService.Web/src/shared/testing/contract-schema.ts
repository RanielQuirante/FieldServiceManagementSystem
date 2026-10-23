import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { packageRoot } from './contract-fixtures'

export const contractFile = resolve(packageRoot, '../../docs/contracts/openapi.v1.json')

type Schema = Readonly<Record<string, unknown>>

interface ContractDocument {
  readonly components: { readonly schemas: Readonly<Record<string, Schema>> }
}

const schemaPrefix = '#/components/schemas/'

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const instantPattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/

let cached: ContractDocument | null = null

function contract(): ContractDocument {
  cached ??= JSON.parse(readFileSync(contractFile, 'utf8')) as ContractDocument
  return cached
}

function isSchema(value: unknown): value is Schema {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function resolveSchema(schema: Schema): Schema {
  const reference = schema.$ref
  if (typeof reference !== 'string') {
    return schema
  }
  const name = reference.startsWith(schemaPrefix) ? reference.slice(schemaPrefix.length) : ''
  const target = contract().components.schemas[name]
  if (target === undefined) {
    throw new Error(`The contract has no schema named ${name}`)
  }
  return resolveSchema(target)
}

function kindOf(value: unknown): string {
  if (value === null) {
    return 'null'
  }
  if (Array.isArray(value)) {
    return 'array'
  }
  if (typeof value === 'number') {
    return Number.isInteger(value) ? 'integer' : 'number'
  }
  return typeof value
}

function declaredTypes(schema: Schema): string[] {
  const declared = schema.type
  if (typeof declared === 'string') {
    return [declared]
  }
  return Array.isArray(declared)
    ? declared.filter((entry): entry is string => typeof entry === 'string')
    : []
}

function checkFormat(schema: Schema, value: string, path: string, problems: string[]): void {
  if (schema.format === 'uuid' && !uuidPattern.test(value)) {
    problems.push(`${path}: "${value}" is not a uuid`)
  }
  if (
    schema.format === 'date-time' &&
    (!instantPattern.test(value) || Number.isNaN(Date.parse(value)))
  ) {
    problems.push(`${path}: "${value}" is not a date-time`)
  }
}

function checkObject(
  schema: Schema,
  value: Readonly<Record<string, unknown>>,
  path: string,
  problems: string[],
): void {
  const properties = isSchema(schema.properties) ? schema.properties : {}
  const required = Array.isArray(schema.required) ? schema.required : []
  for (const key of required) {
    if (typeof key === 'string' && !Object.prototype.hasOwnProperty.call(value, key)) {
      problems.push(`${path}.${key}: missing`)
    }
  }
  for (const [key, member] of Object.entries(value)) {
    const memberSchema = properties[key]
    if (!isSchema(memberSchema)) {
      problems.push(`${path}.${key}: not in the contract`)
      continue
    }
    check(memberSchema, member, `${path}.${key}`, problems)
  }
}

function check(raw: Schema, value: unknown, path: string, problems: string[]): void {
  const schema = resolveSchema(raw)
  if (Array.isArray(schema.oneOf)) {
    const matching = schema.oneOf.filter((option) => {
      const found: string[] = []
      if (isSchema(option)) {
        check(option, value, path, found)
      }
      return isSchema(option) && found.length === 0
    })
    if (matching.length !== 1) {
      problems.push(`${path}: matches ${matching.length} of the oneOf options`)
    }
    return
  }
  if (Array.isArray(schema.enum) && !schema.enum.includes(value)) {
    problems.push(`${path}: ${JSON.stringify(value)} is not one of ${JSON.stringify(schema.enum)}`)
    return
  }
  const kind = kindOf(value)
  const types = declaredTypes(schema)
  const allowed = types.includes(kind) || (kind === 'integer' && types.includes('number'))
  if (types.length > 0 && !allowed) {
    problems.push(`${path}: ${kind} is not ${types.join(' or ')}`)
    return
  }
  if (typeof value === 'string') {
    checkFormat(schema, value, path, problems)
  } else if (Array.isArray(value)) {
    const itemSchema = isSchema(schema.items) ? schema.items : {}
    value.forEach((entry: unknown, index) => {
      check(itemSchema, entry, `${path}[${index}]`, problems)
    })
  } else if (isSchema(value)) {
    checkObject(schema, value, path, problems)
  }
}

export function contractProblems(schemaName: string, value: unknown): string[] {
  const problems: string[] = []
  check({ $ref: `${schemaPrefix}${schemaName}` }, value, schemaName, problems)
  return problems
}
