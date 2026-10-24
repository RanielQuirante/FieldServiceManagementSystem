import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { h, nextTick } from 'vue'

import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { clickBackdrop, isModal, pressEscape } from '@/shared/testing/dialog'

import ConfirmDialog from '../ConfirmDialog.vue'

type DialogProps = InstanceType<typeof ConfirmDialog>['$props']

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView')
})

const baseProps = {
  open: true,
  title: 'Cancel this job?',
  describedBy: 'job-line status-row consequence',
  dismissLabel: 'Keep job',
  confirmLabel: 'Cancel job',
  confirmBusyLabel: 'Cancelling…',
} satisfies DialogProps

function show(props: Partial<DialogProps> = {}, withBanner = false): VueWrapper {
  const wrapper = mount(ConfirmDialog, {
    props: { ...baseProps, ...props },
    slots: {
      description: () => h('p', { id: 'job-line' }, 'JOB-1051 · Replace faulty breaker panel'),
      default: () => h('textarea', { 'aria-label': 'Reason for cancelling' }),
      ...(withBanner ? { banner: () => h('div', { role: 'alert' }, 'Failure') } : {}),
    },
    attachTo: document.body,
  })
  mounted = wrapper
  return wrapper
}

function dialogOf(wrapper: VueWrapper): HTMLDialogElement {
  return wrapper.find<HTMLDialogElement>('dialog').element
}

function button(wrapper: VueWrapper, label: string): HTMLButtonElement {
  const found = wrapper
    .findAll<HTMLButtonElement>('button')
    .find((candidate) => candidate.text() === label)
  if (found === undefined) {
    throw new Error(`No button ${label}`)
  }
  return found.element
}

describe('ConfirmDialog', () => {
  it('is a native dialog shown modal, labelled by its heading and described by the ids it is given', async () => {
    const wrapper = show()
    await nextTick()
    const dialog = dialogOf(wrapper)
    const heading = wrapper.find('h2')

    expect(dialog.tagName).toBe('DIALOG')
    expect(isModal(dialog)).toBe(true)
    expect(dialog.getAttribute('aria-labelledby')).toBe(heading.attributes('id'))
    expect(heading.text()).toBe('Cancel this job?')
    expect(dialog.getAttribute('aria-describedby')).toBe('job-line status-row consequence')
    expect(wrapper.find('.icon-tile--fail').attributes('aria-hidden')).toBe('true')
    expect(wrapper.findAll('.confirm-dialog__actions button').map((item) => item.text())).toEqual([
      'Keep job',
      'Cancel job',
    ])
    expect(wrapper.find('.confirm-dialog__banner').exists()).toBe(false)
  })

  it('closes when open turns false, without emitting a dismissal', async () => {
    const wrapper = show()
    await nextTick()

    await wrapper.setProps({ open: false })

    expect(dialogOf(wrapper).open).toBe(false)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
  })

  it('asks to be dismissed by Keep job and by Esc, and never by a click on the backdrop', async () => {
    const wrapper = show()
    await nextTick()

    clickBackdrop(dialogOf(wrapper))
    expect(wrapper.emitted('dismiss')).toBeUndefined()
    expect(dialogOf(wrapper).open).toBe(true)

    button(wrapper, 'Keep job').click()
    expect(wrapper.emitted('dismiss')).toHaveLength(1)

    pressEscape(dialogOf(wrapper))
    expect(wrapper.emitted('dismiss')).toHaveLength(2)
  })

  it('ignores Keep job and Esc while busy, keeps the Esc key from closing it, and shows itself again if the browser closes it anyway', async () => {
    const wrapper = show({ busy: true })
    await nextTick()
    const dialog = dialogOf(wrapper)

    button(wrapper, 'Keep job').click()
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    dialog.dispatchEvent(escape)
    const cancel = new Event('cancel', { cancelable: true })
    dialog.dispatchEvent(cancel)

    expect(escape.defaultPrevented).toBe(true)
    expect(cancel.defaultPrevented).toBe(true)
    expect(wrapper.emitted('dismiss')).toBeUndefined()

    dialog.close()
    expect(isModal(dialog)).toBe(true)
    expect(document.activeElement).toBe(wrapper.find('.app-button--danger').element)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
  })

  it('asks to confirm from the danger button, which reads the busy label without confirming again while busy', async () => {
    const wrapper = show()
    await nextTick()

    button(wrapper, 'Cancel job').click()
    expect(wrapper.emitted('confirm')).toHaveLength(1)

    await wrapper.setProps({ busy: true })
    const busy = button(wrapper, 'Cancelling…')
    busy.click()

    expect(busy.getAttribute('aria-disabled')).toBe('true')
    expect(busy.classList.contains('app-button--danger')).toBe(true)
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })

  it('scrolls the banner and then the actions into view at the nearest edge, without smooth scrolling', async () => {
    const scrolled: { element: Element; options: unknown }[] = []
    HTMLElement.prototype.scrollIntoView = function (this: Element, options?: unknown) {
      scrolled.push({ element: this, options })
    }
    const wrapper = show({}, true)
    await nextTick()

    ;(wrapper.vm as unknown as { revealBannerAndActions(): void }).revealBannerAndActions()

    expect(
      scrolled.map((entry) => [(entry.element as HTMLElement).className, entry.options]),
    ).toEqual([
      ['confirm-dialog__banner', { block: 'nearest' }],
      ['confirm-dialog__actions', { block: 'nearest' }],
    ])
    const parts = Array.from(dialogOf(wrapper).children, (child) => child.className)
    expect(parts.slice(-2)).toEqual(['confirm-dialog__banner', 'confirm-dialog__actions'])
  })

  it('focuses the danger button on request', async () => {
    const wrapper = show()
    await nextTick()

    ;(wrapper.vm as unknown as { focusConfirm(): void }).focusConfirm()

    expect(document.activeElement).toBe(button(wrapper, 'Cancel job'))
  })

  it('draws the sheet surface with a CanvasText border in forced colours and shows only while open', () => {
    const file = 'shared/ui/ConfirmDialog.vue'

    expect(ruleDeclarations(file, '.confirm-dialog')).toMatchObject({
      padding: 'var(--spacing-fs-24)',
      border: '1px solid var(--fs-glass-sheet-border)',
      'border-radius': 'var(--radius-fs-card)',
      'box-shadow': 'var(--shadow-fs-dialog)',
      gap: 'var(--spacing-fs-20)',
    })
    expect(ruleDeclarations(file, '.confirm-dialog[open]')).toEqual({ display: 'flex' })
    expect(ruleDeclarations(file, '.confirm-dialog', '@media (forced-colors: active)')).toEqual({
      'border-color': 'CanvasText',
    })
  })
})
