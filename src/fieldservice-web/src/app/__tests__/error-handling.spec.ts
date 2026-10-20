import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, onMounted } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { reportError } from '@/features/diagnostics/error-reporter'

import { installErrorHandling, type RemoveErrorHandling } from '../error-handling'

vi.mock('@/features/diagnostics/error-reporter', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/features/diagnostics/error-reporter')>()
  return { ...original, reportError: vi.fn(original.reportError) }
})

const EmptyPage = defineComponent({ render: () => h('div') })

function createTestRouter(failure: Error): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: EmptyPage },
      { path: '/jobs', component: EmptyPage },
      { path: '/broken', component: () => Promise.reject(failure) },
    ],
  })
}

function unhandledRejection(reason: unknown): Event {
  const event = new Event('unhandledrejection')
  Object.defineProperty(event, 'reason', { value: reason })
  return event
}

describe('installErrorHandling', () => {
  let removeErrorHandling: RemoveErrorHandling | undefined

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
  })

  afterEach(() => {
    removeErrorHandling?.()
    removeErrorHandling = undefined
  })

  it('reports an error thrown inside a component through app.config.errorHandler', async () => {
    const failure = new Error('Mounted hook failed')
    const router = createTestRouter(new Error('unused'))
    await router.push('/jobs')
    const FailingComponent = defineComponent({
      setup() {
        onMounted(() => {
          throw failure
        })
        return () => h('div')
      },
    })
    const app = createApp(FailingComponent)
    removeErrorHandling = installErrorHandling(app, router)

    app.mount(document.createElement('div'))
    app.unmount()

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'app.errorHandler', '/jobs')
  })

  it('reports an unhandled promise rejection through the window unhandledrejection event', () => {
    const failure = new Error('Request promise rejected')
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(failure))

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'window.unhandledrejection', '/')
  })

  it('reports a failed navigation through router.onError with the target route path', async () => {
    const failure = new Error('Route component failed to load')
    const router = createTestRouter(failure)
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    await expect(router.push('/broken?tab=history#notes')).rejects.toBe(failure)

    expect(reportError).toHaveBeenCalledTimes(1)
    expect(reportError).toHaveBeenCalledWith(failure, 'router.onError', '/broken')
  })

  it('builds a report with the message, source, route path, stack and time of the error', () => {
    const failure = new Error('Request promise rejected')
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection(failure))

    const report = vi.mocked(reportError).mock.results[0]?.value
    expect(report).toEqual({
      message: 'Request promise rejected',
      source: 'window.unhandledrejection',
      route: '/',
      stack: failure.stack,
      occurredAt: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/),
    })
  })

  it('describes rejection reasons that are not Error objects', () => {
    const router = createTestRouter(new Error('unused'))
    removeErrorHandling = installErrorHandling(createApp(EmptyPage), router)

    window.dispatchEvent(unhandledRejection('Socket ticket request failed'))
    window.dispatchEvent(unhandledRejection(undefined))

    const reports = vi.mocked(reportError).mock.results.map((result) => result.value)
    expect(reports).toMatchObject([
      { message: 'Socket ticket request failed', stack: null },
      { message: 'Unknown error', stack: null },
    ])
  })

  it('stops reporting once the returned cleanup has run', async () => {
    const routerWarning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const failure = new Error('Route component failed to load')
    const router = createTestRouter(failure)
    const app = createApp(EmptyPage)
    installErrorHandling(app, router)()

    window.dispatchEvent(unhandledRejection(new Error('Late rejection')))
    await expect(router.push('/broken')).rejects.toBe(failure)

    expect(reportError).not.toHaveBeenCalled()
    expect(app.config.errorHandler).toBeUndefined()
    expect(routerWarning).toHaveBeenCalled()
  })
})
