import { describe, expect, it } from 'vitest'

import {
  composite,
  contrastRatio,
  parseColor,
  parseGradient,
  relativeLuminance,
  sampleGradient,
  type Rgba,
} from './color-contrast'
import { readTokens, resolveToken } from './stylesheet-source'

type StopChoice = 'first' | 'last' | 'darkest' | 'lightest'

type Paint =
  | { kind: 'color'; token: string }
  | { kind: 'gradient'; token: string }
  | { kind: 'stop'; token: string; stop: StopChoice }
  | { kind: 'limit'; label: string; value: string }

interface ContrastCase {
  pair: string
  text: Paint
  layers: Paint[]
  minimum: number
  positions?: readonly number[]
}

const TEXT = 4.5

const NON_TEXT = 3

const tokens = readTokens()

const color = (token: string): Paint => ({ kind: 'color', token })

const gradient = (token: string): Paint => ({ kind: 'gradient', token })

const stop = (token: string, choice: StopChoice): Paint => ({ kind: 'stop', token, stop: choice })

const glassOnDarkCap: Paint = {
  kind: 'limit',
  label: 'the 0.12 glass-on-dark cap',
  value: 'rgb(255 255 255 / 0.12)',
}

const everyFivePercent = Array.from({ length: 21 }, (_, index) => index / 20)

const onDark = color('--color-fs-on-dark')
const onDark2 = color('--color-fs-on-dark-2')
const ink = color('--color-fs-ink')
const ink2 = color('--color-fs-ink-2')
const muted = color('--color-fs-muted')
const teal700 = color('--color-fs-teal-700')
const high = color('--color-fs-high')
const surface = color('--color-fs-surface')
const backdrop = stop('--fs-backdrop', 'darkest')
const heroTealEnd = stop('--fs-gradient-hero', 'lightest')
const webGlass = color('--fs-glass-g1-bg')
const mobileGlass = color('--fs-glass-g3-bg')
const well = color('--color-fs-well')
const tile = color('--fs-glass-g4-bg')
const control = color('--fs-glass-g4-control-bg')
const gloss = gradient('--fs-gloss')

const statuses = ['unassigned', 'assigned', 'in-progress', 'on-hold', 'completed', 'cancelled']

const heroGlows = ['--fs-glow-band', '--fs-glow-signin', '--fs-glow-mobile']

function readColor(token: string): Rgba {
  return parseColor(resolveToken(token, tokens))
}

function readStops(token: string): Rgba[] {
  return parseGradient(resolveToken(token, tokens)).map((gradientStop) => gradientStop.color)
}

function pickStop(token: string, choice: StopChoice): Rgba {
  const stops = readStops(token)
  const byLuminance = [...stops].sort((a, b) => relativeLuminance(a) - relativeLuminance(b))
  const picked = {
    first: stops[0],
    last: stops[stops.length - 1],
    darkest: byLuminance[0],
    lightest: byLuminance[byLuminance.length - 1],
  }[choice]
  if (picked === undefined) {
    throw new Error(`${token} has no colour stops`)
  }
  return picked
}

function paintAt(paint: Paint, position: number): Rgba {
  switch (paint.kind) {
    case 'color':
      return readColor(paint.token)
    case 'gradient':
      return sampleGradient(parseGradient(resolveToken(paint.token, tokens)), position)
    case 'stop':
      return pickStop(paint.token, paint.stop)
    case 'limit':
      return parseColor(paint.value)
  }
}

function describePaint(paint: Paint): string {
  switch (paint.kind) {
    case 'color':
    case 'gradient':
      return paint.token
    case 'stop':
      return `${paint.token} (${paint.stop} stop)`
    case 'limit':
      return paint.label
  }
}

function lowestRatio({ text, layers, positions }: ContrastCase): number {
  const sampled =
    positions ?? (layers.some((layer) => layer.kind === 'gradient') ? everyFivePercent : [0])
  return Math.min(
    ...sampled.map((position) => {
      const [base, ...upper] = layers.map((layer) => paintAt(layer, position))
      if (base === undefined) {
        throw new Error('A contrast pair needs at least one background layer')
      }
      const background = upper.reduce((below, layer) => composite(layer, below), base)
      return contrastRatio(paintAt(text, position), background)
    }),
  )
}

function named(cases: ContrastCase[]): (ContrastCase & { name: string })[] {
  return cases.map((contrastCase) => ({
    ...contrastCase,
    name: `${contrastCase.pair}: ${describePaint(contrastCase.text)} on ${[...contrastCase.layers]
      .reverse()
      .map(describePaint)
      .join(' over ')}`,
  }))
}

const specPairs: ContrastCase[] = [
  {
    pair: '#FFFFFF on the primary gradient with gloss',
    text: onDark,
    layers: [gradient('--fs-gradient-primary'), gloss],
    minimum: TEXT,
  },
  {
    pair: '#FFFFFF on the pressed primary gradient with gloss',
    text: onDark,
    layers: [gradient('--fs-gradient-primary-pressed'), gloss],
    minimum: TEXT,
  },
  ...heroGlows.flatMap((glow): ContrastCase[] => {
    const worstPoint = [heroTealEnd, stop(glow, 'first')]
    return [
      { pair: '#FFFFFF at the hero worst point', text: onDark, layers: worstPoint, minimum: TEXT },
      {
        pair: '#FFFFFF at the hero worst point on a tile',
        text: onDark,
        layers: [...worstPoint, tile],
        minimum: TEXT,
      },
      {
        pair: '#FFFFFF at the hero worst point on a control',
        text: onDark,
        layers: [...worstPoint, control],
        minimum: TEXT,
      },
      {
        pair: 'on-dark-2 at the hero worst point',
        text: onDark2,
        layers: worstPoint,
        minimum: TEXT,
      },
      {
        pair: 'on-dark-2 at the hero worst point on a tile or the track',
        text: onDark2,
        layers: [...worstPoint, tile],
        minimum: TEXT,
      },
      {
        pair: 'on-dark-2 at the hero worst point on glass on dark at its cap',
        text: onDark2,
        layers: [...worstPoint, glassOnDarkCap],
        minimum: TEXT,
      },
      {
        pair: 'focus ring on dark at the hero worst point on a tile or the track',
        text: color('--color-fs-focus-on-dark'),
        layers: [...worstPoint, tile],
        minimum: NON_TEXT,
      },
      {
        pair: '#FFFFFF on a control on a tile or the track at the hero worst point',
        text: onDark,
        layers: [...worstPoint, tile, control],
        minimum: TEXT,
      },
    ]
  }),
  {
    pair: '#0B1F3A on the accent gradient',
    text: ink,
    layers: [gradient('--fs-gradient-accent')],
    minimum: TEXT,
  },
  { pair: 'ink on the backdrop', text: ink, layers: [backdrop], minimum: TEXT },
  { pair: 'ink-2 on the backdrop', text: ink2, layers: [backdrop], minimum: TEXT },
  { pair: 'muted on the backdrop', text: muted, layers: [backdrop], minimum: TEXT },
  { pair: 'muted on web glass', text: muted, layers: [backdrop, webGlass], minimum: TEXT },
  { pair: 'muted on mobile glass', text: muted, layers: [backdrop, mobileGlass], minimum: TEXT },
  {
    pair: 'muted on a well in web glass',
    text: muted,
    layers: [backdrop, webGlass, well],
    minimum: TEXT,
  },
  {
    pair: 'muted on a well in mobile glass',
    text: muted,
    layers: [backdrop, mobileGlass, well],
    minimum: TEXT,
  },
  ...[...statuses, 'neutral'].map((tint): ContrastCase => ({
    pair: `muted on the ${tint} KPI tint`,
    text: muted,
    layers: [backdrop, gradient(`--fs-gradient-kpi-${tint}`)],
    minimum: TEXT,
  })),
  { pair: 'teal-700 links on glass', text: teal700, layers: [backdrop, webGlass], minimum: TEXT },
  ...[...statuses, 'fail'].map((status): ContrastCase => ({
    pair: `${status} chip text on its chip fill`,
    text: color(`--color-fs-${status}`),
    layers: [gradient(`--fs-gradient-chip-${status}`)],
    minimum: TEXT,
  })),
  { pair: 'danger on white', text: color('--color-fs-danger'), layers: [surface], minimum: TEXT },
  {
    pair: 'danger-strong on danger-soft',
    text: color('--color-fs-danger-strong'),
    layers: [gradient('--fs-gradient-danger-soft')],
    minimum: TEXT,
  },
  {
    pair: 'ink-2 on danger-soft',
    text: ink2,
    layers: [gradient('--fs-gradient-danger-soft')],
    minimum: TEXT,
  },
  { pair: 'High on mobile glass', text: high, layers: [backdrop, mobileGlass], minimum: TEXT },
  {
    pair: 'High on a well in mobile glass',
    text: high,
    layers: [backdrop, mobileGlass, well],
    minimum: TEXT,
  },
  { pair: 'High on web glass', text: high, layers: [backdrop, webGlass], minimum: TEXT },
  {
    pair: 'High on a well in web glass',
    text: high,
    layers: [backdrop, webGlass, well],
    minimum: TEXT,
  },
  {
    pair: '"Just now" on flash',
    text: color('--color-fs-just-now'),
    layers: [backdrop, webGlass, color('--color-fs-flash')],
    minimum: TEXT,
  },
  {
    pair: '#FFFFFF on Pass selected under the centred label',
    text: onDark,
    layers: [gradient('--fs-gradient-pass'), gloss],
    minimum: TEXT,
    positions: [0.5],
  },
  {
    pair: '#FFFFFF on the Pass gradient',
    text: onDark,
    layers: [gradient('--fs-gradient-pass')],
    minimum: TEXT,
  },
  {
    pair: 'line-input border on white',
    text: color('--color-fs-line-input'),
    layers: [surface],
    minimum: NON_TEXT,
  },
  {
    pair: 'line-input border on glass',
    text: color('--color-fs-line-input'),
    layers: [backdrop, webGlass],
    minimum: NON_TEXT,
  },
  {
    pair: 'focus ring on the backdrop',
    text: color('--color-fs-focus'),
    layers: [backdrop],
    minimum: NON_TEXT,
  },
  {
    pair: 'placeholder on white',
    text: color('--color-fs-placeholder'),
    layers: [surface],
    minimum: TEXT,
  },
  ...['muted', 'ink-2', 'danger'].map((role): ContrastCase => ({
    pair: `${role} on the dialog surface`,
    text: color(`--color-fs-${role}`),
    layers: [backdrop, color('--color-fs-overlay'), color('--fs-glass-sheet-bg')],
    minimum: TEXT,
  })),
  {
    pair: 'ink-2 on the error banner',
    text: ink2,
    layers: [backdrop, gradient('--fs-gradient-error-banner')],
    minimum: TEXT,
  },
  {
    pair: '#FFFFFF on the danger tile',
    text: onDark,
    layers: [color('--color-fs-danger')],
    minimum: TEXT,
  },
]

const recipePairs: ContrastCase[] = [
  {
    pair: 'ink on the error banner',
    text: ink,
    layers: [backdrop, gradient('--fs-gradient-error-banner')],
    minimum: TEXT,
  },
  {
    pair: 'ink on the reconnecting and conflict banner',
    text: ink,
    layers: [backdrop, gradient('--fs-gradient-reconnecting-banner')],
    minimum: TEXT,
  },
  {
    pair: 'ink on the back online banner',
    text: ink,
    layers: [backdrop, gradient('--fs-gradient-online-banner')],
    minimum: TEXT,
  },
  {
    pair: 'ink-2 icon on the paused tile',
    text: ink2,
    layers: [gradient('--fs-gradient-paused-tile')],
    minimum: NON_TEXT,
  },
  {
    pair: 'ink-2 second sentence on the reconnecting banner',
    text: ink2,
    layers: [backdrop, gradient('--fs-gradient-reconnecting-banner')],
    minimum: TEXT,
  },
  {
    pair: 'ink-2 second sentence on the back online banner',
    text: ink2,
    layers: [backdrop, gradient('--fs-gradient-online-banner')],
    minimum: TEXT,
  },
  ...(
    [
      ['ink', ink],
      ['ink-2', ink2],
    ] as const
  ).map(([role, text]): ContrastCase => ({
    pair: `${role} on the paused banner`,
    text,
    layers: [backdrop, webGlass],
    minimum: TEXT,
  })),
  {
    pair: 'ink icon on the reconnecting accent tile',
    text: ink,
    layers: [gradient('--fs-gradient-accent')],
    minimum: NON_TEXT,
  },
  {
    pair: '#FFFFFF icon on the back online tile',
    text: onDark,
    layers: [gradient('--fs-gradient-pass')],
    minimum: NON_TEXT,
  },
  {
    pair: 'ink on the live pill in the top bar',
    text: ink,
    layers: [backdrop, color('--fs-glass-topbar-bg'), webGlass],
    minimum: TEXT,
  },
  {
    pair: 'muted on a flashed table cell',
    text: muted,
    layers: [backdrop, webGlass, color('--color-fs-flash')],
    minimum: TEXT,
  },
  {
    pair: 'muted on a flashed table cell under the hover tint',
    text: muted,
    layers: [backdrop, webGlass, color('--color-fs-flash'), color('--color-fs-row-hover')],
    minimum: TEXT,
  },
  {
    pair: '#FFFFFF on a toast',
    text: onDark,
    layers: [surface, gradient('--fs-gradient-toast')],
    minimum: TEXT,
  },
  {
    pair: 'on-dark-2 on a toast',
    text: onDark2,
    layers: [surface, gradient('--fs-gradient-toast')],
    minimum: TEXT,
  },
  {
    pair: 'muted on the table header',
    text: muted,
    layers: [backdrop, webGlass, color('--color-fs-table-header-bg')],
    minimum: TEXT,
  },
  {
    pair: 'ink-2 on the segmented track',
    text: ink2,
    layers: [backdrop, webGlass, color('--color-fs-track')],
    minimum: TEXT,
  },
  {
    pair: 'ink-2 on an unselected filter chip',
    text: ink2,
    layers: [backdrop, color('--color-fs-chip-bg')],
    minimum: TEXT,
  },
  {
    pair: 'muted on a selected assign row',
    text: muted,
    layers: [backdrop, webGlass, color('--color-fs-selected-bg')],
    minimum: TEXT,
  },
  {
    pair: 'ink on a secondary button',
    text: ink,
    layers: [backdrop, color('--color-fs-secondary-bg')],
    minimum: TEXT,
  },
  {
    pair: 'danger on a danger button',
    text: color('--color-fs-danger'),
    layers: [backdrop, color('--color-fs-secondary-bg')],
    minimum: TEXT,
  },
  {
    pair: 'teal-700 on a hovered quiet button',
    text: teal700,
    layers: [backdrop, webGlass, color('--color-fs-quiet-hover')],
    minimum: TEXT,
  },
  {
    pair: 'teal-700 icon on the neutral tile',
    text: teal700,
    layers: [gradient('--fs-tint-neutral')],
    minimum: NON_TEXT,
  },
  {
    pair: 'navy-800 initials on a neutral avatar',
    text: color('--color-fs-navy-800'),
    layers: [gradient('--fs-tint-neutral')],
    minimum: TEXT,
  },
  ...(
    [
      ['ink', ink],
      ['ink-2', ink2],
      ['teal-700', teal700],
    ] as const
  ).map(([role, text]): ContrastCase => ({
    pair: `${role} on the sidebar`,
    text,
    layers: [backdrop, color('--fs-glass-sidebar-bg')],
    minimum: TEXT,
  })),
  {
    pair: 'muted navigation icon on the sidebar',
    text: muted,
    layers: [backdrop, color('--fs-glass-sidebar-bg')],
    minimum: NON_TEXT,
  },
  ...(
    [
      ['ink', ink],
      ['muted', muted],
    ] as const
  ).map(([role, text]): ContrastCase => ({
    pair: `${role} on the account panel`,
    text,
    layers: [backdrop, color('--fs-glass-sidebar-bg'), color('--color-fs-account-bg')],
    minimum: TEXT,
  })),
  ...(
    [
      ['ink', ink],
      ['teal-700', teal700],
    ] as const
  ).map(([role, text]): ContrastCase => ({
    pair: `${role} on the top bar`,
    text,
    layers: [backdrop, color('--fs-glass-topbar-bg')],
    minimum: TEXT,
  })),
  {
    pair: 'ink on a count capsule in an unselected filter chip',
    text: ink,
    layers: [backdrop, color('--color-fs-chip-bg'), color('--color-fs-track')],
    minimum: TEXT,
  },
  {
    pair: 'muted on a hovered table row',
    text: muted,
    layers: [backdrop, webGlass, color('--color-fs-row-hover')],
    minimum: TEXT,
  },
]

describe('contrast arithmetic', () => {
  it('reproduces the reference ratios for opaque and composited pairs', () => {
    const ratio = (text: string, base: string, ...upper: string[]): number => {
      const background = upper
        .map(parseColor)
        .reduce((below, layer) => composite(layer, below), parseColor(base))
      return contrastRatio(parseColor(text), background)
    }

    expect(ratio('#FFFFFF', '#000000')).toBeCloseTo(21, 2)
    expect(ratio('#FFFFFF', '#0F766E')).toBeCloseTo(5.47, 1)
    expect(ratio('#0B1F3A', '#EEF5F4')).toBeCloseTo(14.95, 1)
    expect(ratio('#66768A', '#FFFFFF')).toBeCloseTo(4.64, 1)
    expect(ratio('#B42318', '#FFFFFF')).toBeCloseTo(6.57, 1)
    expect(ratio('#0F766E', '#EEF5F4', 'rgb(255 255 255 / 0.72)')).toBeCloseTo(5.32, 1)
    expect(ratio('#7C8CA1', '#FFFFFF')).toBeCloseTo(3.43, 1)
  })

  it('interpolates translucent gradient stops with premultiplied alpha', () => {
    const fade = parseGradient(
      'linear-gradient(90deg, rgb(255 255 255 / 0.18) 0%, rgb(255 255 255 / 0) 60%)',
    )

    expect(sampleGradient(fade, 0.3)).toEqual({
      r: expect.closeTo(255, 6),
      g: expect.closeTo(255, 6),
      b: expect.closeTo(255, 6),
      a: expect.closeTo(0.09, 6),
    })
    expect(sampleGradient(fade, 0.8).a).toBe(0)
  })
})

describe('contrast of the spec colour pairs, read from tokens.css', () => {
  it('treats the teal end as the brightest point of the hero gradient', () => {
    expect(pickStop('--fs-gradient-hero', 'lightest')).toEqual(
      pickStop('--fs-gradient-hero', 'last'),
    )
  })

  it('keeps every glass-on-dark fill at or below the 0.12 cap', () => {
    const fills = [...tokens.keys()].filter((name) => /^--fs-glass-g4(?:-[a-z]+)*-bg$/.test(name))

    expect(fills.length).toBeGreaterThan(0)
    expect(fills.filter((name) => readColor(name).a > 0.12)).toEqual([])
  })

  it.each(named(specPairs))('$name reaches $minimum:1', (contrastCase) => {
    expect(lowestRatio(contrastCase)).toBeGreaterThanOrEqual(contrastCase.minimum)
  })
})

describe('contrast of the component recipe pairs, read from tokens.css', () => {
  it.each(named(recipePairs))('$name reaches $minimum:1', (contrastCase) => {
    expect(lowestRatio(contrastCase)).toBeGreaterThanOrEqual(contrastCase.minimum)
  })
})
