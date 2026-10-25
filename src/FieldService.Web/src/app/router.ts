import {
  createRouter,
  createWebHistory,
  isNavigationFailure,
  START_LOCATION,
  type LocationQueryRaw,
  type RouteLocationNormalized,
  type RouteLocationRaw,
  type Router,
  type RouterHistory,
  type RouteRecordRaw,
} from 'vue-router'

import { redirectFromQuery } from '@/features/auth/redirect'
import { useSessionStore } from '@/features/auth/session-store'
import { formatDocumentTitle, resetPageTitle } from '@/shared/composables/use-page-title'

export interface BreadcrumbLink {
  readonly label: string
  readonly to: RouteLocationRaw
}

declare module 'vue-router' {
  interface RouteMeta {
    requiresAdministrator?: boolean
    title?: string
    section?: 'dashboard' | 'jobs' | 'technicians'
    breadcrumbs?: readonly BreadcrumbLink[]
    newJobAction?: boolean
  }
}

const jobsCrumb: BreadcrumbLink = { label: 'Jobs', to: { name: 'jobs' } }

export const landingRouteName = 'dashboard'

export const routes: RouteRecordRaw[] = [
  {
    path: '/sign-in',
    name: 'sign-in',
    component: () => import('@/features/auth/pages/SignInPage.vue'),
    meta: { requiresAdministrator: false, title: 'Sign in' },
  },
  {
    path: '/',
    name: 'dashboard',
    component: () => import('@/features/dashboard/pages/DashboardPage.vue'),
    meta: { requiresAdministrator: true, title: 'Dashboard', section: 'dashboard' },
  },
  {
    path: '/jobs',
    name: 'jobs',
    component: () => import('@/features/jobs/pages/JobsPage.vue'),
    meta: { requiresAdministrator: true, title: 'Jobs', section: 'jobs' },
  },
  {
    path: '/jobs/new',
    name: 'job-create',
    component: () => import('@/features/jobs/pages/CreateJobPage.vue'),
    meta: {
      requiresAdministrator: true,
      title: 'New job',
      section: 'jobs',
      breadcrumbs: [jobsCrumb],
      newJobAction: false,
    },
  },
  {
    path: '/jobs/:jobId',
    name: 'job-detail',
    component: () => import('@/features/jobs/pages/JobDetailPage.vue'),
    meta: {
      requiresAdministrator: true,
      title: 'Job details',
      section: 'jobs',
      breadcrumbs: [jobsCrumb],
    },
  },
  {
    path: '/jobs/:jobId/inspection',
    name: 'job-inspection',
    component: () => import('@/features/jobs/pages/InspectionReportPage.vue'),
    meta: {
      requiresAdministrator: true,
      title: 'Inspection report',
      section: 'jobs',
      breadcrumbs: [jobsCrumb],
    },
  },
  {
    path: '/technicians',
    name: 'technicians',
    component: () => import('@/features/technicians/pages/TechniciansPage.vue'),
    meta: { requiresAdministrator: true, title: 'Technicians', section: 'technicians' },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('./NotFoundPage.vue'),
    meta: { requiresAdministrator: true, title: 'Page not found' },
  },
]

export function usesAppShell(route: RouteLocationNormalized): boolean {
  return route.matched.length > 0 && route.meta.requiresAdministrator !== false
}

export function stringifyQuery(query: LocationQueryRaw | undefined): string {
  const pairs: string[] = []
  for (const [key, raw] of Object.entries(query ?? {})) {
    const values = Array.isArray(raw) ? raw : [raw]
    for (const value of values) {
      if (value === undefined) {
        continue
      }
      const name = encodeURIComponent(key)
      pairs.push(value === null ? name : `${name}=${encodeURIComponent(String(value))}`)
    }
  }
  return pairs.join('&')
}

export function signInLocation(redirect: string): RouteLocationRaw {
  return { name: 'sign-in', query: { redirect } }
}

export function pageTitle(route: RouteLocationNormalized): string {
  return formatDocumentTitle(route.meta.title)
}

function requiresAdministrator(route: RouteLocationNormalized): boolean {
  return route.meta.requiresAdministrator !== false
}

export function createAppRouter(
  history: RouterHistory = createWebHistory(import.meta.env.BASE_URL),
  routeRecords: readonly RouteRecordRaw[] = routes,
): Router {
  return createRouter({ history, routes: [...routeRecords], stringifyQuery })
}

export function installNavigationGuards(router: Router): void {
  const session = useSessionStore()

  router.beforeEach(async (to) => {
    await session.restore()
    if (requiresAdministrator(to)) {
      return session.isAdministrator ? true : signInLocation(to.fullPath)
    }
    if (to.name === 'sign-in' && session.isAdministrator) {
      return redirectFromQuery(to.query.redirect)
    }
    return true
  })

  router.afterEach((to, from, failure) => {
    if (isNavigationFailure(failure) || (to.path === from.path && from !== START_LOCATION)) {
      return
    }
    if (to.path !== from.path) {
      resetPageTitle()
    }
    document.title = pageTitle(to)
  })

  session.onSessionEnded((reason) => {
    const current = router.currentRoute.value
    if (!requiresAdministrator(current)) {
      return
    }
    void router.replace(
      reason === 'signed-out' ? { name: 'sign-in' } : signInLocation(current.fullPath),
    )
  })
}

export const router = createAppRouter()
