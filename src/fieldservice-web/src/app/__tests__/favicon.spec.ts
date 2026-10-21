import { describe, expect, it } from 'vitest'

import { readSource } from '@/app/styles/__tests__/stylesheet-source'

const favicon = readSource('../public/favicon.svg')

function parse(svg: string): Document {
  return new DOMParser().parseFromString(svg, 'image/svg+xml')
}

describe('the favicon', () => {
  it('draws the logo mark on light in a 36-unit square', () => {
    const root = parse(favicon).documentElement

    expect(root.tagName).toBe('svg')
    expect(root.getAttribute('viewBox')).toBe('0 0 36 36')
    expect(root.querySelectorAll(':scope > rect')).toHaveLength(4)
    expect(root.querySelector('linearGradient#primary')?.getAttribute('x2')).toBe('1')
  })

  it('nests a larger, heavier pin so it still reads in a 16-pixel tab', () => {
    const pin = parse(favicon).querySelector(':scope > svg')
    const dot = pin?.querySelector('circle')

    expect(pin?.getAttribute('x')).toBe('6')
    expect(pin?.getAttribute('y')).toBe('6')
    expect(pin?.getAttribute('width')).toBe('24')
    expect(pin?.getAttribute('height')).toBe('24')
    expect(pin?.getAttribute('stroke-width')).toBe('2.5')
    expect(dot?.getAttribute('r')).toBe('3.25')
    expect(dot?.getAttribute('fill')).toBe('#FCD34D')
  })

  it('holds no script, style, comment or external reference', () => {
    expect(favicon).not.toMatch(/<script|<style|<!--|href=|url\((?!#)/i)
  })
})
