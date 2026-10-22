import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router'

import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { usePageTitle } from '@/shared/composables/use-page-title'

import AppButton from '../AppButton.vue'
import AppIcon from '../AppIcon.vue'
import AppShell from '../AppShell.vue'
import Avatar from '../Avatar.vue'
import Banner from '../Banner.vue'
import CountCapsule from '../CountCapsule.vue'
import EmptyState from '../EmptyState.vue'
import ErrorState from '../ErrorState.vue'
import FilterChipGroup from '../FilterChipGroup.vue'
import IconButton from '../IconButton.vue'
import { icons, type IconName } from '../icons'
import IconTile from '../IconTile.vue'
import LoadingState from '../LoadingState.vue'
import PageHeader from '../PageHeader.vue'
import PriorityLabel from '../PriorityLabel.vue'
import SectionCard from '../SectionCard.vue'
import SegmentedControl from '../SegmentedControl.vue'
import SelectField from '../SelectField.vue'
import StatusChip from '../StatusChip.vue'
import TextArea from '../TextArea.vue'
import TextField from '../TextField.vue'

const specIconNames = [
  'check',
  'x',
  'minus',
  'plus',
  'chevron-right',
  'chevron-left',
  'chevron-down',
  'chevron-up',
  'arrow-left',
  'search',
  'filter',
  'clock',
  'calendar',
  'map-pin',
  'building',
  'user',
  'users',
  'user-check',
  'phone',
  'wrench',
  'clipboard-check',
  'circle-dashed',
  'check-circle',
  'x-circle',
  'pause-circle',
  'play-circle',
  'alert-triangle',
  'refresh',
  'wifi-off',
  'bell',
  'grid',
  'list',
  'log-out',
  'more-vertical',
  'navigation',
  'lock',
  'eye',
  'message',
  'send',
  'activity',
  'thermometer',
  'mail',
]

describe('AppIcon', () => {
  it('holds exactly the icon names of the design spec', () => {
    expect(Object.keys(icons)).toEqual(specIconNames)
  })

  it('renders a hidden 24-unit stroke icon at the requested size', () => {
    const wrapper = mount(AppIcon, { props: { name: 'clipboard-check', size: 18 } })
    const svg = wrapper.find('svg')

    expect(svg.attributes()).toMatchObject({
      'aria-hidden': 'true',
      focusable: 'false',
      width: '18',
      height: '18',
      viewBox: '0 0 24 24',
      fill: 'none',
      stroke: 'currentColor',
      'stroke-width': '2',
    })
    expect(svg.findAll('rect')).toHaveLength(1)
    expect(svg.findAll('path')).toHaveLength(2)
  })

  it('uses the heavier stroke at 14 and 16 pixels and keeps dashed circles', () => {
    const wrapper = mount(AppIcon, { props: { name: 'circle-dashed' as IconName, size: 14 } })

    expect(wrapper.find('svg').attributes('stroke-width')).toBe('2.25')
    expect(wrapper.find('circle').attributes('stroke-dasharray')).toBe('3 3')
  })

  it('lets every icon take its control text colour under forced colours', () => {
    const wrapper = mount(AppIcon, { props: { name: 'user-check', size: 18 } })

    expect(wrapper.find('svg').classes()).toContain('app-icon')
    expect(
      ruleDeclarations('app/styles/base.css', '.app-icon', '@media (forced-colors: active)'),
    ).toEqual({ 'forced-color-adjust': 'auto' })
  })
})

describe('StatusChip', () => {
  it.each([
    ['Unassigned', 'Unassigned', 'circle'],
    ['Assigned', 'Assigned', 'path'],
    ['InProgress', 'In progress', 'path'],
    ['OnHold', 'On hold', 'path'],
    ['Completed', 'Completed', 'path'],
    ['Cancelled', 'Cancelled', 'path'],
  ])('shows the %s status as a hidden icon and the label %s', (status, label, shape) => {
    const wrapper = mount(StatusChip, { props: { status } })

    expect(wrapper.text()).toBe(label)
    expect(wrapper.find('svg').attributes('aria-hidden')).toBe('true')
    expect(wrapper.find(`svg ${shape}`).exists()).toBe(true)
  })

  it('shows an unknown status as its raw value with neutral styling', () => {
    const wrapper = mount(StatusChip, { props: { status: 'Escalated' } })

    expect(wrapper.text()).toBe('Escalated')
    expect(wrapper.classes()).toContain('status-chip--unknown')
  })

  it.each([
    ['on', 'On shift', 'var(--fs-gradient-chip-completed)', 'var(--color-fs-teal-700)'],
    ['off', 'Off shift', 'var(--fs-gradient-chip-cancelled)', 'var(--color-fs-line-input)'],
  ] as const)(
    'shows the %s shift variant as a hidden dot and the label %s',
    (shift, label, fill, dot) => {
      const wrapper = mount(StatusChip, { props: { shift } })

      expect(wrapper.text()).toBe(label)
      expect(wrapper.classes()).toEqual([
        'status-chip',
        'status-chip--shift',
        `status-chip--${shift}-shift`,
      ])
      expect(wrapper.find('svg').exists()).toBe(false)
      expect(wrapper.find('.status-chip__dot').attributes('aria-hidden')).toBe('true')
      expect(
        ruleDeclarations('shared/ui/StatusChip.vue', `.status-chip--${shift}-shift`),
      ).toMatchObject({
        '--chip-bg': fill,
        '--chip-dot': dot,
      })
    },
  )
})

describe('TextField', () => {
  it('ties the input to its label', () => {
    const wrapper = mount(TextField, { props: { label: 'Site address', id: 'site-address' } })

    expect(wrapper.find('label').attributes('for')).toBe('site-address')
    expect(wrapper.find('input').attributes('id')).toBe('site-address')
    expect(wrapper.find('input').attributes('aria-invalid')).toBeUndefined()
  })

  it('creates a unique id for each field when none is given', () => {
    const wrapper = mount({
      render: () => [h(TextField, { label: 'Title' }), h(TextField, { label: 'Customer' })],
    })
    const ids = wrapper.findAll('input').map((input) => input.attributes('id'))
    const targets = wrapper.findAll('label').map((label) => label.attributes('for'))

    expect(ids.every((id) => typeof id === 'string' && id.length > 0)).toBe(true)
    expect(new Set(ids).size).toBe(2)
    expect(targets).toEqual(ids)
  })

  it('ties the error line to an invalid input', () => {
    const wrapper = mount(TextField, {
      props: { label: 'Site address', id: 'site-address', error: 'Enter the site address.' },
    })
    const input = wrapper.find('input')

    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe('site-address-error')
    expect(wrapper.find('#site-address-error').text()).toBe('Enter the site address.')
    expect(wrapper.find('#site-address-error svg').attributes('aria-hidden')).toBe('true')
  })

  it('describes the input with its help, after the error when one shows', async () => {
    const wrapper = mount(TextField, {
      props: { label: 'Equipment', id: 'equipment', help: 'Name the unit or system to work on.' },
    })
    const input = wrapper.find('input')

    expect(input.attributes('aria-describedby')).toBe('equipment-help')
    expect(wrapper.find('#equipment-help').text()).toBe('Name the unit or system to work on.')

    await wrapper.setProps({ error: 'Use 200 characters or fewer.' })

    expect(input.attributes('aria-describedby')).toBe('equipment-error equipment-help')
    expect(wrapper.findAll('p').map((line) => line.attributes('id'))).toEqual([
      'equipment-error',
      'equipment-help',
    ])
  })

  it('marks an optional field in its label', () => {
    const wrapper = mount(TextField, { props: { label: 'Contact name', optional: true } })

    expect(wrapper.find('label').text()).toBe('Contact name Optional')
  })

  it('names a search field by its placeholder', () => {
    const wrapper = mount(TextField, { props: { label: 'Search jobs', type: 'search' } })
    const input = wrapper.find('input')

    expect(wrapper.find('label').exists()).toBe(false)
    expect(input.attributes('aria-label')).toBe('Search jobs')
    expect(input.attributes('placeholder')).toBe('Search jobs')
    expect(wrapper.find('svg').attributes('aria-hidden')).toBe('true')
  })

  it('updates its model', async () => {
    const wrapper = mount(TextField, { props: { label: 'Title', modelValue: '' } })

    await wrapper.find('input').setValue('Exterior signage lights out')

    expect(wrapper.emitted('update:modelValue')).toEqual([['Exterior signage lights out']])
  })

  it('leaves spell check alone on fields that do not hold credentials', () => {
    const wrapper = mount(TextField, { props: { label: 'Title' } })
    const input = wrapper.find('input')

    expect(input.attributes('spellcheck')).toBeUndefined()
    expect(input.attributes('autocorrect')).toBeUndefined()
  })

  it('switches the password between hidden and shown with a pressed toggle', async () => {
    const wrapper = mount(TextField, { props: { label: 'Password', type: 'password' } })
    const toggle = wrapper.find('button')

    expect(toggle.attributes('type')).toBe('button')
    expect(toggle.attributes('aria-label')).toBe('Show password')
    expect(toggle.attributes('aria-pressed')).toBe('false')
    await toggle.trigger('click')
    expect(toggle.attributes('aria-pressed')).toBe('true')
    expect(wrapper.find('input').attributes('type')).toBe('text')
    expect(wrapper.find('input').attributes('spellcheck')).toBe('false')
  })
})

describe('Banner', () => {
  it('is a status message by default', () => {
    const wrapper = mount(Banner, {
      props: { title: 'Back online.', message: 'Jobs refreshed at 10:44 AM.', variant: 'success' },
    })

    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.text()).toBe('Back online. Jobs refreshed at 10:44 AM.')
    expect(wrapper.find('.banner__tile').attributes('aria-hidden')).toBe('true')
  })

  it('becomes an alert and shows the reference after a failed action', () => {
    const wrapper = mount(Banner, {
      props: {
        variant: 'error',
        role: 'alert',
        title: 'Jobs could not be loaded.',
        message: 'Check your connection, then try again.',
        reference: 'dd85f06c-7606-4dfc-a702-fa39647047d8',
      },
    })

    expect(wrapper.attributes('role')).toBe('alert')
    expect(wrapper.find('.banner__reference').text()).toBe(
      'Reference dd85f06c-7606-4dfc-a702-fa39647047d8',
    )
  })

  it('renders an action slot', () => {
    const wrapper = mount(Banner, {
      props: { title: 'Live updates are paused.' },
      slots: { actions: () => h('button', { type: 'button' }, 'Refresh now') },
    })

    expect(wrapper.find('button').text()).toBe('Refresh now')
  })
})

describe('AppButton', () => {
  it('is a plain button unless it submits a form', () => {
    expect(mount(AppButton, { slots: { default: 'Save' } }).attributes('type')).toBe('button')
    expect(
      mount(AppButton, { props: { type: 'submit' }, slots: { default: 'Save' } }).attributes(
        'type',
      ),
    ).toBe('submit')
  })

  it('shows a progressive label and stops clicks while busy', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppButton, {
      props: { busy: true, busyLabel: 'Creating job…' },
      slots: { default: 'Create job' },
      attrs: { onClick },
    })

    await wrapper.trigger('click')

    expect(wrapper.text()).toBe('Creating job…')
    expect(wrapper.attributes('aria-disabled')).toBe('true')
    expect(wrapper.attributes('disabled')).toBeUndefined()
    expect(onClick).not.toHaveBeenCalled()
  })

  it('uses the native disabled state when disabled', () => {
    const wrapper = mount(AppButton, { props: { disabled: true }, slots: { default: 'Assign' } })

    expect(wrapper.attributes('disabled')).toBeDefined()
  })

  it('stays focusable and stops clicks when it is only announced as disabled', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppButton, {
      props: { ariaDisabled: true },
      slots: { default: 'Assign technician' },
      attrs: { onClick },
      attachTo: document.body,
    })

    await wrapper.trigger('click')
    ;(wrapper.element as HTMLButtonElement).focus()

    expect(wrapper.attributes('aria-disabled')).toBe('true')
    expect(wrapper.attributes('disabled')).toBeUndefined()
    expect(wrapper.classes()).not.toContain('app-button--busy')
    expect(document.activeElement).toBe(wrapper.element)
    expect(onClick).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('takes the announced disabled state from an aria-disabled binding', async () => {
    const onClick = vi.fn()
    const wrapper = mount(AppButton, {
      attrs: { 'aria-disabled': false, onClick },
      slots: { default: 'Assign to Priya Natarajan' },
    })

    await wrapper.trigger('click')

    expect(wrapper.attributes('aria-disabled')).toBeUndefined()
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('renders a link for navigation and never a button inside it', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { render: () => null } },
        { path: '/jobs/new', component: { render: () => null } },
      ],
    })
    const wrapper = mount(AppButton, {
      props: { to: '/jobs/new', icon: 'plus' },
      slots: { default: 'New job' },
      global: { plugins: [router] },
    })

    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.attributes('href')).toBe('/jobs/new')
    expect(wrapper.find('button').exists()).toBe(false)
    await wrapper.trigger('click')
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/jobs/new'))
  })
})

describe('SectionCard', () => {
  it('is a section labelled by its heading', () => {
    const wrapper = mount(SectionCard, {
      props: { heading: 'Needs a technician' },
      slots: { default: () => h('p', 'Body') },
    })
    const heading = wrapper.find('h2')

    expect(wrapper.element.tagName).toBe('SECTION')
    expect(wrapper.attributes('aria-labelledby')).toBe(heading.attributes('id'))
    expect(heading.text()).toBe('Needs a technician')
  })

  it('is a plain container without a heading', () => {
    const wrapper = mount(SectionCard, { props: { variant: 'plain' }, slots: { default: 'Body' } })

    expect(wrapper.element.tagName).toBe('DIV')
    expect(wrapper.attributes('aria-labelledby')).toBeUndefined()
  })

  it('puts a muted line under the heading without changing the section name', () => {
    const wrapper = mount(SectionCard, {
      props: { heading: 'Assign technician', subheading: 'Electrical technicians first' },
      slots: { default: 'Body' },
    })
    const heading = wrapper.find('h2')

    expect(wrapper.attributes('aria-labelledby')).toBe(heading.attributes('id'))
    expect(wrapper.find('header .section-card__titles').text()).toBe(
      'Assign technicianElectrical technicians first',
    )
    expect(wrapper.find('.section-card__subheading').element.tagName).toBe('P')
    expect(declarationsOf('SectionCard.vue', '.section-card__subheading')).toMatchObject({
      color: 'var(--color-fs-muted)',
      'font-size': 'var(--text-fs-meta)',
    })
  })

  it('keeps a plain heading out of focus unless the card asks for a focus target', () => {
    const wrapper = mount(SectionCard, {
      props: { heading: 'Job' },
      slots: { default: 'Body' },
      attachTo: document.body,
    })
    const heading = wrapper.find<HTMLHeadingElement>('h2')

    expect(heading.attributes('tabindex')).toBeUndefined()
    ;(wrapper.vm as unknown as { focusHeading(): void }).focusHeading()
    expect(document.activeElement).not.toBe(heading.element)
    wrapper.unmount()
  })

  it.each([
    ['without', null],
    ['with', 'Electrical technicians first'],
  ])(
    'offers its heading as a focus target outside the Tab order, %s a subheading',
    (_name, subheading) => {
      const wrapper = mount(SectionCard, {
        props: { heading: 'Checklist and assignment', subheading, focusableHeading: true },
        slots: { default: 'Body' },
        attachTo: document.body,
      })
      const heading = wrapper.find<HTMLHeadingElement>('h2')

      expect(heading.attributes('tabindex')).toBe('-1')
      expect(heading.element.tabIndex).toBe(-1)
      ;(wrapper.vm as unknown as { focusHeading(): void }).focusHeading()
      expect(document.activeElement).toBe(heading.element)
      expect(wrapper.attributes('aria-labelledby')).toBe(heading.attributes('id'))
      expect(declarationsOf('SectionCard.vue', '.section-card__title:focus')).toEqual({})
      expect(ruleDeclarations('app/styles/base.css', ':focus-visible')).toMatchObject({
        outline: '2px solid var(--color-fs-focus)',
      })
      wrapper.unmount()
    },
  )
})

function declarationsOf(
  component: string,
  selector: string,
  within?: string,
): Record<string, string> {
  return ruleDeclarations(`shared/ui/${component}`, selector, within)
}

describe('reflow at 320 pixels', () => {
  it('lets a text input shrink below its intrinsic width', () => {
    expect(declarationsOf('TextField.vue', '.text-field__input')).toMatchObject({
      width: '100%',
      'min-width': '0',
    })
  })

  it('wraps a correlation reference inside its banner when a line is too short', () => {
    const reference = declarationsOf('Banner.vue', '.banner__reference-id')

    expect(reference).toMatchObject({
      display: 'inline-block',
      'max-width': '100%',
      'overflow-wrap': 'anywhere',
    })
    expect(reference['white-space']).toBeUndefined()
  })

  it('lets the skeleton page title shrink to the page instead of widening it', () => {
    expect(declarationsOf('PageHeader.vue', '.page-header--loading .page-header__title')).toEqual({
      'align-self': 'stretch',
    })
    expect(declarationsOf('LoadingState.vue', '.loading-state__block')).toMatchObject({
      'max-width': '100%',
    })
  })

  it('wraps the four priority segments two by two on a phone', () => {
    expect(declarationsOf('SegmentedControl.vue', '.segmented-control__track')).toMatchObject({
      'flex-wrap': 'wrap',
    })
    expect(declarationsOf('SegmentedControl.vue', '.segmented-control__segment')).toMatchObject({
      flex: '1 1 6.5rem',
    })
  })

  it('pads the sign-in card from 24 pixels on phones up to 40 pixels', () => {
    expect(declarationsOf('SectionCard.vue', '.section-card--plain')).toMatchObject({
      padding: 'clamp(var(--spacing-fs-24), 7vw, var(--spacing-fs-40))',
    })
  })
})

describe('the shell on a window wide enough for the sidebar', () => {
  it('keeps the sidebar in the window while the page scrolls', () => {
    expect(declarationsOf('AppShell.vue', '.app-shell')).toMatchObject({
      'container-type': 'inline-size',
    })
    expect(
      declarationsOf('AppShell.vue', '.app-shell__sidebar', '@container (width >= 808px)'),
    ).toEqual({
      position: 'sticky',
      top: '0',
      'align-self': 'flex-start',
      height: '100dvh',
      'overflow-y': 'auto',
    })
  })
})

const emptyPage = { render: () => null }

const shellRoutes: RouteRecordRaw[] = [
  { path: '/', redirect: '/jobs' },
  {
    path: '/jobs',
    name: 'jobs',
    component: emptyPage,
    meta: { title: 'Jobs', section: 'jobs' },
  },
  {
    path: '/jobs/new',
    name: 'job-create',
    component: emptyPage,
    meta: {
      title: 'New job',
      section: 'jobs',
      breadcrumbs: [{ label: 'Jobs', to: { name: 'jobs' } }],
      newJobAction: false,
    },
  },
  {
    path: '/jobs/:jobId',
    name: 'job-detail',
    component: emptyPage,
    meta: {
      title: 'Job details',
      section: 'jobs',
      breadcrumbs: [{ label: 'Jobs', to: { name: 'jobs' } }],
    },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: emptyPage,
    meta: { title: 'Page not found' },
  },
]

async function shellAt(path: string, routes: RouteRecordRaw[] = shellRoutes) {
  const router: Router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(path)
  const wrapper = mount(AppShell, {
    props: { userName: 'Dana Whitfield', userRole: 'Dispatcher' },
    slots: { default: () => h('h1', 'Page') },
    global: { plugins: [router] },
    attachTo: document.body,
  })
  return { wrapper, router }
}

describe('AppShell', () => {
  let unmount: (() => void) | null = null

  afterEach(() => {
    unmount?.()
    unmount = null
  })

  it('starts with a skip link to main, which takes focus', async () => {
    const { wrapper } = await shellAt('/jobs')
    unmount = () => wrapper.unmount()
    const skip = wrapper.find('a')

    expect(skip.text()).toBe('Skip to main content')
    expect(skip.attributes('href')).toBe('#main')
    await skip.trigger('click')

    const main = wrapper.find('main')
    expect(main.attributes()).toMatchObject({ id: 'main', tabindex: '-1' })
    expect(document.activeElement).toBe(main.element)
  })

  it('opens the main navigation with the brand lockup and marks Jobs as the current page', async () => {
    const { wrapper } = await shellAt('/jobs/new')
    unmount = () => wrapper.unmount()
    const navigation = wrapper.find('nav[aria-label="Main"]')
    const links = navigation.findAll('a')

    expect(links[0]?.text()).toBe('FieldService Admin')
    expect(links[0]?.find('.app-shell__mark').attributes('aria-hidden')).toBe('true')
    expect(links.map((link) => link.text())).toEqual(['FieldService Admin', 'Jobs'])
    expect(links[1]?.attributes('aria-current')).toBe('page')
  })

  it('lists only the destinations the route table holds', async () => {
    const withTechnicians = [
      ...shellRoutes.slice(0, -1),
      {
        path: '/technicians',
        name: 'technicians',
        component: emptyPage,
        meta: { section: 'technicians' as const },
      },
      {
        path: '/dashboard',
        name: 'dashboard',
        component: emptyPage,
        meta: { section: 'dashboard' as const },
      },
    ]
    const { wrapper } = await shellAt('/technicians', withTechnicians)
    unmount = () => wrapper.unmount()
    const links = wrapper.findAll('.app-shell__link')

    expect(links.map((link) => link.text())).toEqual(['Dashboard', 'Jobs', 'Technicians'])
    expect(links.map((link) => link.attributes('aria-current'))).toEqual([
      undefined,
      undefined,
      'page',
    ])
  })

  it('shows the breadcrumb trail with the current page marked and the separators hidden', async () => {
    const { wrapper } = await shellAt('/jobs/new')
    unmount = () => wrapper.unmount()
    const breadcrumb = wrapper.find('nav[aria-label="Breadcrumb"]')

    expect(breadcrumb.findAll('li').map((item) => item.text())).toEqual(['Jobs', 'New job'])
    expect(breadcrumb.find('a').attributes('href')).toBe('/jobs')
    expect(breadcrumb.find('[aria-current="page"]').text()).toBe('New job')
    expect(breadcrumb.find('svg').attributes('aria-hidden')).toBe('true')
  })

  it('names the current crumb after the page when the page gives its own title', async () => {
    const DetailPage = defineComponent({
      setup() {
        usePageTitle(() => 'JOB-1051')
        return () => h('h1', 'Replace faulty breaker panel')
      },
    })
    const router = createRouter({ history: createMemoryHistory(), routes: shellRoutes })
    await router.push('/jobs/01a15488-9680-7989-92af-ed07fc34a890')
    const wrapper = mount(AppShell, {
      props: { userName: 'Dana Whitfield', userRole: 'Dispatcher' },
      slots: { default: () => h(DetailPage) },
      global: { plugins: [router] },
    })
    unmount = () => wrapper.unmount()
    await nextTick()

    const current = wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]')
    expect(current.text()).toBe('JOB-1051')
    expect(current.classes()).toContain('app-shell__crumb-current--mono')
    expect(document.title).toBe('JOB-1051 · FieldService Admin')
  })

  it.each([
    ['while the job loads', null, 'Job details'],
    ['for a missing job', 'Job not found', 'Job not found'],
  ])(
    'keeps the current crumb in the sans face %s, because it is not a job number',
    async (_, pageTitle, crumb) => {
      const DetailPage = defineComponent({
        setup() {
          usePageTitle(() => pageTitle)
          return () => h('h1', crumb)
        },
      })
      const router = createRouter({ history: createMemoryHistory(), routes: shellRoutes })
      await router.push('/jobs/01a15488-9680-7989-92af-ed07fc34a890')
      const wrapper = mount(AppShell, {
        props: { userName: 'Dana Whitfield', userRole: 'Dispatcher' },
        slots: { default: () => h(DetailPage) },
        global: { plugins: [router] },
      })
      unmount = () => wrapper.unmount()
      await nextTick()

      const current = wrapper.find('nav[aria-label="Breadcrumb"] [aria-current="page"]')
      expect(current.text()).toBe(crumb)
      expect(current.classes()).not.toContain('app-shell__crumb-current--mono')
    },
  )

  it('offers New job everywhere except on the new job page', async () => {
    const jobs = await shellAt('/jobs')
    const newJob = jobs.wrapper.findAll('header a').find((link) => link.text() === 'New job')
    expect(newJob?.attributes('href')).toBe('/jobs/new')
    jobs.wrapper.unmount()

    const create = await shellAt('/jobs/new')
    unmount = () => create.wrapper.unmount()
    expect(create.wrapper.findAll('a').some((link) => link.text() === 'New job')).toBe(false)
  })

  it('shows the signed-in user and a named Sign out button that asks to sign out', async () => {
    const { wrapper } = await shellAt('/jobs')
    unmount = () => wrapper.unmount()
    const account = wrapper.find('.app-shell__account')
    const signOut = account.find('button')

    expect(account.find('.avatar').text()).toBe('DW')
    expect(account.find('.avatar').attributes('aria-hidden')).toBe('true')
    expect(account.text()).toContain('Dana Whitfield')
    expect(account.text()).toContain('Dispatcher')
    expect(signOut.attributes('aria-label')).toBe('Sign out')
    await signOut.trigger('click')
    expect(wrapper.emitted('sign-out')).toHaveLength(1)
  })

  it('holds the notification region inside main', async () => {
    const { wrapper } = await shellAt('/jobs')
    unmount = () => wrapper.unmount()

    expect(wrapper.find('main [role="region"][aria-label="Notifications"]').exists()).toBe(true)
  })
})

describe('IconButton', () => {
  it('is a plain button named by its label with a hidden icon', () => {
    const wrapper = mount(IconButton, { props: { label: 'Dismiss notification', icon: 'x' } })

    expect(wrapper.attributes()).toMatchObject({
      type: 'button',
      'aria-label': 'Dismiss notification',
    })
    expect(wrapper.find('svg').attributes('aria-hidden')).toBe('true')
  })

  it('stops clicks while busy without leaving the tab order', async () => {
    const onClick = vi.fn()
    const wrapper = mount(IconButton, {
      props: { label: 'Sign out', icon: 'log-out', busy: true },
      attrs: { onClick },
    })

    await wrapper.trigger('click')

    expect(wrapper.attributes('aria-disabled')).toBe('true')
    expect(wrapper.attributes('disabled')).toBeUndefined()
    expect(onClick).not.toHaveBeenCalled()
  })
})

describe('FilterChipGroup', () => {
  const options = [
    { value: 'all', label: 'All', count: 20 },
    {
      value: 'OnHold',
      label: 'On hold',
      icon: 'pause-circle' as const,
      tone: 'on-hold' as const,
      count: 3,
    },
  ]

  it('is a labelled group of toggle buttons with the selection in aria-pressed', () => {
    const wrapper = mount(FilterChipGroup, {
      props: { label: 'Filter by status', options, modelValue: 'all' },
    })
    const chips = wrapper.findAll('button')

    expect(wrapper.attributes()).toMatchObject({ role: 'group', 'aria-label': 'Filter by status' })
    expect(chips.map((chip) => chip.attributes('aria-pressed'))).toEqual(['true', 'false'])
    expect(chips[1]?.find('svg').attributes('aria-hidden')).toBe('true')
  })

  it('names a counted chip "<label>, <count>" from the values it shows, label first', () => {
    const wrapper = mount(FilterChipGroup, {
      props: {
        label: 'Filter by shift',
        options: [...options, { value: 'none', label: 'No count' }],
        modelValue: 'all',
      },
    })
    const chips = wrapper.findAll('button')

    expect(chips.map((chip) => chip.attributes('aria-label'))).toEqual([
      'All, 20',
      'On hold, 3',
      undefined,
    ])
    expect(chips.map((chip) => chip.text())).toEqual(['All20', 'On hold3', 'No count'])
    expect(wrapper.find('.visually-hidden').exists()).toBe(false)
  })

  it('selects one chip at a time and ignores a click on the selected chip', async () => {
    const wrapper = mount(FilterChipGroup, {
      props: { label: 'Filter by status', options, modelValue: 'all' },
    })

    await wrapper.findAll('button')[0]?.trigger('click')
    await wrapper.findAll('button')[1]?.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['OnHold']])
  })
})

describe('SegmentedControl', () => {
  const options = [
    { value: 'Low', label: 'Low', icon: 'chevron-down' as const },
    { value: 'Normal', label: 'Normal', icon: 'minus' as const },
  ]

  it('is a fieldset with a legend and native radios inside their labels', async () => {
    const wrapper = mount(SegmentedControl, {
      props: {
        legend: 'Priority',
        id: 'priority',
        name: 'priority',
        options,
        modelValue: 'Normal',
      },
    })
    const radios = wrapper.findAll('input[type="radio"]')

    expect(wrapper.element.tagName).toBe('FIELDSET')
    expect(wrapper.find('legend').text()).toBe('Priority')
    expect(radios.map((radio) => radio.attributes('id'))).toEqual([
      'priority-low',
      'priority-normal',
    ])
    expect(wrapper.findAll('label').map((label) => label.attributes('for'))).toEqual([
      'priority-low',
      'priority-normal',
    ])
    expect((radios[1]?.element as HTMLInputElement).checked).toBe(true)

    await radios[0]?.setValue(true)
    expect(wrapper.emitted('update:modelValue')).toEqual([['Low']])
  })

  it('ties an error to the group', () => {
    const wrapper = mount(SegmentedControl, {
      props: { legend: 'Priority', id: 'priority', options, error: 'Choose a priority.' },
    })

    expect(wrapper.attributes('aria-describedby')).toBe('priority-error')
    expect(wrapper.find('#priority-error').text()).toBe('Choose a priority.')
  })

  it('names a counted segment "<label>, <count>" and shows the count in a capsule', () => {
    const wrapper = mount(SegmentedControl, {
      props: {
        legend: 'Show jobs',
        id: 'view',
        options: [
          { value: 'active', label: 'Active', count: 3 },
          { value: 'completed', label: 'Completed' },
        ],
        modelValue: 'active',
      },
    })
    const radios = wrapper.findAll('input[type="radio"]')
    const capsules = wrapper.findAll('.count-capsule')

    expect(radios.map((radio) => radio.attributes('aria-label'))).toEqual(['Active, 3', undefined])
    expect(capsules.map((capsule) => capsule.text())).toEqual(['3'])
    expect(capsules[0]?.classes()).toContain('count-capsule--in-segment')
    expect(wrapper.findAll('label').map((label) => label.text())).toEqual(['Active 3', 'Completed'])
  })
})

describe('SelectField', () => {
  const options = [
    { value: 'a', label: 'Boiler service' },
    { value: 'b', label: 'Rooftop HVAC service' },
  ]

  it('ties the select to its label and offers a placeholder that cannot be chosen', () => {
    const wrapper = mount(SelectField, {
      props: {
        label: 'Checklist template',
        id: 'checklist-template',
        options,
        placeholder: 'Choose a checklist template',
      },
    })
    const placeholder = wrapper.find('option')

    expect(wrapper.find('label').attributes('for')).toBe('checklist-template')
    expect(wrapper.find('select').attributes('id')).toBe('checklist-template')
    expect(placeholder.text()).toBe('Choose a checklist template')
    expect(placeholder.attributes('disabled')).toBeDefined()
    expect(wrapper.find('.select-field__chevron').attributes('aria-hidden')).toBe('true')
  })

  it('can be named without a visible label, as in a toolbar', () => {
    const wrapper = mount(SelectField, { props: { label: 'Priority', options, hiddenLabel: true } })

    expect(wrapper.find('label').exists()).toBe(false)
    expect(wrapper.find('select').attributes('aria-label')).toBe('Priority')
  })

  it('describes the select with its error, then its help', () => {
    const wrapper = mount(SelectField, {
      props: {
        label: 'Technician',
        id: 'technician',
        options,
        error: 'Choose a technician.',
        help: 'The technician sees the job at once.',
      },
    })
    const select = wrapper.find('select')

    expect(select.attributes('aria-invalid')).toBe('true')
    expect(select.attributes('aria-describedby')).toBe('technician-error technician-help')
  })

  it('places options in labelled groups, keeping disabled options visible', () => {
    const wrapper = mount(SelectField, {
      props: {
        label: 'Assign technician',
        id: 'assign-technician',
        options: [
          { value: '', label: 'Leave unassigned' },
          { label: 'Electrical', options: [{ value: 'p', label: 'Priya Natarajan' }] },
          {
            label: 'Different trade',
            options: [{ value: 's', label: 'Samuel Osei', disabled: true }],
          },
        ],
        modelValue: '',
      },
    })
    const groups = wrapper.findAll('optgroup')

    expect(groups.map((group) => group.attributes('label'))).toEqual([
      'Electrical',
      'Different trade',
    ])
    expect(wrapper.find('select > option').text()).toBe('Leave unassigned')
    expect(groups[1]?.find('option').attributes('disabled')).toBeDefined()
    expect(wrapper.findAll('option').map((option) => option.text())).toEqual([
      'Leave unassigned',
      'Priya Natarajan',
      'Samuel Osei',
    ])
  })
})

describe('TextArea', () => {
  it('ties the text area to its label, error and length limit', () => {
    const wrapper = mount(TextArea, {
      props: {
        label: 'Description',
        id: 'job-description',
        maxlength: 2000,
        error: 'Describe the work to be done.',
      },
    })
    const area = wrapper.find('textarea')

    expect(wrapper.find('label').attributes('for')).toBe('job-description')
    expect(area.attributes()).toMatchObject({
      id: 'job-description',
      maxlength: '2000',
      'aria-invalid': 'true',
      'aria-describedby': 'job-description-error',
    })
  })
})

describe('TextField pickers and patterns', () => {
  it('uses the native date and time pickers with a hidden trailing icon', () => {
    const wrapper = mount(TextField, {
      props: { label: 'Date', type: 'date', trailingIcon: 'calendar' },
    })

    expect(wrapper.find('input').attributes('type')).toBe('date')
    expect(wrapper.find('.text-field__trailing').attributes('aria-hidden')).toBe('true')
  })

  it('passes a pattern to the input', () => {
    const wrapper = mount(TextField, {
      props: { label: 'Contact phone', type: 'tel', pattern: '[0-9]{3,31}' },
    })

    expect(wrapper.find('input').attributes('pattern')).toBe('[0-9]{3,31}')
  })
})

describe('PageHeader', () => {
  it('holds the one page heading and moves focus to it when it appears', () => {
    const wrapper = mount(PageHeader, {
      props: { title: 'Jobs', subline: '20 jobs' },
      attachTo: document.body,
    })
    const heading = wrapper.find('h1')

    expect(heading.text()).toBe('Jobs')
    expect(heading.attributes('tabindex')).toBe('-1')
    expect(document.activeElement).toBe(heading.element)
    expect(wrapper.find('.page-header__subline').text()).toBe('20 jobs')
    wrapper.unmount()
  })

  it('puts the job number above the title on detail pages', () => {
    const wrapper = mount(PageHeader, {
      props: { title: 'Replace faulty breaker panel', jobNumber: 'JOB-1051', focusOnMount: false },
    })

    expect(wrapper.find('.page-header__number').text()).toBe('JOB-1051')
    expect(wrapper.find('h1').text()).toBe('Replace faulty breaker panel')
  })

  it('keeps a named heading while its skeleton title is shown', () => {
    const wrapper = mount(PageHeader, {
      props: { title: 'Job details', loading: true, focusOnMount: false },
    })
    const heading = wrapper.find('h1')

    expect(heading.text()).toBe('Job details')
    expect(heading.find('.loading-state').attributes('aria-hidden')).toBe('true')
  })
})

describe('PriorityLabel', () => {
  it.each([
    ['Low', 'chevron-down'],
    ['Normal', 'minus'],
    ['High', 'chevron-up'],
  ])('shows %s as a hidden glyph and the word', (priority) => {
    const wrapper = mount(PriorityLabel, { props: { priority } })

    expect(wrapper.text()).toBe(priority)
    expect(wrapper.find('svg').attributes('aria-hidden')).toBe('true')
    expect(wrapper.classes()).toContain(`priority-label--${priority.toLowerCase()}`)
  })

  it('shows Urgent as the badge, with sentence-case source text', () => {
    const wrapper = mount(PriorityLabel, { props: { priority: 'Urgent' } })

    expect(wrapper.text()).toBe('Urgent')
    expect(wrapper.classes()).toContain('priority-label--urgent')
    expect(wrapper.find('svg').attributes('stroke-width')).toBe('2.5')
  })

  it('shows an unknown priority as it arrived, without a glyph', () => {
    const wrapper = mount(PriorityLabel, { props: { priority: 'Critical' } })

    expect(wrapper.text()).toBe('Critical')
    expect(wrapper.find('svg').exists()).toBe(false)
  })
})

describe('small decorations', () => {
  it('keeps avatars and icon tiles out of the accessibility tree', () => {
    expect(mount(Avatar, { props: { name: 'Priya Natarajan' } }).attributes('aria-hidden')).toBe(
      'true',
    )
    expect(mount(Avatar, { props: { name: 'Priya Natarajan' } }).text()).toBe('PN')
    expect(mount(IconTile, { props: { icon: 'search', size: 56 } }).attributes('aria-hidden')).toBe(
      'true',
    )
  })

  it('reads a count capsule as plain text and the New tag as New', () => {
    expect(mount(CountCapsule, { props: { count: 4 } }).text()).toBe('4')
    expect(mount(CountCapsule, { props: { variant: 'new' } }).text()).toBe('New')
  })
})

describe('page states', () => {
  it('gives an empty state a heading at the level its context needs', () => {
    const wrapper = mount(EmptyState, {
      props: {
        title: 'No jobs match these filters',
        body: 'Change the search or filters.',
        headingLevel: 3,
      },
      slots: { actions: () => h('button', { type: 'button' }, 'Clear filters') },
    })

    expect(wrapper.find('h3').text()).toBe('No jobs match these filters')
    expect(wrapper.find('.icon-tile').attributes('aria-hidden')).toBe('true')
    expect(wrapper.find('button').text()).toBe('Clear filters')
  })

  it('shows a load failure as the error banner with its reference and a Try again button', async () => {
    const wrapper = mount(ErrorState, {
      props: {
        title: 'Jobs could not be loaded.',
        message: 'Check your connection, then try again.',
        reference: 'dd85f06c-7606-4dfc-a702-fa39647047d8',
      },
    })

    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.find('.banner__reference').text()).toBe(
      'Reference dd85f06c-7606-4dfc-a702-fa39647047d8',
    )
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('announces loading once with a visually hidden status and hides the skeleton', () => {
    const wrapper = mount(LoadingState, {
      props: { label: 'Loading jobs', shapes: ['line', 'line-short'] },
    })

    expect(wrapper.find('[role="status"]').text()).toBe('Loading jobs')
    expect(wrapper.find('[role="status"]').classes()).toContain('visually-hidden')
    expect(wrapper.find('.loading-state').attributes('aria-hidden')).toBe('true')
    expect(wrapper.findAll('.loading-state__block')).toHaveLength(2)
  })
})
