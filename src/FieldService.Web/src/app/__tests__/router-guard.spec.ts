import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, parseQuery, type Router } from 'vue-router'

import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import { jobList } from '@/features/jobs/__tests__/job-fixtures'
import { useSessionStore } from '@/features/auth/session-store'
import { sessionChannelName } from '@/features/auth/session-sync'
import { json, noContent, problem, stubFetch } from '@/shared/testing/fetch-stub'

import App from '../App.vue'
import { createAppRouter, installNavigationGuards, routes, stringifyQuery } from '../router'

const refreshRoute = 'POST /api/auth/refresh'

const pageRoutes = routes.filter((route) => route.redirect === undefined)

function guardedRouter(): Router {
  const router = createAppRouter(createMemoryHistory())
  installNavigationGuards(router)
  return router
}

describe('the route table', () => {
  it('declares for every page whether it needs an administrator and gives it a title', () => {
    for (const route of pageRoutes) {
      expect(typeof route.name).toBe('string')
      expect(typeof route.meta?.requiresAdministrator).toBe('boolean')
      expect(typeof route.meta?.title).toBe('string')
    }
  })

  it('holds the pages under the route names of the plan, each loaded lazily', () => {
    expect(pageRoutes.map((route) => [route.name, route.path])).toEqual([
      ['sign-in', '/sign-in'],
      ['jobs', '/jobs'],
      ['job-create', '/jobs/new'],
      ['job-detail', '/jobs/:jobId'],
      ['job-inspection', '/jobs/:jobId/inspection'],
      ['not-found', '/:pathMatch(.*)*'],
    ])
    for (const route of pageRoutes) {
      expect(typeof route.component).toBe('function')
    }
  })

  it('makes only the sign-in page public', () => {
    expect(
      pageRoutes
        .filter((route) => route.meta?.requiresAdministrator === false)
        .map((route) => route.name),
    ).toEqual(['sign-in'])
  })

  it('files the jobs pages under Jobs, with New job, the job detail and the report one level down', () => {
    const byName = Object.fromEntries(
      pageRoutes.map((route) => [String(route.name), route.meta] as const),
    )

    expect(byName.jobs?.section).toBe('jobs')
    expect(byName['job-create']?.section).toBe('jobs')
    expect(byName['job-detail']?.section).toBe('jobs')
    expect(byName['job-inspection']?.section).toBe('jobs')
    expect(byName['job-create']?.breadcrumbs?.map((crumb) => crumb.label)).toEqual(['Jobs'])
    expect(byName['job-create']?.newJobAction).toBe(false)
    expect(byName['job-detail']?.breadcrumbs?.map((crumb) => crumb.label)).toEqual(['Jobs'])
    expect(byName['job-inspection']?.breadcrumbs?.map((crumb) => crumb.label)).toEqual(['Jobs'])
    expect(byName['job-inspection']?.title).toBe('Inspection report')
    expect(byName['job-inspection']?.newJobAction).toBeUndefined()
  })
})

describe('the navigation guard', () => {
  it('waits for one session restore across navigations', async () => {
    const stub = stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push('/jobs')
    await router.push('/jobs/new')

    expect(stub.requestsTo(refreshRoute)).toHaveLength(1)
    expect(router.currentRoute.value.name).toBe('job-create')
  })

  it.each([
    '/jobs',
    '/jobs/new',
    '/jobs/01a15488-9680-7989-92af-ed07fc34a890',
    '/jobs/01a15488-9680-7989-92af-ed07fc34a890/inspection',
  ])(
    'sends a signed-out visitor from %s to sign-in with the path as the redirect',
    async (path) => {
      stubFetch({ [refreshRoute]: problem(401, 'auth.session-expired') })
      const router = guardedRouter()

      await router.push(path)

      expect(router.currentRoute.value.name).toBe('sign-in')
      expect(router.currentRoute.value.query.redirect).toBe(path)
      expect(router.currentRoute.value.fullPath).toBe(
        `/sign-in?redirect=${encodeURIComponent(path)}`,
      )
    },
  )

  it('keeps the query string of the protected path in the redirect', async () => {
    stubFetch({ [refreshRoute]: problem(401, 'auth.session-expired') })
    const router = guardedRouter()

    await router.push('/jobs?status=OnHold&page=2')

    expect(router.currentRoute.value.fullPath).toBe(
      '/sign-in?redirect=%2Fjobs%3Fstatus%3DOnHold%26page%3D2',
    )
    expect(router.currentRoute.value.query.redirect).toBe('/jobs?status=OnHold&page=2')
  })

  it('treats a path without a page as protected', async () => {
    stubFetch({ [refreshRoute]: problem(401, 'auth.session-expired') })
    const router = guardedRouter()

    await router.push('/reports')

    expect(router.currentRoute.value.name).toBe('sign-in')
    expect(router.currentRoute.value.query.redirect).toBe('/reports')
  })

  it('sends an unknown path to the not-found page for a signed-in administrator', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push('/reports/2026?view=week')

    expect(router.currentRoute.value.name).toBe('not-found')
    expect(router.currentRoute.value.fullPath).toBe('/reports/2026?view=week')
    expect(document.title).toBe('Page not found · FieldService Admin')
  })

  it('lands on the jobs list until the dashboard arrives', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push('/')

    expect(router.currentRoute.value.name).toBe('jobs')
    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it('lets a restored administrator open a protected route', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push('/jobs')

    expect(router.currentRoute.value.name).toBe('jobs')
  })

  it('sends a signed-in administrator from sign-in to a safe redirect', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push({ name: 'sign-in', query: { redirect: '/jobs/new' } })

    expect(router.currentRoute.value.name).toBe('job-create')
  })

  it('ignores an unsafe redirect for a signed-in administrator', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push({ name: 'sign-in', query: { redirect: '//evil.example/jobs' } })

    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it.each(['/..//evil.example', '/%2e%2e//evil.example', '/./\\evil.example'])(
    'ignores a redirect whose dot segments normalize to a protocol-relative path: %s',
    async (redirect) => {
      stubFetch({ [refreshRoute]: json(sessionResponse()) })
      const router = guardedRouter()

      await router.push({ name: 'sign-in', query: { redirect } })

      expect(router.currentRoute.value.fullPath).toBe('/jobs')
    },
  )

  it('shows the sign-in page to a signed-out visitor', async () => {
    stubFetch({ [refreshRoute]: problem(401, 'auth.session-expired') })
    const router = guardedRouter()

    await router.push('/sign-in')

    expect(router.currentRoute.value.name).toBe('sign-in')
    expect(document.title).toBe('Sign in · FieldService Admin')
  })

  it('names each page in the document title', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()

    await router.push('/jobs')
    expect(document.title).toBe('Jobs · FieldService Admin')

    await router.push('/jobs/new')
    expect(document.title).toBe('New job · FieldService Admin')

    await router.push('/jobs/01a15488-9680-7989-92af-ed07fc34a890')
    expect(document.title).toBe('Job details · FieldService Admin')
  })
})

describe('when the session ends', () => {
  it('returns to sign-in with the current path when the session expires', async () => {
    stubFetch({
      [refreshRoute]: [json(sessionResponse()), problem(401, 'auth.session-expired')],
    })
    const router = guardedRouter()
    await router.push('/jobs?page=2')

    await useSessionStore().refreshSession()

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('sign-in'))
    expect(router.currentRoute.value.query.redirect).toBe('/jobs?page=2')
  })

  it('returns to sign-in when another tab signs out', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()) })
    const router = guardedRouter()
    await router.push('/jobs/new')

    new BroadcastChannel(sessionChannelName).postMessage({ type: 'signed-out' })

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('sign-in'))
    expect(router.currentRoute.value.query.redirect).toBe('/jobs/new')
    expect(useSessionStore().endReason).toBe('signed-out-elsewhere')
  })

  it('opens a plain sign-in page after the administrator signs out', async () => {
    stubFetch({ [refreshRoute]: json(sessionResponse()), 'POST /api/auth/sign-out': noContent() })
    const router = guardedRouter()
    await router.push('/jobs')

    await useSessionStore().signOut()

    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('sign-in'))
    expect(router.currentRoute.value.fullPath).toBe('/sign-in')
  })
})

describe('the application frame', () => {
  it('shows signed-in pages inside the shell and the sign-in page without it', async () => {
    stubFetch({
      [refreshRoute]: json(sessionResponse()),
      'GET /api/jobs': json(jobList([], { totalCount: 0 })),
      'GET /api/technicians': json({ items: [] }),
    })
    const router = guardedRouter()
    await router.push('/reports')
    const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
    await flushPromises()

    expect(wrapper.find('nav[aria-label="Main"]').exists()).toBe(true)
    expect(wrapper.find('main#main h1').text()).toBe('Page not found')
    expect(wrapper.find('.app-shell__account').text()).toContain('Dana Whitfield')
    expect(wrapper.find('.app-shell__account').text()).toContain('Dispatcher')

    await router.push('/sign-in')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('jobs')
    expect(wrapper.find('main#main h1').text()).toBe('Jobs')
    wrapper.unmount()
  })

  it('signs out from the shell and opens a plain sign-in page', async () => {
    const stub = stubFetch({
      [refreshRoute]: [json(sessionResponse()), problem(401, 'auth.session-expired')],
      'POST /api/auth/sign-out': noContent(),
    })
    const router = guardedRouter()
    await router.push('/reports')
    const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
    await flushPromises()

    await wrapper.find('button[aria-label="Sign out"]').trigger('click')
    await flushPromises()

    expect(stub.requestsTo('POST /api/auth/sign-out')).toHaveLength(1)
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/sign-in'))
    await flushPromises()
    expect(wrapper.find('nav[aria-label="Main"]').exists()).toBe(false)
    expect(wrapper.find('[role="region"][aria-label="Notifications"]').exists()).toBe(true)
    wrapper.unmount()
  })
})

describe('stringifyQuery', () => {
  it('encodes every reserved character so the redirect stays one value', () => {
    expect(stringifyQuery({ redirect: '/jobs?status=OnHold&page=2#notes' })).toBe(
      'redirect=%2Fjobs%3Fstatus%3DOnHold%26page%3D2%23notes',
    )
  })

  it('writes repeated, empty and valueless parameters as the default serializer does', () => {
    expect(
      stringifyQuery({
        status: ['OnHold', 'Assigned'],
        search: '',
        flag: null,
        skip: undefined,
        page: 2,
      }),
    ).toBe('status=OnHold&status=Assigned&search=&flag&page=2')
    expect(stringifyQuery(undefined)).toBe('')
  })

  it('round-trips through the router query parser', () => {
    const query = { redirect: '/jobs?search=Elm Street&status=OnHold', search: 'a+b' }

    expect(parseQuery(stringifyQuery(query))).toEqual(query)
  })
})
