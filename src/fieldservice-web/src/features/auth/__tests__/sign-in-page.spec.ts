import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, type Router, type RouteRecordRaw } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards, routes } from '@/app/router'
import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'
import AppIcon from '@/shared/ui/AppIcon.vue'
import type { IconName } from '@/shared/ui/icons'

import { useSessionStore } from '../session-store'
import {
  demoAdministratorEmail,
  password,
  sessionResponse,
  sessionResponseBody,
} from './session-fixtures'

const signInRoute = 'POST /api/auth/sign-in'

const emptyPage = { render: () => null }

const workflowRoutes: RouteRecordRaw[] = [
  { path: '/jobs', name: 'jobs', component: emptyPage, meta: { requiresAdministrator: true } },
]

interface SignInPage {
  readonly wrapper: VueWrapper
  readonly router: Router
  readonly stub: FetchStub
}

let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

async function openSignIn(
  routeTable: RouteTable = {},
  path = '/sign-in?redirect=%2Fjobs',
): Promise<SignInPage> {
  const stub = stubFetch({
    'POST /api/auth/refresh': problem(401, 'auth.session-expired'),
    ...routeTable,
  })
  const router = createAppRouter(createMemoryHistory(), [...routes, ...workflowRoutes])
  installNavigationGuards(router)
  await router.push(path)
  const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()
  return { wrapper, router, stub }
}

async function submitCredentials(
  wrapper: VueWrapper,
  email: string,
  secret: string,
): Promise<void> {
  await wrapper.find('#sign-in-email').setValue(email)
  await wrapper.find('#sign-in-password').setValue(secret)
  await wrapper.find('form').trigger('submit')
  await flushPromises()
}

function alertText(wrapper: VueWrapper): string {
  return wrapper.find('[role="alert"]').text()
}

describe('the sign-in page', () => {
  it('shows the hero, a labelled form and the technician hint', async () => {
    const { wrapper } = await openSignIn()

    const hero = wrapper.find('section[aria-label="FieldService Admin"]')
    expect(hero.attributes('data-surface')).toBe('dark')
    expect(hero.find('h2').text()).toBe('Dispatch and track field jobs in real time.')
    expect(wrapper.find('.sign-in-hero__glimpse').attributes('aria-hidden')).toBe('true')
    expect(wrapper.find('main h1').text()).toBe('Sign in')
    expect(wrapper.find('form').attributes('aria-labelledby')).toBe('sign-in-title')
    expect(wrapper.find('label[for="sign-in-email"]').text()).toBe('Email')
    expect(wrapper.find('label[for="sign-in-password"]').text()).toBe('Password')
    expect(wrapper.find('button[type="submit"]').text()).toBe('Sign in')
    expect(wrapper.text()).toContain('Technicians sign in from the FieldService Tech app.')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('shows field messages for missing values without calling the API', async () => {
    const { wrapper, stub } = await openSignIn()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const email = wrapper.find('#sign-in-email')
    const secret = wrapper.find('#sign-in-password')
    expect(email.attributes('aria-invalid')).toBe('true')
    expect(wrapper.find(`#${email.attributes('aria-describedby')}`).text()).toBe(
      'Enter your email address.',
    )
    expect(secret.attributes('aria-invalid')).toBe('true')
    expect(wrapper.find(`#${secret.attributes('aria-describedby')}`).text()).toBe(
      'Enter your password.',
    )
    expect(document.activeElement).toBe(email.element)
    expect(stub.requestsTo(signInRoute)).toHaveLength(0)
  })

  it('asks for a complete email address', async () => {
    const { wrapper, stub } = await openSignIn()

    await submitCredentials(wrapper, 'dana.whitfield', password())

    expect(wrapper.find('#sign-in-email-error').text()).toBe(
      'Enter a complete email address, such as name@fieldservice.example.',
    )
    expect(wrapper.find('#sign-in-password').attributes('aria-invalid')).toBeUndefined()
    expect(stub.requestsTo(signInRoute)).toHaveLength(0)
  })

  it('signs in and opens the redirect path', async () => {
    const secret = password()
    const { wrapper, router, stub } = await openSignIn({
      [signInRoute]: json(sessionResponse()),
    })

    await submitCredentials(wrapper, `  ${demoAdministratorEmail} `, secret)

    expect(JSON.parse(stub.requestsTo(signInRoute)[0]?.body ?? '')).toEqual({
      email: demoAdministratorEmail,
      password: secret,
      client: 'Web',
    })
    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it('opens the landing page instead of an unsafe redirect', async () => {
    const { wrapper, router } = await openSignIn(
      { [signInRoute]: json(sessionResponse()) },
      '/sign-in?redirect=https%3A%2F%2Fevil.example%2Fjobs',
    )

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it('shows the invalid-credentials message for a 401', async () => {
    const { wrapper, router } = await openSignIn({
      [signInRoute]: problem(401, 'auth.invalid-credentials'),
    })

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    expect(alertText(wrapper)).toBe('The email or password is incorrect.')
    expect(wrapper.find('[role="alert"]').text()).not.toContain('Reference')
    expect(router.currentRoute.value.name).toBe('sign-in')
  })

  it('tells a technician to use the FieldService Tech app', async () => {
    const { wrapper } = await openSignIn({
      [signInRoute]: problem(403, 'auth.wrong-application'),
    })

    await submitCredentials(wrapper, 'marco.reyes@fieldservice.example', password())

    expect(alertText(wrapper)).toBe(
      'This is a technician account. Technicians sign in from the FieldService Tech app.',
    )
  })

  it('tells the user how long to wait after too many attempts', async () => {
    const { wrapper } = await openSignIn({
      [signInRoute]: problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '42' }),
    })

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    expect(alertText(wrapper)).toBe('Too many attempts. Try again in 42 seconds.')
  })

  it('shows the reference of an unexpected failure', async () => {
    const { wrapper, stub } = await openSignIn({ [signInRoute]: problem(500, 'server.error') })

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    const reference = stub.requestsTo(signInRoute)[0]?.headers.get('X-Correlation-ID') ?? ''
    expect(reference).not.toBe('')
    expect(alertText(wrapper)).toBe(
      `Sign-in could not be completed. Try again in a moment. Reference ${reference}`,
    )
  })

  it('says when the API cannot be reached', async () => {
    const { wrapper } = await openSignIn({ [signInRoute]: networkFailure() })

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    expect(alertText(wrapper)).toBe(
      'FieldService could not be reached. Check your connection, then try again.',
    )
  })

  it('shows server field errors on their inputs', async () => {
    const { wrapper } = await openSignIn({
      [signInRoute]: problem(400, 'request.validation-failed', {
        errors: { email: ['Enter a valid email address.'] },
      }),
    })

    await submitCredentials(wrapper, demoAdministratorEmail, password())

    expect(wrapper.find('#sign-in-email').attributes('aria-invalid')).toBe('true')
    expect(wrapper.find('#sign-in-email-error').text()).toBe('Enter a valid email address.')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('disables the button while the request runs and sends one request', async () => {
    const reply = deferred()
    const { wrapper, router, stub } = await openSignIn({ [signInRoute]: reply.reply })

    await wrapper.find('#sign-in-email').setValue(demoAdministratorEmail)
    await wrapper.find('#sign-in-password').setValue(password())
    await wrapper.find('form').trigger('submit')
    await reply.requested
    const button = wrapper.find('button[type="submit"]')

    expect(button.attributes('aria-disabled')).toBe('true')
    expect(button.text()).toBe('Signing in…')
    await button.trigger('click')
    await wrapper.find('form').trigger('submit')
    expect(stub.requestsTo(signInRoute)).toHaveLength(1)

    reply.resolve(sessionResponseBody(`access-${crypto.randomUUID()}`))
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/jobs')
  })

  it.each([
    ['hidden', false],
    ['shown', true],
  ])('keeps spell check and autocorrection off with the password %s', async (_state, shown) => {
    const { wrapper } = await openSignIn()
    const toggle = wrapper.find('button[aria-label="Show password"]')
    expect(toggle.attributes('aria-pressed')).toBe('false')

    if (shown) {
      await toggle.trigger('click')
    }

    const email = wrapper.find('#sign-in-email')
    const secret = wrapper.find('#sign-in-password')
    expect(toggle.attributes('aria-pressed')).toBe(String(shown))
    expect(secret.attributes('type')).toBe(shown ? 'text' : 'password')
    expect(email.attributes('type')).toBe('email')
    expect(email.attributes('autocomplete')).toBe('username')
    expect(secret.attributes('autocomplete')).toBe('current-password')
    for (const input of [email, secret]) {
      expect(input.attributes('spellcheck')).toBe('false')
      expect(input.attributes('autocapitalize')).toBe('off')
      expect(input.attributes('autocorrect')).toBe('off')
    }
  })

  it('explains that the session ended, with the clock icon', async () => {
    const { wrapper } = await openSignIn()

    useSessionStore().$patch({ endReason: 'expired' })
    await flushPromises()

    const notice = wrapper.find('[role="status"]')
    expect(notice.text()).toBe('Your session ended. Sign in again to continue.')
    expect(notice.find('.banner__tile svg').element.innerHTML).toBe(iconShapes('clock'))
  })

  it('explains a sign-out from another tab, with the sign-out icon', async () => {
    const { wrapper } = await openSignIn()

    useSessionStore().$patch({ endReason: 'signed-out-elsewhere' })
    await flushPromises()

    const notice = wrapper.find('[role="status"]')
    expect(notice.text()).toBe('You signed out in another tab.')
    expect(notice.find('.banner__tile svg').element.innerHTML).toBe(iconShapes('log-out'))
  })

  it('turns the hero compact below 600 pixels so the form starts in the first screen', () => {
    const compact = '@media (width < 600px)'

    expect(
      ruleDeclarations('features/auth/components/SignInHero.vue', '.sign-in-hero', compact),
    ).toEqual({ gap: 'var(--spacing-fs-24)', padding: 'var(--spacing-fs-32) var(--spacing-fs-24)' })
    expect(
      ruleDeclarations(
        'features/auth/components/SignInHero.vue',
        '.sign-in-hero__glimpse',
        compact,
      ),
    ).toEqual({ display: 'none' })
    expect(
      ruleDeclarations('features/auth/pages/SignInPage.vue', '.sign-in-page__main', compact),
    ).toEqual({ padding: 'var(--spacing-fs-24) var(--spacing-fs-16) var(--spacing-fs-48)' })
  })
})

function iconShapes(name: IconName): string {
  return mount(AppIcon, { props: { name, size: 18 } }).element.innerHTML
}
