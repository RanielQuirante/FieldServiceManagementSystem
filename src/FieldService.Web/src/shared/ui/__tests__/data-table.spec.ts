import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'

import {
  installStyles,
  readStyles,
  ruleDeclarations,
} from '@/app/styles/__tests__/stylesheet-source'

import DataTable, { type DataTableColumn, type DataTableState } from '../DataTable.vue'
import Pagination, { rangeSummary } from '../Pagination.vue'

interface Row {
  readonly id: string
  readonly title: string
  readonly customer: string
}

const columns: readonly DataTableColumn[] = [
  { key: 'title', label: 'Job', skeleton: ['job-number', 'job-title'] },
  { key: 'customer', label: 'Customer and site', skeleton: ['customer', 'site'] },
]

const rows: readonly Row[] = [
  { id: 'a', title: 'Replace faulty breaker panel', customer: 'Pinecrest Public Library' },
  { id: 'b', title: 'Leaking rooftop drain', customer: 'Harborview Apartments' },
]

const denseColumns: readonly DataTableColumn[] = Array.from({ length: 7 }, (_, index) => ({
  key: `c${index}`,
  label: `C${index}`,
}))

let mounted: VueWrapper | null = null
let removeStyles: (() => void) | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  removeStyles?.()
  removeStyles = null
})

function paddings(element: Element): string[] {
  const style = getComputedStyle(element)
  return [style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft]
}

const simpleSelectorPart = /\.[\w-]+|:[\w-]+|\[[^\]]*\]/g

function specificity(selector: string): readonly number[] {
  const classLike = selector.match(simpleSelectorPart) ?? []
  const types = selector.replace(simpleSelectorPart, ' ').match(/[a-z][\w-]*/gi) ?? []
  return [selector.match(/#[\w-]+/g)?.length ?? 0, classLike.length, types.length]
}

function outranks(selector: string, other: string): boolean {
  const [a, b] = [specificity(selector), specificity(other)]
  const index = a.findIndex((value, position) => value !== b[position])
  return index >= 0 && (a[index] ?? 0) > (b[index] ?? 0)
}

function cellPaddingSelectors(file: string): string[] {
  return Array.from(readStyles(file).matchAll(/([^{}]+)\{([^{}]*)\}/g))
    .filter((rule) => /(^|[\s;{])padding(-left|-right)?\s*:/.test(rule[2] ?? ''))
    .flatMap((rule) => (rule[1] ?? '').split(',').map((selector) => selector.trim()))
    .filter((selector) => /(^|[\s>])td(:[\w-]+)*$/.test(selector))
}

function mountTable(state: DataTableState, extra: Record<string, unknown> = {}): VueWrapper {
  mounted = mount(DataTable, {
    attachTo: document.body,
    props: {
      caption: 'Jobs',
      columns,
      rows,
      rowKey: (row: unknown) => (row as Row).id,
      state,
      loadingLabel: 'Loading jobs',
      ...extra,
    },
    slots: {
      cell: ({ row, column }: { row: unknown; column: DataTableColumn }) =>
        column.key === 'title' ? (row as Row).title : (row as Row).customer,
      empty: () => h('p', 'No jobs match these filters'),
      footer: () => h('p', 'Showing 1–2 of 2 jobs'),
    },
  })
  return mounted
}

describe('DataTable', () => {
  it('names the table with a visually hidden caption and scopes each header to its column', () => {
    const table = mountTable('content')

    expect(table.find('caption').text()).toBe('Jobs')
    expect(table.find('caption').classes()).toContain('visually-hidden')
    expect(table.findAll('th').map((header) => header.attributes('scope'))).toEqual(['col', 'col'])
    expect(table.findAll('th').map((header) => header.text())).toEqual(['Job', 'Customer and site'])
  })

  it('scrolls inside a labelled, focusable region', () => {
    const region = mountTable('content').find('.data-table__scroll')

    expect(region.attributes()).toMatchObject({
      role: 'region',
      tabindex: '0',
      'aria-label': 'Jobs',
    })
  })

  it('renders one row per item with the cell slot and a footer', () => {
    const table = mountTable('content')
    const bodyRows = table.findAll('tbody tr')

    expect(bodyRows).toHaveLength(2)
    expect(bodyRows[0]?.findAll('td').map((cell) => cell.text())).toEqual([
      'Replace faulty breaker panel',
      'Pinecrest Public Library',
    ])
    expect(table.find('.data-table__footer').text()).toBe('Showing 1–2 of 2 jobs')
    expect(table.find('table').attributes('aria-busy')).toBeUndefined()
  })

  it('keeps the real header row while loading, with hidden skeleton rows and one status message', () => {
    const table = mountTable('loading')
    const bodyRows = table.findAll('tbody tr')

    expect(table.find('table').attributes('aria-busy')).toBe('true')
    expect(table.findAll('th')).toHaveLength(2)
    expect(bodyRows).toHaveLength(5)
    expect(bodyRows.every((row) => row.attributes('aria-hidden') === 'true')).toBe(true)
    expect(bodyRows[0]?.findAll('.loading-state__block')).toHaveLength(4)
    expect(table.findAll('[role="status"]').map((status) => status.text())).toEqual([
      'Loading jobs',
    ])
    expect(table.find('.data-table__footer').exists()).toBe(false)
  })

  it('marks the table busy while it refreshes rows it already shows', () => {
    const table = mountTable('content', { busy: true })

    expect(table.find('table').attributes('aria-busy')).toBe('true')
    expect(table.findAll('tbody tr')).toHaveLength(2)
    expect(table.find('[role="status"]').text()).toBe('Loading jobs')
  })

  it('puts the empty state in one row under the header, spanning every column', () => {
    const table = mountTable('empty')
    const cells = table.findAll('tbody td')

    expect(cells).toHaveLength(1)
    expect(cells[0]?.attributes('colspan')).toBe('2')
    expect(cells[0]?.text()).toBe('No jobs match these filters')
    expect(table.findAll('th')).toHaveLength(2)
  })

  it.each([
    ['two columns', columns],
    ['seven dense columns', denseColumns],
  ])(
    'gives the empty-state cell zero padding on every side over the edge padding, with %s',
    (_description, tableColumns) => {
      removeStyles = installStyles('shared/ui/DataTable.vue')
      const table = mountTable('empty', { columns: tableColumns, minWidth: 980 })
      const cell = table.find('.data-table__empty-cell').element

      expect(cell.matches('td:first-child:last-child')).toBe(true)
      expect(paddings(cell)).toEqual(['0px', '0px', '0px', '0px'])
    },
  )

  it('zeroes the empty-state cell with a rule that outranks every cell padding rule', () => {
    const emptyCell = '.data-table__row--empty > .data-table__empty-cell'
    const others = cellPaddingSelectors('shared/ui/DataTable.vue')

    expect(ruleDeclarations('shared/ui/DataTable.vue', emptyCell)).toEqual({ padding: '0' })
    expect(others).toEqual(
      expect.arrayContaining(['td', '.data-table--dense td', 'td:first-child', 'td:last-child']),
    )
    expect(others.filter((selector) => !outranks(emptyCell, selector))).toEqual([])
  })

  it('keeps the 20px edge padding on the first and last cells of a content row', () => {
    removeStyles = installStyles('shared/ui/DataTable.vue')
    const cells = mountTable('content').findAll('tbody tr:first-child td')

    expect(cells.map((cell) => paddings(cell.element))).toEqual([
      ['14px', '16px', '14px', '20px'],
      ['14px', '20px', '14px', '16px'],
    ])
  })

  it('uses the dense padding for seven or more columns and a minimum width when asked', () => {
    const table = mountTable('content', { columns: denseColumns, minWidth: 980 })

    expect(table.classes()).toContain('data-table--dense')
    expect(table.find('table').classes()).toContain('data-table__table--min-980')
  })

  it('drops its own glass inside a section card', () => {
    expect(mountTable('content', { inCard: true }).classes()).toContain('data-table--in-card')
  })

  it('marks updated rows by key', async () => {
    const table = mountTable('content', { updatedKeys: new Set(['b']) })

    expect(table.findAll('tbody tr').map((row) => row.classes('data-table__row--updated'))).toEqual(
      [false, true],
    )

    await table.setProps({ updatedKeys: new Set<string>() })
    expect(table.findAll('.data-table__row--updated')).toHaveLength(0)
  })

  it('gives updated cells the flash, which wins over the hover tint', () => {
    const css = readStyles('shared/ui/DataTable.vue')
    const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g)).map((rule) => ({
      prelude: (rule[1] ?? '').trim(),
      body: rule[2] ?? '',
      index: rule.index,
    }))
    const flash = rules.find((rule) => rule.body.includes('var(--color-fs-flash)'))
    const hover = rules.find((rule) => rule.body.includes('var(--color-fs-row-hover)'))
    const flashSelectors = flash?.prelude.split(',').map((selector) => selector.trim()) ?? []
    const flashOnHover = flashSelectors.find((selector) => selector.includes(':hover')) ?? ''
    const hoverSelector = (hover?.prelude ?? '').replace(/:not\([^)]*\)/g, '.not')

    expect(flashSelectors).toEqual(
      expect.arrayContaining(['.data-table__row.data-table__row--updated td']),
    )
    expect(flashOnHover).toContain(':hover')
    expect(hoverSelector).toContain(':hover')
    expect(outranks(hoverSelector, flashOnHover)).toBe(false)
    expect((flash?.index ?? 0) > (hover?.index ?? 0)).toBe(true)
  })

  it('moves focus to the scroll region when the row holding focus leaves', async () => {
    const table = mountTable('content')
    table.find<HTMLElement>('tbody tr td').element.setAttribute('tabindex', '-1')
    table.find<HTMLElement>('tbody tr td').element.focus()

    await table.setProps({ rows: rows.slice(1) })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(document.activeElement?.getAttribute('role')).toBe('region')
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Jobs')
  })

  it('leaves focus alone when the focused row stays', async () => {
    const table = mountTable('content')
    const cell = table.findAll<HTMLElement>('tbody tr td')[2]?.element
    cell?.setAttribute('tabindex', '-1')
    cell?.focus()

    await table.setProps({ rows: [...rows] })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(document.activeElement).toBe(cell)
  })
})

describe('Pagination', () => {
  function mountPagination(page: number, totalCount = 20): VueWrapper {
    mounted = mount(Pagination, {
      props: { page, pageSize: 12, totalCount, noun: 'jobs', nounSingular: 'job' },
      attachTo: document.body,
    })
    return mounted
  }

  it('summarises the rows shown and sits in a navigation landmark', () => {
    const pagination = mountPagination(1)

    expect(pagination.find('.pagination__summary').text()).toBe('Showing 1–12 of 20 jobs')
    expect(pagination.find('nav').attributes('aria-label')).toBe('Pagination')
  })

  it('disables Previous on the first page and Next on the last', () => {
    const first = mountPagination(1)
    const [previous, next] = first.findAll('button')

    expect(previous?.text()).toBe('Previous')
    expect(previous?.attributes('disabled')).toBeDefined()
    expect(next?.text()).toBe('Next')
    expect(next?.attributes('disabled')).toBeUndefined()
    first.unmount()

    const last = mountPagination(2)
    expect(last.find('.pagination__summary').text()).toBe('Showing 13–20 of 20 jobs')
    expect(last.findAll('button')[1]?.attributes('disabled')).toBeDefined()
  })

  it.each([
    [1, 'Next', 'Previous'],
    [2, 'Previous', 'Next'],
  ])(
    'shows a teal chevron on the enabled button of page %i and the disabled colour on the other',
    (page, enabledLabel, disabledLabel) => {
      removeStyles = installStyles('shared/ui/Pagination.vue')
      const buttons = mountPagination(page).findAll('button')
      const enabled = buttons.find((button) => button.text() === enabledLabel)
      const disabled = buttons.find((button) => button.text() === disabledLabel)

      expect(enabled?.attributes('disabled')).toBeUndefined()
      expect(disabled?.attributes('disabled')).toBeDefined()
      expect(getComputedStyle(enabled?.find('.app-icon').element as Element).color).toBe(
        'var(--color-fs-teal-700)',
      )
      expect(getComputedStyle(enabled?.element as Element).color).toBe('var(--color-fs-ink)')
      expect(getComputedStyle(disabled?.find('.app-icon').element as Element).color).toBe(
        'var(--color-fs-disabled-text)',
      )
    },
  )

  it('asks for the next and previous pages', async () => {
    const pagination = mountPagination(1, 40)

    await pagination.findAll('button')[1]?.trigger('click')
    await pagination.setProps({ page: 2 })
    await pagination.findAll('button')[0]?.trigger('click')

    expect(pagination.emitted('change')).toEqual([[2], [1]])
  })

  it('moves focus to Previous when Next becomes disabled under it', async () => {
    const Host = defineComponent({
      setup() {
        const page = ref(1)
        return () =>
          h(Pagination, {
            page: page.value,
            pageSize: 12,
            totalCount: 20,
            onChange: (value: number) => {
              page.value = value
            },
          })
      },
    })
    mounted = mount(Host, { attachTo: document.body })
    const next = mounted.findAll('button')[1]
    ;(next?.element as HTMLButtonElement).focus()

    await next?.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(document.activeElement?.textContent?.trim()).toBe('Previous')
  })

  it('writes single rows and the singular noun', () => {
    expect(rangeSummary(1, 12, 1, 'jobs', 'job')).toBe('Showing 1 of 1 job')
    expect(rangeSummary(2, 12, 13, 'jobs', 'job')).toBe('Showing 13 of 13 jobs')
  })
})
