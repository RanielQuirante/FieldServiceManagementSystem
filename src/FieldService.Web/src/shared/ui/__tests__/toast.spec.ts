import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { clearanceRules, focusClearanceMargin } from '@/shared/composables/use-focus-clearance'
import {
  errorToastBody,
  errorToastTitle,
  eventToastMilliseconds,
  maximumToasts,
  narrowMaximumEventToasts,
  narrowMaximumToasts,
  useToasts,
  wideToastLayoutQuery,
} from '@/shared/composables/use-toasts'

import Toast from '../Toast.vue'
import ToastHost from '../ToastHost.vue'

const reference = 'f0766650-82a1-49f8-b5cc-f3103d346544'

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

function mountHost(placement: 'shell' | 'sign-in' = 'shell'): VueWrapper {
  mounted = mount(ToastHost, { props: { placement }, attachTo: document.body })
  return mounted
}

function titles(): string[] {
  return useToasts().toasts.map((toast) => toast.title)
}

describe('the toast queue', () => {
  it('keeps the oldest first and at most three toasts from 600px', () => {
    const toasts = useToasts()

    for (const title of ['First', 'Second', 'Third', 'Fourth']) {
      toasts.show({ title })
    }

    expect(maximumToasts).toBe(3)
    expect(titles()).toEqual(['Second', 'Third', 'Fourth'])
  })

  it('shows one event toast at a time below 600px, and two toasts in all', () => {
    const toasts = useToasts()
    toasts.setLayout('narrow')

    toasts.show({ title: 'First' })
    toasts.show({ title: 'Second' })
    expect(narrowMaximumEventToasts).toBe(1)
    expect(titles()).toEqual(['Second'])

    toasts.showError(reference)
    expect(narrowMaximumToasts).toBe(2)
    expect(titles()).toEqual(['Second', errorToastTitle])

    toasts.show({ title: 'Third' })
    expect(titles()).toEqual([errorToastTitle, 'Third'])
  })

  it('trims event toasts when the window narrows', () => {
    const toasts = useToasts()
    toasts.show({ title: 'First' })
    toasts.show({ title: 'Second' })

    toasts.setLayout('narrow')

    expect(titles()).toEqual(['Second'])
  })

  it('never removes an error toast to make room', () => {
    const toasts = useToasts()
    toasts.showError('a')
    toasts.showError('b')
    toasts.showError('c')

    toasts.show({ title: 'Event' })
    toasts.showError('d')

    expect(useToasts().toasts.map((toast) => toast.reference)).toEqual(['a', 'b', 'c', 'd'])
  })

  it('keeps a held toast and lets the next oldest event toast leave instead', () => {
    const toasts = useToasts()
    const held = toasts.show({ title: 'Held' })
    toasts.show({ title: 'Second' })
    toasts.show({ title: 'Third' })
    toasts.hold(held)

    toasts.show({ title: 'Fourth' })
    expect(titles()).toEqual(['Held', 'Third', 'Fourth'])

    toasts.setLayout('narrow')
    expect(titles()).toEqual(['Held'])

    toasts.show({ title: 'Fifth' })
    expect(titles()).toEqual(['Held', 'Fifth'])

    toasts.release(held)
    expect(titles()).toEqual(['Fifth'])
  })

  it('closes event toasts after 8 seconds and keeps error toasts until dismissed', () => {
    const toasts = useToasts()

    toasts.show({ title: 'Priya Natarajan started work' })
    toasts.showError(reference)

    expect(eventToastMilliseconds).toBe(8_000)
    expect(toasts.toasts.map((toast) => toast.durationMs)).toEqual([8_000, null])
  })

  it('writes the error toast copy and shows each reference once', () => {
    const toasts = useToasts()

    const first = toasts.showError(reference)
    const repeat = toasts.showError(reference)
    toasts.showError(null)

    expect(repeat).toBe(first)
    expect(toasts.toasts).toHaveLength(2)
    expect(toasts.toasts[0]).toMatchObject({
      kind: 'error',
      title: errorToastTitle,
      body: errorToastBody,
      reference,
    })
  })

  it('adds a reference only to a shown error toast that has none', () => {
    const toasts = useToasts()
    const pending = toasts.showError(null)
    const failedCall = toasts.showError(reference)
    const event = toasts.show({ title: 'Tom Becker put a job on hold' })
    const accepted = '927ba8e3-24aa-4d0e-8ef1-d8c4203c37fe'

    expect(toasts.attachReference(pending, accepted)).toBe(true)
    expect(toasts.attachReference(pending, reference)).toBe(false)
    expect(toasts.attachReference(failedCall, accepted)).toBe(false)
    expect(toasts.attachReference(event, accepted)).toBe(false)
    expect(toasts.attachReference(9_999, accepted)).toBe(false)

    expect(toasts.toasts.map((toast) => [toast.id, toast.reference])).toEqual([
      [pending, accepted],
      [failedCall, reference],
      [event, null],
    ])
  })

  it('removes a dismissed toast, and every toast on clear', () => {
    const toasts = useToasts()
    const id = toasts.show({ title: 'Tom Becker put a job on hold' })

    toasts.dismiss(id)
    expect(toasts.toasts).toEqual([])

    toasts.show({ title: 'One' })
    toasts.showError(reference)
    toasts.clear()
    expect(toasts.toasts).toEqual([])
  })
})

describe('ToastHost', () => {
  it('is a region named Notifications with a hidden polite status, present before any toast', () => {
    const host = mountHost()

    expect(host.attributes()).toMatchObject({ role: 'region', 'aria-label': 'Notifications' })
    expect(host.attributes('aria-live')).toBeUndefined()
    const status = host.find('[role="status"]')
    expect(status.attributes('aria-atomic')).toBe('false')
    expect(status.classes()).toContain('visually-hidden')
    expect(status.text()).toBe('')
  })

  it('gives each event toast one line in the status and removes it when the toast leaves', async () => {
    const host = mountHost()
    const toasts = useToasts()
    const first = toasts.show({ title: 'A', announcement: 'JOB-1049. A. Body.' })
    toasts.show({ title: 'B', announcement: 'JOB-1049. A. Body.' })
    toasts.showError(reference)
    await nextTick()

    const lines = (): string[] => host.findAll('[role="status"] p').map((line) => line.text())
    expect(lines()).toEqual(['JOB-1049. A. Body.', 'JOB-1049. A. Body.'])

    toasts.dismiss(first)
    await nextTick()
    expect(lines()).toEqual(['JOB-1049. A. Body.'])
  })

  it('renders the oldest toast first and removes a toast when it is dismissed', async () => {
    const host = mountHost()
    const toasts = useToasts()
    toasts.show({ title: 'First' })
    toasts.show({ title: 'Second' })
    await nextTick()

    expect(host.findAll('.toast__title').map((title) => title.text())).toEqual(['First', 'Second'])

    await host.findAll('.toast button')[0]?.trigger('click')

    expect(titles()).toEqual(['Second'])
  })

  it('follows the 600px layout query', async () => {
    const listeners: (() => void)[] = []
    const query = {
      matches: true,
      addEventListener: (_type: string, listener: () => void) => listeners.push(listener),
      removeEventListener: vi.fn(),
    }
    const matchMedia = vi.fn(() => query)
    vi.stubGlobal('matchMedia', matchMedia)
    mountHost('sign-in')

    expect(matchMedia).toHaveBeenCalledWith(wideToastLayoutQuery)
    expect(wideToastLayoutQuery).toBe('(min-width: 600px)')
    expect(useToasts().layout).toBe('wide')

    query.matches = false
    listeners.forEach((listener) => listener())
    await nextTick()
    expect(useToasts().layout).toBe('narrow')
  })

  it('moves focus to the next newer toast after a dismissal, then the older one, then back', async () => {
    document.body.innerHTML =
      '<main id="main" tabindex="-1"><button id="origin">Search</button></main>'
    const host = mountHost()
    const toasts = useToasts()
    toasts.show({ title: 'First' })
    toasts.show({ title: 'Second' })
    toasts.show({ title: 'Third' })
    await nextTick()
    const origin = document.getElementById('origin')
    origin?.focus()
    const buttons = (): HTMLElement[] =>
      host.findAll<HTMLElement>('.toast button').map((button) => button.element)

    buttons()[1]?.focus()
    expect(document.activeElement).toBe(buttons()[1])
    await host.findAll('.toast button')[1]?.trigger('click')
    await nextTick()
    await nextTick()
    expect(document.activeElement?.closest('.toast')?.textContent).toContain('Third')

    const remaining = host.findAll('.toast button')
    await remaining[remaining.length - 1]?.trigger('click')
    await nextTick()
    await nextTick()
    expect(document.activeElement?.closest('.toast')?.textContent).toContain('First')

    await host.findAll('.toast button')[0]?.trigger('click')
    await nextTick()
    await nextTick()
    expect(document.activeElement).toBe(origin)
  })

  it('falls back to main when the element focused before is gone', async () => {
    document.body.innerHTML = '<main id="main" tabindex="-1"></main>'
    const host = mountHost()
    const toasts = useToasts()
    toasts.show({ title: 'Only' })
    await nextTick()

    host.find<HTMLElement>('.toast button').element.focus()
    await host.find('.toast button').trigger('click')
    await nextTick()
    await nextTick()

    expect(document.activeElement?.id).toBe('main')
  })
})

describe('ToastHost and keyboard focus', () => {
  const observed: Element[] = []
  let notify: (() => void) | null = null
  let disconnected = 0

  class FakeResizeObserver {
    constructor(callback: () => void) {
      notify = callback
    }

    observe(target: Element): void {
      observed.push(target)
    }

    unobserve(): void {}

    disconnect(): void {
      disconnected += 1
    }
  }

  beforeEach(() => {
    observed.length = 0
    notify = null
    disconnected = 0
    vi.stubGlobal('ResizeObserver', FakeResizeObserver)
    Object.defineProperty(document, 'adoptedStyleSheets', {
      value: [],
      writable: true,
      configurable: true,
    })
  })

  afterEach(() => {
    Reflect.deleteProperty(document, 'adoptedStyleSheets')
  })

  function clearance(): string {
    return document.adoptedStyleSheets
      .flatMap((sheet) => Array.from(sheet.cssRules, (rule) => rule.cssText))
      .join(' ')
  }

  function stackOf(host: VueWrapper, height: () => number): Element {
    const stack = host.find('.toast-host__stack').element
    vi.spyOn(stack, 'getBoundingClientRect').mockImplementation(
      () => ({ height: height() }) as DOMRect,
    )
    return stack
  }

  it('writes the bottom scroll padding and the end padding of main from numbers only', () => {
    expect(clearanceRules(114.6, '--spacing-fs-56')).toBe(
      [
        ':root { scroll-padding-bottom: 115px; }',
        '#main { padding-bottom: max(var(--spacing-fs-56), 115px); }',
      ].join('\n'),
    )
    expect(clearanceRules(80, '--spacing-fs-48')).toContain('max(var(--spacing-fs-48), 80px)')
  })

  it.each([
    ['shell', '--spacing-fs-56'],
    ['sign-in', '--spacing-fs-48'],
  ] as const)(
    'pads the %s page bottom by the stack height plus 32 pixels while a toast shows',
    async (placement, base) => {
      const host = mountHost(placement)
      let height = 0
      const stack = stackOf(host, () => height)
      const toasts = useToasts()

      expect(focusClearanceMargin).toBe(32)
      expect(observed).toEqual([stack])

      const id = toasts.showError(reference)
      await nextTick()
      height = 114.6
      notify?.()
      expect(clearance()).toContain('scroll-padding-bottom: 147px')
      expect(clearance()).toContain(`max(var(${base}), 147px)`)
      expect(clearance()).not.toContain('scroll-padding-top')

      toasts.dismiss(id)
      await nextTick()
      height = 0
      notify?.()
      expect(clearance()).toBe('')
    },
  )

  it('stops watching and removes the padding when the host goes away', async () => {
    const host = mountHost()
    stackOf(host, () => 120)
    useToasts().showError(reference)
    await nextTick()
    notify?.()
    expect(clearance()).toContain('scroll-padding-bottom: 152px')

    host.unmount()
    mounted = null

    expect(disconnected).toBe(1)
    expect(clearance()).toBe('')
  })
})

describe('Toast', () => {
  it('shows an error as an alert on the dark surface with its reference and a named dismiss button', () => {
    const toasts = useToasts()
    toasts.showError(reference)
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }

    const wrapper = mount(Toast, { props: { toast } })
    const alert = wrapper.find('[role="alert"]')

    expect(wrapper.attributes('data-surface')).toBe('dark')
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(alert.find('.toast__title').text()).toBe('Something went wrong')
    expect(alert.find('.toast__body').text()).toBe(
      'Your last action may not have finished. Check the page, then try again.',
    )
    expect(alert.find('.toast__reference').text()).toBe(`Reference ${reference}`)
    expect(alert.find('.toast__tile').attributes('aria-hidden')).toBe('true')
    expect(alert.find('button').attributes('aria-label')).toBe('Dismiss notification')
  })

  it('announces the error once: a reference that arrives later joins outside the alert', async () => {
    const toasts = useToasts()
    const id = toasts.showError(null)
    const shown = toasts.toasts[0]
    if (shown === undefined) {
      throw new Error('No toast queued')
    }
    const wrapper = mount(Toast, { props: { toast: shown } })
    const alert = wrapper.find('[role="alert"]')
    const announcedBefore = alert.element.innerHTML

    toasts.attachReference(id, reference)
    const updated = toasts.toasts[0]
    if (updated === undefined) {
      throw new Error('No toast queued')
    }
    await wrapper.setProps({ toast: updated })

    const later = wrapper.find('.toast__reference--later')
    expect(later.text()).toBe(`Reference ${reference}`)
    expect(alert.element.contains(later.element)).toBe(false)
    expect(alert.element.innerHTML).toBe(announcedBefore)
    expect(later.element.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull()
    expect(wrapper.findAll('.toast__reference')).toHaveLength(1)
  })

  it('keeps a reference known at once inside the alert, where it is read with the error', () => {
    const toasts = useToasts()
    toasts.showError(reference)
    const shown = toasts.toasts[0]
    if (shown === undefined) {
      throw new Error('No toast queued')
    }

    const wrapper = mount(Toast, { props: { toast: shown } })

    expect(wrapper.find('[role="alert"] .toast__reference').text()).toBe(`Reference ${reference}`)
    expect(wrapper.find('.toast__reference--later').exists()).toBe(false)
  })

  it('lines a later reference up with the text column under the body', () => {
    expect(ruleDeclarations('shared/ui/Toast.vue', '.toast__reference--later')).toEqual({
      margin: '0',
      padding: '0 calc(2.25rem + var(--spacing-fs-12)) 0 calc(32px + var(--spacing-fs-12))',
    })
    expect(ruleDeclarations('shared/ui/Toast.vue', '.toast')).toMatchObject({
      'flex-direction': 'column',
      gap: 'var(--spacing-fs-4)',
    })
    expect(ruleDeclarations('shared/ui/Toast.vue', '.toast__text')).toMatchObject({
      gap: 'var(--spacing-fs-4)',
    })
    expect(ruleDeclarations('shared/ui/Toast.vue', '.toast__tile')).toMatchObject({
      width: '32px',
    })
  })

  it('leaves out the reference line when there is no reference', () => {
    const toasts = useToasts()
    toasts.showError(null)
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }

    const wrapper = mount(Toast, { props: { toast } })

    expect(wrapper.find('.toast__reference').exists()).toBe(false)
  })

  it('adds the reference line in place once the reference arrives, keeping focus where it was', async () => {
    const toasts = useToasts()
    const host = mountHost()
    const id = toasts.showError(null)
    await nextTick()
    const dismiss = host.find<HTMLButtonElement>(`[data-toast-id="${id}"] button`)
    dismiss.element.focus()

    toasts.attachReference(id, reference)
    await nextTick()

    const shown = host.find(`[data-toast-id="${id}"]`)
    expect(shown.find('.toast__reference').text()).toBe(`Reference ${reference}`)
    expect(host.findAll('.toast')).toHaveLength(1)
    expect(document.activeElement).toBe(dismiss.element)
  })

  it('shows an event without a live role, with the hidden separator and the note, and never takes focus', () => {
    const toasts = useToasts()
    toasts.show({
      title: 'Tom Becker put a job on hold',
      body: 'Water heater leaking at base is now On hold.',
      note: '“Waiting for replacement gas valve”',
      meta: 'JOB-1044',
      metaDetail: '10:31 AM',
    })
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }
    const before = document.activeElement

    const wrapper = mount(Toast, { props: { toast }, attachTo: document.body })

    expect(wrapper.attributes('role')).toBeUndefined()
    expect(wrapper.findAll('[role]')).toHaveLength(0)
    const meta = wrapper.find('.toast__meta')
    expect(meta.text()).toBe('JOB-1044\u{00a0}\u{00b7} , 10:31 AM')
    expect(meta.find('[aria-hidden="true"]').element.textContent).toBe('\u{00a0}\u{00b7} ')
    expect(meta.find('[aria-hidden="true"]').text()).toBe('·')
    expect(meta.find('.visually-hidden').text()).toBe(',')
    expect(wrapper.find('.toast__note').text()).toBe('“Waiting for replacement gas valve”')
    expect(wrapper.find('.toast__dot').attributes('aria-hidden')).toBe('true')
    expect(document.activeElement).toBe(before)
    wrapper.unmount()
  })

  it('dismisses an event toast after its time, pausing while hovered or focused', async () => {
    vi.useFakeTimers()
    const toasts = useToasts()
    toasts.show({ title: 'Tom Becker resumed work' })
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }
    const wrapper = mount(Toast, { props: { toast }, attachTo: document.body })

    vi.advanceTimersByTime(5_000)
    await wrapper.trigger('mouseenter')
    vi.advanceTimersByTime(20_000)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
    expect(wrapper.emitted('hold')).toHaveLength(1)

    await wrapper.trigger('mouseleave')
    expect(wrapper.emitted('release')).toHaveLength(1)
    await wrapper.find('button').trigger('focusin')
    vi.advanceTimersByTime(20_000)
    expect(wrapper.emitted('dismiss')).toBeUndefined()

    await wrapper.find('button').trigger('focusout')
    vi.advanceTimersByTime(2_999)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
    vi.advanceTimersByTime(1)
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
    wrapper.unmount()
  })

  it('counts its 8 seconds only while the page is visible', async () => {
    vi.useFakeTimers()
    let visibility: DocumentVisibilityState = 'visible'
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
    const toasts = useToasts()
    toasts.show({ title: 'Aisha Karim completed a job' })
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }
    const wrapper = mount(Toast, { props: { toast }, attachTo: document.body })

    vi.advanceTimersByTime(3_000)
    visibility = 'hidden'
    document.dispatchEvent(new Event('visibilitychange'))
    vi.advanceTimersByTime(10 * 60_000)
    expect(wrapper.emitted('dismiss')).toBeUndefined()

    visibility = 'visible'
    document.dispatchEvent(new Event('visibilitychange'))
    vi.advanceTimersByTime(4_999)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
    vi.advanceTimersByTime(1)
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
    wrapper.unmount()
  })

  it('draws a solid Canvas surface without the backdrop blur in forced colours', () => {
    const toasts = useToasts()
    toasts.show({ title: 'Tom Becker resumed work' })
    toasts.showError(reference)
    const roots = toasts.toasts.map((toast) => {
      const wrapper = mount(Toast, { props: { toast } })
      const isToast = wrapper.classes('toast')
      wrapper.unmount()
      return isToast
    })

    expect(roots).toEqual([true, true])
    expect(ruleDeclarations('shared/ui/Toast.vue', '.toast')).toMatchObject({
      background: 'var(--fs-gradient-toast)',
      'backdrop-filter': 'blur(var(--blur-fs-glass))',
    })
    expect(
      ruleDeclarations('shared/ui/Toast.vue', '.toast', '@media (forced-colors: active)'),
    ).toEqual({
      background: 'Canvas',
      '-webkit-backdrop-filter': 'none',
      'backdrop-filter': 'none',
      'border-color': 'CanvasText',
    })
  })

  it('never closes an error toast by itself', () => {
    vi.useFakeTimers()
    const toasts = useToasts()
    toasts.showError(reference)
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }
    const wrapper = mount(Toast, { props: { toast } })

    vi.advanceTimersByTime(60 * 60_000)

    expect(wrapper.emitted('dismiss')).toBeUndefined()
  })
})
