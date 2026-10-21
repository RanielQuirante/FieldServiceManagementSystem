import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { focusClearanceMargin } from '@/shared/composables/use-focus-clearance'
import {
  errorToastBody,
  errorToastTitle,
  eventToastMilliseconds,
  maximumToasts,
  useToasts,
} from '@/shared/composables/use-toasts'

import Toast from '../Toast.vue'
import ToastHost from '../ToastHost.vue'

const reference = 'f0766650-82a1-49f8-b5cc-f3103d346544'

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

function mountHost(placement: 'main' | 'viewport' = 'main'): VueWrapper {
  mounted = mount(ToastHost, { props: { placement }, attachTo: document.body })
  return mounted
}

describe('the toast queue', () => {
  it('keeps at most three toasts with the newest first', () => {
    const toasts = useToasts()

    for (const title of ['First', 'Second', 'Third', 'Fourth']) {
      toasts.show({ title })
    }

    expect(maximumToasts).toBe(3)
    expect(toasts.toasts.map((toast) => toast.title)).toEqual(['Fourth', 'Third', 'Second'])
  })

  it('closes event toasts after 8 seconds and keeps error toasts until dismissed', () => {
    const toasts = useToasts()

    toasts.show({ title: 'Priya Natarajan started work' })
    toasts.showError(reference)

    expect(eventToastMilliseconds).toBe(8_000)
    expect(toasts.toasts.map((toast) => toast.durationMs)).toEqual([null, 8_000])
  })

  it('writes the error toast copy and shows each reference once', () => {
    const toasts = useToasts()

    const first = toasts.showError(reference)
    const repeat = toasts.showError(reference)
    toasts.showError(null)

    expect(repeat).toBe(first)
    expect(toasts.toasts).toHaveLength(2)
    expect(toasts.toasts[1]).toMatchObject({
      kind: 'error',
      title: errorToastTitle,
      body: errorToastBody,
      reference,
    })
  })

  it('removes a dismissed toast', () => {
    const toasts = useToasts()
    const id = toasts.show({ title: 'Tom Becker put a job on hold' })

    toasts.dismiss(id)

    expect(toasts.toasts).toEqual([])
  })
})

describe('ToastHost', () => {
  it('is a polite live region named Notifications, present before any toast', () => {
    const host = mountHost()

    expect(host.attributes()).toMatchObject({
      role: 'region',
      'aria-label': 'Notifications',
      'aria-live': 'polite',
    })
    expect(host.findAll('[role="status"], [role="alert"]')).toHaveLength(0)
  })

  it('renders the queue newest first and removes a toast when it is dismissed', async () => {
    const host = mountHost()
    const toasts = useToasts()
    toasts.show({ title: 'First' })
    toasts.show({ title: 'Second' })
    await nextTick()

    const titles = host.findAll('.toast__title').map((title) => title.text())
    expect(titles).toEqual(['Second', 'First'])

    await host.findAll('button')[0]?.trigger('click')

    expect(toasts.toasts.map((toast) => toast.title)).toEqual(['First'])
  })

  it('places itself in main or fixed to the viewport', () => {
    expect(mountHost('viewport').classes()).toContain('toast-host--viewport')
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

  function scrollPadding(): string {
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

  it.each(['main', 'viewport'] as const)(
    'pads the document scroll by the %s toast stack height plus 32 pixels while a toast shows',
    async (placement) => {
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
      expect(scrollPadding()).toMatch(/^:root\s*\{\s*scroll-padding-top: 147px;\s*\}$/)

      toasts.dismiss(id)
      await nextTick()
      height = 0
      notify?.()
      expect(scrollPadding()).toBe('')
    },
  )

  it('stops watching and removes the padding when the host goes away', async () => {
    const host = mountHost()
    stackOf(host, () => 120)
    useToasts().showError(reference)
    await nextTick()
    notify?.()
    expect(scrollPadding()).toContain('scroll-padding-top: 152px')

    host.unmount()
    mounted = null

    expect(disconnected).toBe(1)
    expect(scrollPadding()).toBe('')
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

    expect(wrapper.attributes('role')).toBe('alert')
    expect(wrapper.attributes('data-surface')).toBe('dark')
    expect(wrapper.find('.toast__title').text()).toBe('Something went wrong')
    expect(wrapper.find('.toast__body').text()).toBe(
      'Your last action may not have finished. Check the page, then try again.',
    )
    expect(wrapper.find('.toast__reference').text()).toBe(`Reference ${reference}`)
    expect(wrapper.find('.toast__tile').attributes('aria-hidden')).toBe('true')
    expect(wrapper.find('button').attributes('aria-label')).toBe('Dismiss notification')
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

  it('shows an event as a status and never takes focus', () => {
    const toasts = useToasts()
    toasts.show({ title: 'Priya Natarajan started work', meta: 'JOB-1049 · just now' })
    const toast = toasts.toasts[0]
    if (toast === undefined) {
      throw new Error('No toast queued')
    }
    const before = document.activeElement

    const wrapper = mount(Toast, { props: { toast }, attachTo: document.body })

    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.find('.toast__meta').text()).toBe('JOB-1049 · just now')
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

    await wrapper.trigger('mouseleave')
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
