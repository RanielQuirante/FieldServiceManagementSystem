import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

export const tokensFile = 'app/styles/tokens.css'

export const fontsFile = 'app/styles/fonts.css'

export interface StyleSource {
  file: string
  css: string
}

export interface Declaration {
  property: string
  value: string
}

const vueStyleBlock = /<style\b[^>]*>([\s\S]*?)<\/style\s*>/gi

const quotedString = /(["'])(?:\\.|(?!\1)[^\\\n])*\1/g

const declaration = /(?<=[{;}])(\s*(--[\w-]+|-?[a-z][\w-]*)\s*:\s*)([^;{}]*?)\s*(?=[;}])/gi

const tokenReference = /var\(\s*(--[\w-]+)/g

export function readSource(file: string): string {
  return readFileSync(join(sourceRoot, file), 'utf8')
}

export function listSourceFiles(extension: string): string[] {
  return readdirSync(sourceRoot, { recursive: true, encoding: 'utf8' })
    .map((path) => path.split(sep).join('/'))
    .filter((path) => path.endsWith(extension))
    .sort()
}

export function extractVueStyles(component: string): string {
  return Array.from(component.matchAll(vueStyleBlock), (match) => match[1] ?? '').join('\n')
}

export function readStyleSources(): StyleSource[] {
  const stylesheets = listSourceFiles('.css').map((file) => ({ file, css: readSource(file) }))
  const components = listSourceFiles('.vue').map((file) => ({
    file,
    css: extractVueStyles(readSource(file)),
  }))
  return [...stylesheets, ...components]
}

export function maskStrings(css: string): string {
  return css.replace(quotedString, (text) => `"${'_'.repeat(Math.max(text.length - 2, 0))}"`)
}

export function parseDeclarations(css: string): Declaration[] {
  return Array.from(maskStrings(css).matchAll(declaration), (match) => {
    const start = (match.index ?? 0) + (match[1] ?? '').length
    return {
      property: (match[2] ?? '').toLowerCase(),
      value: css.slice(start, start + (match[3] ?? '').length),
    }
  })
}

export function readStyles(file: string): string {
  return file.endsWith('.vue') ? extractVueStyles(readSource(file)) : readSource(file)
}

export function blockOf(css: string, prelude: string): string {
  const start = css.indexOf(`${prelude} {`)
  if (start < 0) {
    return ''
  }
  const open = css.indexOf('{', start)
  let depth = 0
  for (let index = open; index < css.length; index += 1) {
    if (css[index] === '{') {
      depth += 1
    } else if (css[index] === '}') {
      depth -= 1
      if (depth === 0) {
        return css.slice(open + 1, index)
      }
    }
  }
  return ''
}

export function ruleDeclarations(
  file: string,
  selector: string,
  within?: string,
): Record<string, string> {
  const css = within === undefined ? readStyles(file) : blockOf(readStyles(file), within)
  const rule = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g)).find(
    (match) => match[1]?.trim() === selector,
  )
  return Object.fromEntries(
    parseDeclarations(`{${rule?.[2] ?? ''}}`).map(({ property, value }) => [property, value]),
  )
}

export function installStyles(...files: string[]): () => void {
  const element = document.createElement('style')
  element.textContent = files.map(readStyles).join('\n')
  document.head.append(element)
  return () => element.remove()
}

export function findTokenReferences(css: string): string[] {
  return Array.from(css.matchAll(tokenReference), (match) => match[1] ?? '')
}

export function readTokens(css: string = readSource(tokensFile)): Map<string, string> {
  const tokens = new Map<string, string>()
  for (const { property, value } of parseDeclarations(css)) {
    if (property.startsWith('--')) {
      tokens.set(property, value)
    }
  }
  return tokens
}

export function resolveTokens(
  value: string,
  tokens: ReadonlyMap<string, string>,
  depth = 0,
): string {
  if (depth > 16) {
    throw new Error(`Token references nest too deeply in "${value}"`)
  }
  return value.replace(/var\(\s*(--[\w-]+)\s*\)/g, (_reference, name: string) => {
    const referenced = tokens.get(name)
    if (referenced === undefined) {
      throw new Error(`Token ${name} is not defined in ${tokensFile}`)
    }
    return resolveTokens(referenced, tokens, depth + 1)
  })
}

export function resolveToken(name: string, tokens: ReadonlyMap<string, string>): string {
  const value = tokens.get(name)
  if (value === undefined) {
    throw new Error(`Token ${name} is not defined in ${tokensFile}`)
  }
  return resolveTokens(value, tokens).replace(/\s+/g, ' ').trim()
}
