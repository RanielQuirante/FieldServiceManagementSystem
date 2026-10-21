export interface Rgba {
  r: number
  g: number
  b: number
  a: number
}

export interface GradientStop {
  color: Rgba
  position: number
}

const hexColor = /^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i

const rgbFunction = /^rgba?\((.*)\)$/is

const gradientFunction = /^(?:repeating-)?(?:linear|radial)-gradient\((.*)\)$/is

function parseChannel(text: string): number {
  const value = Number.parseFloat(text)
  if (!Number.isFinite(value)) {
    throw new Error(`"${text}" is not a colour channel`)
  }
  return text.trim().endsWith('%') ? (value / 100) * 255 : value
}

function parseAlpha(text: string | undefined): number {
  if (text === undefined) {
    return 1
  }
  const value = Number.parseFloat(text)
  if (!Number.isFinite(value)) {
    throw new Error(`"${text}" is not an alpha value`)
  }
  return text.trim().endsWith('%') ? value / 100 : value
}

function parseHex(digits: string): Rgba {
  const full = digits.length <= 4 ? Array.from(digits, (digit) => digit + digit).join('') : digits
  const channel = (index: number): number => Number.parseInt(full.slice(index, index + 2), 16)
  return {
    r: channel(0),
    g: channel(2),
    b: channel(4),
    a: full.length === 8 ? channel(6) / 255 : 1,
  }
}

export function parseColor(text: string): Rgba {
  const trimmed = text.trim()
  const hex = hexColor.exec(trimmed)
  if (hex?.[1] !== undefined) {
    return parseHex(hex[1])
  }
  const rgb = rgbFunction.exec(trimmed)
  if (rgb?.[1] !== undefined) {
    const [channels = '', alpha] = rgb[1].split('/')
    const parts = channels.split(/[\s,]+/).filter((part) => part.length > 0)
    if (alpha === undefined && parts.length === 4) {
      const [r = '', g = '', b = '', a = ''] = parts
      return { r: parseChannel(r), g: parseChannel(g), b: parseChannel(b), a: parseAlpha(a) }
    }
    if (parts.length !== 3) {
      throw new Error(`"${text}" is not an rgb() colour`)
    }
    const [r = '', g = '', b = ''] = parts
    return { r: parseChannel(r), g: parseChannel(g), b: parseChannel(b), a: parseAlpha(alpha) }
  }
  throw new Error(`"${text}" is not a colour this test can read`)
}

function splitTopLevel(text: string): string[] {
  const parts: string[] = []
  let depth = 0
  let start = 0
  for (let index = 0; index < text.length; index += 1) {
    const character = text[index]
    if (character === '(') {
      depth += 1
    } else if (character === ')') {
      depth -= 1
    } else if (character === ',' && depth === 0) {
      parts.push(text.slice(start, index).trim())
      start = index + 1
    }
  }
  parts.push(text.slice(start).trim())
  return parts
}

function splitStop(stop: string): { color: string; position: string } {
  if (stop.startsWith('#')) {
    const end = stop.search(/\s/)
    return end === -1
      ? { color: stop, position: '' }
      : { color: stop.slice(0, end), position: stop.slice(end).trim() }
  }
  const close = stop.indexOf(')')
  return { color: stop.slice(0, close + 1), position: stop.slice(close + 1).trim() }
}

export function parseGradient(text: string): GradientStop[] {
  const gradient = gradientFunction.exec(text.trim())
  if (gradient?.[1] === undefined) {
    throw new Error(`"${text}" is not a linear or radial gradient`)
  }
  const stops = splitTopLevel(gradient[1])
    .filter((part) => /^(#|rgba?\()/i.test(part))
    .map(splitStop)
  if (stops.length < 2) {
    throw new Error(`"${text}" has fewer than two colour stops`)
  }
  const last = stops.length - 1
  return stops.map(({ color, position }, index) => ({
    color: parseColor(color),
    position: position.endsWith('%') ? Number.parseFloat(position) / 100 : index / last,
  }))
}

function mix(from: Rgba, to: Rgba, amount: number): Rgba {
  const a = from.a + (to.a - from.a) * amount
  if (a === 0) {
    return { r: 0, g: 0, b: 0, a: 0 }
  }
  const channel = (start: number, end: number): number =>
    (start * from.a + (end * to.a - start * from.a) * amount) / a
  return { r: channel(from.r, to.r), g: channel(from.g, to.g), b: channel(from.b, to.b), a }
}

export function sampleGradient(stops: readonly GradientStop[], position: number): Rgba {
  const first = stops[0]
  const last = stops[stops.length - 1]
  if (first === undefined || last === undefined) {
    throw new Error('A gradient needs colour stops')
  }
  if (position <= first.position) {
    return first.color
  }
  for (let index = 1; index < stops.length; index += 1) {
    const start = stops[index - 1]
    const end = stops[index]
    if (start !== undefined && end !== undefined && position <= end.position) {
      const span = end.position - start.position
      return mix(start.color, end.color, span === 0 ? 1 : (position - start.position) / span)
    }
  }
  return last.color
}

export function composite(top: Rgba, bottom: Rgba): Rgba {
  const a = top.a + bottom.a * (1 - top.a)
  if (a === 0) {
    return { r: 0, g: 0, b: 0, a: 0 }
  }
  const channel = (upper: number, lower: number): number =>
    (upper * top.a + lower * bottom.a * (1 - top.a)) / a
  return {
    r: channel(top.r, bottom.r),
    g: channel(top.g, bottom.g),
    b: channel(top.b, bottom.b),
    a,
  }
}

function linearChannel(value: number): number {
  const channel = value / 255
  return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
}

export function relativeLuminance(color: Rgba): number {
  return (
    0.2126 * linearChannel(color.r) +
    0.7152 * linearChannel(color.g) +
    0.0722 * linearChannel(color.b)
  )
}

export function contrastRatio(foreground: Rgba, background: Rgba): number {
  if (Math.abs(background.a - 1) > 1e-9) {
    throw new Error('Contrast needs an opaque background; start the layers on an opaque colour')
  }
  const text = relativeLuminance(composite(foreground, background))
  const surface = relativeLuminance(background)
  return (Math.max(text, surface) + 0.05) / (Math.min(text, surface) + 0.05)
}
