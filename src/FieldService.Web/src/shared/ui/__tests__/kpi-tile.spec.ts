import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { readTokens, ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'

import AppIcon from '../AppIcon.vue'
import type { IconName } from '../icons'
import KpiTile from '../KpiTile.vue'
import StatusChip from '../StatusChip.vue'

const component = 'shared/ui/KpiTile.vue'

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

function show(props: InstanceType<typeof KpiTile>['$props'], slots = {}): VueWrapper {
  const wrapper = mount(KpiTile, { props, slots })
  mounted = wrapper
  return wrapper
}

async function linkRouter(): Promise<Router> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { render: () => null } },
      { path: '/jobs', name: 'jobs', component: { render: () => null } },
    ],
  })
  await router.push('/')
  return router
}

function iconMarkup(name: IconName): string {
  return mount(AppIcon, { props: { name, size: 20 } }).element.innerHTML
}

function spokenText(element: Element): string {
  const copy = element.cloneNode(true) as Element
  copy.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => hidden.remove())
  return (copy.textContent ?? '').replace(/[ \t\r\n]+/g, ' ').trim()
}

describe('KpiTile', () => {
  it('is a static block read as label, value and subline, outside the tab order', () => {
    const wrapper = show({
      label: 'Time on site',
      icon: 'clock',
      value: '1 h 46 min',
      subline: '9:12–10:58 AM',
    })

    expect(wrapper.element.tagName).toBe('DIV')
    expect(wrapper.attributes('tabindex')).toBeUndefined()
    expect(wrapper.attributes('aria-label')).toBeUndefined()
    expect(wrapper.find('.kpi-tile__label').text()).toBe('Time on site')
    expect(wrapper.find('.kpi-tile__value').text()).toBe('1 h 46 min')
    expect(wrapper.find('.kpi-tile__subline').text()).toBe('9:12–10:58 AM')
    expect(spokenText(wrapper.element)).toBe('Time on site1 h 46 min9:12–10:58 AM')
  })

  it('draws its 40px icon tile hidden, in the neutral tint by default', () => {
    const wrapper = show({ label: 'Result', icon: 'clipboard-check', value: '8' })

    const tile = wrapper.find('.icon-tile')
    expect(tile.attributes('aria-hidden')).toBe('true')
    expect(tile.classes()).toEqual(expect.arrayContaining(['icon-tile--40', 'icon-tile--neutral']))
    expect(tile.find('svg').element.innerHTML).toBe(iconMarkup('clipboard-check'))
    expect(wrapper.classes()).toContain('kpi-tile--neutral')
  })

  it.each([
    ['Unassigned', 'unassigned', '.kpi-tile--unassigned'],
    ['Assigned', 'assigned', '.kpi-tile--assigned'],
    ['InProgress', 'in-progress', '.kpi-tile--in-progress'],
    ['OnHold', 'on-hold', '.kpi-tile--on-hold'],
    ['Completed', 'completed', '.kpi-tile--completed'],
    ['Cancelled', 'cancelled', '.kpi-tile--cancelled,\n.kpi-tile--unknown'],
  ] as const)('takes the %s tint and status tile', (tone, name, selector) => {
    const wrapper = show({ label: 'Tile', icon: 'clock', value: '2', tone })

    expect(wrapper.classes()).toContain(`kpi-tile--${name}`)
    expect(wrapper.find('.icon-tile').classes()).toContain(`icon-tile--${name}`)
    expect(ruleDeclarations(component, selector)).toEqual({
      '--kpi-bg': `var(--fs-gradient-kpi-${name})`,
    })
  })

  it('shows "—" in the muted colour when the value is unavailable, with the reason under it', () => {
    const wrapper = show({
      label: 'Time on site',
      icon: 'clock',
      value: null,
      subline: 'Not started',
    })

    const value = wrapper.find('.kpi-tile__value')
    expect(value.text()).toBe('—')
    expect(value.classes()).toContain('kpi-tile__value--unavailable')
    expect(wrapper.find('.kpi-tile__subline').text()).toBe('Not started')
    expect(ruleDeclarations(component, '.kpi-tile__value--unavailable')).toEqual({
      color: 'var(--color-fs-muted)',
    })
  })

  it('hides the " · " between subline parts, its dot kept on the line before, and reads a comma instead', () => {
    const wrapper = show({
      label: 'Result',
      icon: 'clipboard-check',
      value: '8',
      subline: ['Rooftop HVAC service', '10 items'],
    })

    const subline = wrapper.find('.kpi-tile__subline')
    expect(subline.find('[aria-hidden="true"]').text()).toBe('·')
    expect(subline.find('[aria-hidden="true"]').element.textContent).toBe('\u{00a0}\u{00b7} ')
    expect(subline.find('.visually-hidden').element.textContent).toBe(', ')
    expect(spokenText(subline.element)).toBe('Rooftop HVAC service, 10 items')
  })

  it('takes content that is not one text in its value slot, such as result chips', () => {
    const wrapper = show(
      { label: 'Result', icon: 'clipboard-check', subline: '6 of 10 answered' },
      {
        value: () => [
          h(StatusChip, { result: 'Pass', count: 4 }),
          h(StatusChip, { result: 'Fail', count: 1 }),
        ],
      },
    )

    expect(wrapper.find('.kpi-tile__value').exists()).toBe(false)
    expect(wrapper.findAll('.kpi-tile__content .status-chip').map((chip) => chip.text())).toEqual([
      '4 Pass',
      '1 Fail',
    ])
  })

  it('keeps the real label and tile while loading, with hidden skeleton value and subline', () => {
    const wrapper = show({
      label: 'Completed',
      icon: 'check-circle',
      value: '10:58 AM',
      subline: 'by Marco Reyes',
      loading: true,
    })

    expect(wrapper.find('.kpi-tile__label').text()).toBe('Completed')
    expect(wrapper.find('.icon-tile').exists()).toBe(true)
    expect(wrapper.find('.kpi-tile__value').exists()).toBe(false)
    expect(wrapper.find('.kpi-tile__subline').exists()).toBe(false)
    const skeleton = wrapper.find('.kpi-tile__skeleton')
    expect(skeleton.attributes('aria-hidden')).toBe('true')
    expect(skeleton.findAll('span')).toHaveLength(2)
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(ruleDeclarations(component, '.kpi-tile__skeleton-value')).toEqual({
      width: '3rem',
      height: '1.75rem',
    })
    expect(ruleDeclarations(component, '.kpi-tile__skeleton-subline')).toEqual({
      width: '72%',
      height: '0.875rem',
    })
    expect(ruleDeclarations(component, '.kpi-tile__skeleton')).toMatchObject({
      gap: 'var(--spacing-fs-8)',
    })
  })

  it('shows a hidden 72×13 skeleton label and an empty neutral tile when its label and icon are unknown', () => {
    const wrapper = show({ label: null, icon: null, loading: true })

    expect(wrapper.find('.kpi-tile__label').exists()).toBe(false)
    const label = wrapper.find('.kpi-tile__head > .kpi-tile__label-skeleton')
    expect(label.attributes('aria-hidden')).toBe('true')
    expect(label.text()).toBe('')
    const tile = wrapper.find('.icon-tile')
    expect(tile.attributes('aria-hidden')).toBe('true')
    expect(tile.classes()).toEqual(expect.arrayContaining(['icon-tile--40', 'icon-tile--neutral']))
    expect(tile.find('svg').exists()).toBe(false)
    expect(spokenText(wrapper.element)).toBe('')
    expect(ruleDeclarations(component, '.kpi-tile__label-skeleton')).toEqual({
      width: '4.5rem',
      height: '0.8125rem',
    })
    expect(
      ruleDeclarations(
        component,
        '.kpi-tile__label-skeleton,\n.kpi-tile__skeleton-value,\n.kpi-tile__skeleton-subline',
      ),
    ).toEqual({
      display: 'block',
      'max-width': '100%',
      'border-radius': 'var(--radius-fs-pill)',
      background: 'var(--fs-skeleton)',
    })
    expect(
      ruleDeclarations(
        component,
        '.kpi-tile__label-skeleton,\n  .kpi-tile__skeleton-value,\n  .kpi-tile__skeleton-subline',
        '@media (forced-colors: active)',
      ),
    ).toEqual({ border: '1px solid CanvasText' })
  })

  it('keeps a known label and icon beside the skeleton value while loading', () => {
    const wrapper = show({ label: 'Result', icon: 'clipboard-check', loading: true })

    expect(wrapper.find('.kpi-tile__label').text()).toBe('Result')
    expect(wrapper.find('.kpi-tile__label-skeleton').exists()).toBe(false)
    expect(wrapper.find('.icon-tile svg').element.innerHTML).toBe(iconMarkup('clipboard-check'))
  })

  it('leaves an unknown label out of a linked tile name', async () => {
    const router = await linkRouter()
    const wrapper = mount(KpiTile, {
      props: { label: null, icon: null, value: '2', subline: 'Today', to: { name: 'jobs' } },
      global: { plugins: [router] },
    })
    mounted = wrapper

    expect(wrapper.attributes('aria-label')).toBe('2, Today')
  })

  it('becomes a router link named "<label>, <value>, <subline>" when it has a destination', async () => {
    const router = await linkRouter()
    const wrapper = mount(KpiTile, {
      props: {
        label: 'In progress',
        icon: 'play-circle',
        tone: 'InProgress',
        value: '2',
        subline: 'Across 2 technicians',
        to: { name: 'jobs', query: { status: 'InProgress' } },
      },
      global: { plugins: [router] },
    })
    mounted = wrapper

    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.attributes('href')).toBe('/jobs?status=InProgress')
    expect(wrapper.attributes('aria-label')).toBe('In progress, 2, Across 2 technicians')
    expect(wrapper.classes()).toEqual(
      expect.arrayContaining(['kpi-tile', 'kpi-tile--in-progress', 'kpi-tile--link']),
    )

    await wrapper.trigger('click')
    await router.isReady()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(router.currentRoute.value.fullPath).toBe('/jobs?status=InProgress')
  })

  it('follows the 5.16 card recipe from the tokens', () => {
    const tokens = readTokens()

    expect(ruleDeclarations(component, '.kpi-tile')).toEqual({
      display: 'flex',
      'flex-direction': 'column',
      gap: 'var(--spacing-fs-14)',
      'min-width': '0',
      padding: 'var(--spacing-fs-20)',
      border: '1px solid var(--fs-glass-g1-border)',
      'border-radius': 'var(--radius-fs-card)',
      background: 'var(--kpi-bg)',
      '-webkit-backdrop-filter': 'var(--fs-glass-g1-filter)',
      'backdrop-filter': 'var(--fs-glass-g1-filter)',
      'box-shadow': 'var(--shadow-fs-glass)',
      color: 'var(--color-fs-ink)',
      'text-decoration': 'none',
      'container-type': 'inline-size',
    })
    expect(tokens.get('--radius-fs-card')).toBe('1.25rem')
    expect(tokens.get('--spacing-fs-20')).toBe('1.25rem')
    expect(ruleDeclarations(component, '.kpi-tile--neutral')).toEqual({
      '--kpi-bg': 'var(--fs-gradient-kpi-neutral)',
    })
    expect(ruleDeclarations(component, '.kpi-tile__label')).toMatchObject({
      color: 'var(--color-fs-ink-2)',
      'font-size': 'var(--text-fs-label)',
      'font-weight': 'var(--text-fs-label--font-weight)',
    })
    expect(ruleDeclarations(component, '.kpi-tile__value')).toEqual({
      'font-size': 'var(--text-fs-kpi)',
      'font-weight': 'var(--text-fs-kpi--font-weight)',
      'line-height': 'var(--text-fs-kpi--line-height)',
      'letter-spacing': 'var(--text-fs-kpi--letter-spacing)',
      'font-variant-numeric': 'tabular-nums',
    })
    expect(ruleDeclarations(component, '.kpi-tile__subline')).toMatchObject({
      color: 'var(--color-fs-muted)',
      'font-size': 'var(--text-fs-meta)',
    })
  })

  it('stacks its head, tile over a two-line label, once the tile is narrower than its label row needs', () => {
    const narrow = '@container (width < 125px)'
    const tokens = readTokens()
    const padding = Number.parseFloat(tokens.get('--spacing-fs-20') ?? '') * 16
    const unassignedLabelWidth = 73
    const labelRow = unassignedLabelWidth + 12 + 40

    expect(labelRow + 2 * padding).toBe(165)
    expect(labelRow).toBe(125)
    expect(ruleDeclarations(component, '.kpi-tile__head', narrow)).toEqual({
      'flex-direction': 'column-reverse',
      'align-items': 'flex-start',
      'justify-content': 'flex-end',
      gap: 'var(--spacing-fs-10)',
    })
    expect(ruleDeclarations(component, '.kpi-tile__label', narrow)).toEqual({
      'min-height': '2.6em',
    })
  })

  it('keeps the label before the tile in reading order when the head stacks', () => {
    const wrapper = show({
      label: 'Unassigned',
      icon: 'circle-dashed',
      tone: 'Unassigned',
      value: '4',
    })
    const head = wrapper.find('.kpi-tile__head').element

    expect(Array.from(head.children).map((child) => child.className)).toEqual([
      'kpi-tile__label',
      'icon-tile icon-tile--40 icon-tile--unassigned',
    ])
  })

  it('turns opaque without blur under reduced transparency and draws a CanvasText border in forced colours', () => {
    expect(
      ruleDeclarations(component, '.kpi-tile', '@media (prefers-reduced-transparency: reduce)'),
    ).toEqual({
      background: 'var(--color-fs-surface)',
      '-webkit-backdrop-filter': 'none',
      'backdrop-filter': 'none',
    })
    expect(
      ruleDeclarations(
        component,
        '.kpi-tile',
        '@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)))',
      ),
    ).toEqual({ background: 'var(--fs-glass-g3-bg)' })
    expect(ruleDeclarations(component, '.kpi-tile', '@media (forced-colors: active)')).toEqual({
      'border-color': 'CanvasText',
    })
  })
})
