import { existsSync } from 'node:fs'
import { join } from 'node:path'

import { describe, expect, it } from 'vitest'

import {
  findTokenReferences,
  fontsFile,
  listSourceFiles,
  maskStrings,
  parseDeclarations,
  readSource,
  readStyleSources,
  readTokens,
  sourceRoot,
  tokensFile,
} from './stylesheet-source'

const namedColors = new Set(
  [
    'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue',
    'blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk',
    'crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki',
    'darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen',
    'darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue',
    'dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite',
    'gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki',
    'lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan',
    'lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen',
    'lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen',
    'magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen',
    'mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream',
    'mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid',
    'palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum',
    'powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown',
    'seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen',
    'steelblue tan teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen',
  ]
    .join(' ')
    .split(' '),
)

const hexColor = /#[0-9a-f]{3,8}\b/i

const colorFunction = /\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color|color-mix)\(/i

const gradientFunction = /\b(?:repeating-)?(?:linear|radial|conic)-gradient\(/i

const colorProperty =
  /^(?:--|color$|background|border|outline|box-shadow|text-shadow|-webkit-box-shadow|fill$|stroke$|caret-color$|accent-color$|text-decoration|column-rule|-webkit-text-fill-color$|-webkit-text-stroke|scrollbar-color$|stop-color$|flood-color$|lighting-color$)|-color$/

const tokenFamilies: readonly RegExp[] = [
  /^--font-(?:sans|mono)$/,
  /^--color-fs-[a-z0-9]+(?:-[a-z0-9]+)*$/,
  /^--fs-gradient-[a-z0-9]+(?:-[a-z0-9]+)*$/,
  /^--fs-backdrop$/,
  /^--fs-gloss(?:-[a-z0-9]+)*$/,
  /^--fs-glow-[a-z0-9]+$/,
  /^--fs-tint-neutral$/,
  /^--fs-flash-row$/,
  /^--fs-skeleton$/,
  /^--fs-glass-[a-z0-9]+(?:-[a-z0-9]+)*-(?:bg|border|filter)$/,
  /^--shadow-fs-[a-z0-9]+(?:-[a-z0-9]+)*$/,
  /^--blur-fs-[a-z0-9]+$/,
  /^--radius-fs-[a-z0-9]+(?:-[a-z0-9]+)*$/,
  /^--spacing-fs-\d+$/,
  /^--text-fs-[a-z0-9]+(?:-[a-z0-9]+)*(?:--(?:line-height|letter-spacing|font-weight))?$/,
  /^--fs-duration-(?:hover|sheet)$/,
]

const spacingSteps = [2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64]

const typeParts = ['line-height', 'letter-spacing', 'font-weight']

const webFontFaces = [
  'Geist 400',
  'Geist 500',
  'Geist 600',
  'Geist 700',
  'Geist Mono 400',
  'Geist Mono 500',
  'Geist Mono 600',
]

function withoutTokenReferences(value: string): string {
  let previous = ''
  let current = value
  while (current !== previous) {
    previous = current
    current = current.replace(/var\([^()]*\)/g, ' ')
  }
  return current
}

function findLiteralValues(css: string): string[] {
  const findings: string[] = []
  for (const declaration of parseDeclarations(css)) {
    const { property } = declaration
    const value = maskStrings(declaration.value)
    const shown = `${property}: ${declaration.value.replace(/\s+/g, ' ')}`
    if (hexColor.test(value) || colorFunction.test(value) || gradientFunction.test(value)) {
      findings.push(shown)
      continue
    }
    if (colorProperty.test(property)) {
      const words =
        withoutTokenReferences(value)
          .toLowerCase()
          .match(/-?[a-z][a-z0-9-]*/g) ?? []
      if (words.some((word) => namedColors.has(word))) {
        findings.push(shown)
      }
    }
  }
  return findings
}

function findUndefinedTokens(css: string, tokens: ReadonlyMap<string, string>): string[] {
  const local = new Set(
    parseDeclarations(css)
      .map(({ property }) => property)
      .filter((property) => property.startsWith('--')),
  )
  return findTokenReferences(css).filter((name) => !tokens.has(name) && !local.has(name))
}

const tokensCss = readSource(tokensFile)
const tokens = readTokens(tokensCss)
const checkedSources = readStyleSources().filter(({ file }) => file !== tokensFile)

describe('the literal value detector', () => {
  it('flags hex, functional and gradient colours', () => {
    const css = [
      '.a { color: #fff; }',
      '.b { background: rgba(0, 0, 0, 0.5); }',
      '.c { border-color: hsl(10 20% 30%); }',
      '.d { background-image: linear-gradient(90deg, var(--x) 0%, var(--y) 100%); }',
      '.e { --chip-bg: #0b1f3a; }',
      '.f { color: color-mix(in srgb, var(--x) 50%, var(--y)); }',
    ].join('\n')

    expect(findLiteralValues(css)).toHaveLength(6)
  })

  it('flags named colours in shadows, borders and custom properties', () => {
    const css = [
      '.a { box-shadow: 0 1px 2px black; }',
      '.b { border: 1px solid red; }',
      '.c { --chip-fg: white; }',
      '.d { outline-color: teal; }',
    ].join('\n')

    expect(findLiteralValues(css)).toEqual([
      'box-shadow: 0 1px 2px black',
      'border: 1px solid red',
      '--chip-fg: white',
      'outline-color: teal',
    ])
  })

  it('allows tokens, system colours, transparent and currentColor', () => {
    const css = [
      '.a { color: var(--color-fs-ink); box-shadow: var(--shadow-fs-glass); }',
      '.b { background: var(--fs-gloss), var(--fs-gradient-primary); }',
      '.c { border-color: CanvasText; outline-color: Highlight; }',
      '.d { background: transparent; fill: currentColor; }',
      '.e { box-shadow: 0 0 0 2px var(--color-fs-focus); transition: box-shadow 150ms; }',
      '.f { font-family: "Navy Display"; border-radius: var(--radius-fs-card); }',
      ':root .g:hover { color: var(--color-fs-teal-800); }',
    ].join('\n')

    expect(findLiteralValues(css)).toEqual([])
  })

  it('reads declarations without mistaking selectors or quoted text for values', () => {
    const css =
      "a:hover { color: var(--x); } .b::after { content: '#fff; red'; font: 1rem 'Geist'; }"

    expect(parseDeclarations(css)).toEqual([
      { property: 'color', value: 'var(--x)' },
      { property: 'content', value: "'#fff; red'" },
      { property: 'font', value: "1rem 'Geist'" },
    ])
    expect(findLiteralValues(css)).toEqual([])
  })
})

describe('tokens.css', () => {
  it('holds custom properties only, in a single :root rule', () => {
    const declarations = parseDeclarations(tokensCss)

    expect(maskStrings(tokensCss)).toMatch(/^\s*:root\s*\{[^{}]*\}\s*$/)
    expect(declarations.length).toBeGreaterThan(0)
    expect(declarations.filter(({ property }) => !property.startsWith('--'))).toEqual([])
  })

  it('defines each token once', () => {
    const names = parseDeclarations(tokensCss).map(({ property }) => property)
    const repeated = names.filter((name, index) => names.indexOf(name) !== index)

    expect(repeated).toEqual([])
  })

  it('names every token after one of the token families', () => {
    const unknown = [...tokens.keys()].filter(
      (name) => !tokenFamilies.some((family) => family.test(name)),
    )

    expect(unknown).toEqual([])
  })

  it('refers only to tokens it defines', () => {
    expect(findUndefinedTokens(tokensCss, tokens)).toEqual([])
  })

  it('defines every spacing step in rem and no other step', () => {
    const spacing = [...tokens.entries()].filter(([name]) => name.startsWith('--spacing-fs-'))

    expect(spacing).toEqual(spacingSteps.map((step) => [`--spacing-fs-${step}`, `${step / 16}rem`]))
  })

  it('gives every type role its line height, letter spacing and font weight', () => {
    const roles = [...tokens.keys()].filter(
      (name) => name.startsWith('--text-fs-') && !name.slice('--text-fs-'.length).includes('--'),
    )
    const missing = roles.flatMap((role) =>
      typeParts.map((part) => `${role}--${part}`).filter((name) => !tokens.has(name)),
    )

    expect(roles.length).toBeGreaterThan(0)
    expect(missing).toEqual([])
  })
})

describe('component and global styles', () => {
  it('checks the global stylesheets and every component style block', () => {
    const files = checkedSources.map(({ file }) => file)

    expect(files).toEqual(expect.arrayContaining(['app/styles/base.css', 'app/styles/fonts.css']))
    expect(files).toEqual(expect.arrayContaining(listSourceFiles('.vue')))
    expect(files).not.toContain(tokensFile)
  })

  it.each(checkedSources)(
    'keeps literal colours, gradients and shadow colours out of $file',
    ({ css }) => {
      expect(findLiteralValues(css)).toEqual([])
    },
  )

  it.each(checkedSources)('uses only defined tokens in $file', ({ css }) => {
    expect(findUndefinedTokens(css, tokens)).toEqual([])
  })

  it.each(checkedSources.filter(({ file }) => file !== fontsFile))(
    'loads no url() resource from $file',
    ({ css }) => {
      expect(css).not.toMatch(/url\(/i)
    },
  )
})

describe('fonts.css', () => {
  const fontsCss = readSource(fontsFile)
  const faces = Array.from(fontsCss.matchAll(/@font-face\s*\{([^}]*)\}/g), (match) => {
    const source = match[1] ?? ''
    const body = parseDeclarations(`{${source}}`)
    const read = (property: string): string =>
      body.find((declaration) => declaration.property === property)?.value ?? ''
    return {
      family: /font-family\s*:\s*(['"])(.*?)\1/.exec(source)?.[2] ?? '',
      weight: read('font-weight'),
      style: read('font-style'),
      display: read('font-display'),
      urls: Array.from(source.matchAll(/url\(\s*(['"]?)(.*?)\1\s*\)/g), (url) => url[2] ?? ''),
    }
  })

  it('declares one swap face per web weight of Geist and Geist Mono', () => {
    expect(faces.map(({ family, weight }) => `${family} ${weight}`)).toEqual(webFontFaces)
    expect(faces.every(({ style, display }) => style === 'normal' && display === 'swap')).toBe(true)
  })

  it('loads every face from one local WOFF2 file in the fonts folder', () => {
    const urls = faces.flatMap((face) => face.urls)

    expect(faces.every((face) => face.urls.length === 1)).toBe(true)
    expect(urls.filter((url) => !/^\.\.\/fonts\/[A-Za-z]+-[A-Za-z]+\.woff2$/.test(url))).toEqual([])
    expect(urls.filter((url) => !existsSync(join(sourceRoot, 'app/styles', url)))).toEqual([])
    expect(new Set(urls).size).toBe(webFontFaces.length)
  })
})
