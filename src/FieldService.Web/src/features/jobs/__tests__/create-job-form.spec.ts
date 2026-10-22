import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, type Router } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import {
  aishaTechnician,
  checklistTemplates,
  demoTechnicians,
  demoToday,
  historyEntry,
  jobDetails,
  priyaTechnician,
  samuelTechnician,
  technician,
} from './job-fixtures'

const templatesRoute = 'GET /api/checklist-templates'

const techniciansRoute = 'GET /api/technicians'

const rooftopHvac = '01a15175-3c00-7000-8000-000102000000'

const createRoute = 'POST /api/jobs'

const reference = 'b2b49504-1cd6-4228-9a7b-85f907ad5a91'

const generalElectrical = '01a15175-3c00-7000-8000-000101000000'

interface OpenForm {
  readonly wrapper: VueWrapper
  readonly router: Router
  readonly stub: FetchStub
}

let mounted: VueWrapper | null = null

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(demoToday)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
})

async function openForm(routeTable: RouteTable = {}): Promise<OpenForm> {
  const stub = stubFetch({
    'POST /api/auth/refresh': json(sessionResponse()),
    [templatesRoute]: json({ items: checklistTemplates }),
    [techniciansRoute]: json({ items: demoTechnicians }),
    ...routeTable,
  })
  const router = createAppRouter(createMemoryHistory())
  installNavigationGuards(router)
  await router.push('/jobs/new')
  const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()
  return { wrapper, router, stub }
}

function field(wrapper: VueWrapper, id: string): DOMWrapper<HTMLInputElement> {
  return wrapper.find<HTMLInputElement>(`#${id}`)
}

async function fillValidJob(wrapper: VueWrapper): Promise<void> {
  await field(wrapper, 'job-title').setValue('Exterior signage lights out')
  await wrapper
    .find('#job-description')
    .setValue(
      'Both pylon signs at the north entrance are dark after 6 PM. Timer and photocell suspected.',
    )
  await field(wrapper, 'customer').setValue('Riverside Shopping Center')
  await field(wrapper, 'site-address').setValue('500 Riverside Boulevard')
  await field(wrapper, 'site-access-notes').setValue('Meet security at the north office')
  await field(wrapper, 'contact-name').setValue('Grace Liu')
  await field(wrapper, 'contact-phone').setValue('(555) 014-8812')
  await field(wrapper, 'job-date').setValue('2026-10-20')
  await field(wrapper, 'job-start').setValue('09:00')
  await field(wrapper, 'job-end').setValue('11:00')
  await wrapper.find('#checklist-template').setValue(generalElectrical)
}

async function submit(wrapper: VueWrapper): Promise<void> {
  await wrapper.find('form.create-job-form').trigger('submit')
  await flushPromises()
}

function technicianSelect(wrapper: VueWrapper): HTMLSelectElement {
  return wrapper.find<HTMLSelectElement>('#assign-technician').element
}

interface ListedOption {
  readonly group: string | null
  readonly text: string
  readonly value: string
  readonly disabled: boolean
}

function listedOptions(select: HTMLSelectElement): ListedOption[] {
  return Array.from(select.options, (option) => ({
    group: option.parentElement instanceof HTMLOptGroupElement ? option.parentElement.label : null,
    text: option.text.trim(),
    value: option.value,
    disabled: option.disabled,
  }))
}

function errorOf(wrapper: VueWrapper, id: string): string | null {
  const control = wrapper.find(`#${id}`)
  if (control.attributes('aria-invalid') !== 'true') {
    return null
  }
  const describedBy = control.attributes('aria-describedby')?.split(' ')[0] ?? ''
  return wrapper.find(`#${describedBy}`).text()
}

describe('the new job form', () => {
  it('mirrors the server limits with native constraints', async () => {
    const { wrapper } = await openForm()

    expect(
      [
        'job-title',
        'job-description',
        'equipment',
        'customer',
        'site-address',
        'site-access-notes',
      ].map((id) => wrapper.find(`#${id}`).attributes('maxlength')),
    ).toEqual(['120', '2000', '200', '120', '300', '500'])
    expect(field(wrapper, 'contact-name').attributes('maxlength')).toBe('100')
    expect(field(wrapper, 'contact-phone').attributes()).toMatchObject({
      type: 'tel',
      maxlength: '32',
    })
    expect(
      new RegExp(`^(?:${field(wrapper, 'contact-phone').attributes('pattern')})$`, 'v').test(
        '(555) 014-8812',
      ),
    ).toBe(true)
    expect(
      [
        'job-title',
        'job-description',
        'customer',
        'site-address',
        'job-date',
        'job-start',
        'job-end',
      ].map((id) => wrapper.find(`#${id}`).attributes('required')),
    ).toEqual(Array(7).fill(''))
    expect(wrapper.find('#checklist-template').attributes('required')).toBeDefined()
    expect(field(wrapper, 'site-access-notes').attributes('required')).toBeUndefined()
    expect(field(wrapper, 'job-date').attributes('type')).toBe('date')
    expect(field(wrapper, 'job-start').attributes('type')).toBe('time')
    expect(wrapper.find('form').attributes('novalidate')).toBeDefined()
  })

  it('lays out the four cards with labelled fields, optional markers and the technician select', async () => {
    const { wrapper } = await openForm()
    const lastCard = wrapper.find('section[aria-labelledby="checklist-section-title"]')

    expect(wrapper.find('form').attributes('aria-labelledby')).toBe('page-title')
    expect(wrapper.find('#page-title').text()).toBe('New job')
    expect(wrapper.findAll('form h2').map((heading) => heading.text())).toEqual([
      'Job',
      'Customer and site',
      'Schedule and priority',
      'Checklist and assignment',
    ])
    expect(wrapper.find('label[for="site-access-notes"]').text()).toBe('Site access notes Optional')
    expect(wrapper.find('label[for="contact-phone"]').text()).toBe('Contact phone Optional')
    expect(wrapper.find('label[for="site-address"]').text()).toBe('Site address')
    expect(lastCard.findAll('select').map((select) => select.attributes('id'))).toEqual([
      'checklist-template',
      'assign-technician',
    ])
    expect(wrapper.find('fieldset legend').text()).toBe('Priority')
    expect(
      wrapper
        .findAll('fieldset input[type="radio"]')
        .map((radio) => [radio.attributes('id'), (radio.element as HTMLInputElement).checked]),
    ).toEqual([
      ['priority-low', false],
      ['priority-normal', true],
      ['priority-high', false],
      ['priority-urgent', false],
    ])
    expect(wrapper.find('form a[href="/jobs"]').text()).toBe('Cancel')
    expect(wrapper.findAll('header a').some((link) => link.text() === 'New job')).toBe(false)
  })

  it('offers an optional Equipment field after Description in the Job card', async () => {
    const { wrapper } = await openForm()
    const jobCard = wrapper.find('section[aria-labelledby="job-section-title"]')
    const equipment = field(wrapper, 'equipment')

    expect(jobCard.findAll('input, textarea').map((control) => control.attributes('id'))).toEqual([
      'job-title',
      'job-description',
      'equipment',
    ])
    expect(wrapper.find('label[for="equipment"]').text()).toBe('Equipment Optional')
    expect(equipment.attributes()).toMatchObject({
      type: 'text',
      name: 'equipment',
      maxlength: '200',
      autocomplete: 'off',
      placeholder: 'Unit, model or tag',
      'aria-describedby': 'equipment-help',
    })
    expect(equipment.attributes('required')).toBeUndefined()
    expect(wrapper.find('#equipment-help').text()).toBe(
      'Name the unit or system to work on. The technician sees it on the job details.',
    )
  })

  it('sends the equipment trimmed, and leaves it out when it is blank', async () => {
    const { wrapper, stub } = await openForm({
      [createRoute]: [problem(500, 'server.error'), problem(500, 'server.error')],
    })
    await fillValidJob(wrapper)

    await field(wrapper, 'equipment').setValue('  RTU-2, 10-ton packaged rooftop unit  ')
    await submit(wrapper)
    await field(wrapper, 'equipment').setValue('   ')
    await submit(wrapper)

    const bodies = stub
      .requestsTo(createRoute)
      .map((request) => JSON.parse(request.body ?? '{}') as Record<string, unknown>)
    expect(bodies[0]?.equipment).toBe('RTU-2, 10-ton packaged rooftop unit')
    expect(bodies[1]).not.toHaveProperty('equipment')
  })

  it('shows a server error about the equipment on its field and names it in the summary', async () => {
    const { wrapper } = await openForm({
      [createRoute]: problem(400, 'request.validation-failed', {
        errors: {
          equipment: ['Use 200 characters or fewer.'],
          siteAddress: ['Enter the site address.'],
        },
      }),
    })
    await fillValidJob(wrapper)
    await field(wrapper, 'equipment').setValue('RTU-2')

    await submit(wrapper)

    expect(field(wrapper, 'equipment').attributes()).toMatchObject({
      'aria-invalid': 'true',
      'aria-describedby': 'equipment-error equipment-help',
    })
    expect(errorOf(wrapper, 'equipment')).toBe('Use 200 characters or fewer.')
    expect(wrapper.find('.create-job-form > .banner--error').text()).toBe(
      'Some details need attention. Check Equipment and Site address.',
    )
    expect(document.activeElement?.id).toBe('equipment')
  })

  it('lists the checklist templates in the order the API gives them', async () => {
    const { wrapper } = await openForm()
    const options = wrapper.findAll('#checklist-template option')

    expect(options.map((option) => option.text())).toEqual([
      'Choose a checklist template',
      'Boiler service',
      'Electrical panel inspection',
      'Fire extinguisher inspection',
      'General electrical inspection',
      'Rooftop HVAC service',
    ])
    expect(wrapper.find('#checklist-template').attributes('disabled')).toBeUndefined()
  })

  it('keeps the template select disabled while the templates load', async () => {
    const slow = deferred()
    const { wrapper } = await openForm({ [templatesRoute]: slow.reply })

    expect(wrapper.find('#checklist-template').attributes('disabled')).toBeDefined()
    expect(wrapper.find('#checklist-template option').text()).toBe('Loading checklist templates…')

    slow.resolve(new Response(JSON.stringify({ items: checklistTemplates }), { status: 200 }))
    await flushPromises()
    expect(wrapper.find('#checklist-template').attributes('disabled')).toBeUndefined()
  })

  it('shows a template load failure with its reference, and Try again reloads', async () => {
    const { wrapper, stub } = await openForm({
      [templatesRoute]: [
        problem(500, 'server.error', { correlationId: reference }),
        json({ items: checklistTemplates }),
      ],
    })
    const banner = wrapper.find('.banner--error')

    expect(banner.text()).toContain('Checklist templates could not be loaded.')
    expect(banner.text()).toContain(`Reference ${reference}`)

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(stub.requestsTo(templatesRoute)).toHaveLength(2)
    expect(wrapper.findAll('#checklist-template option')).toHaveLength(6)
  })

  it('reports a template load failure as a status, and Try again moves focus to the template select', async () => {
    const { wrapper } = await openForm({
      [templatesRoute]: [problem(500, 'server.error'), json({ items: checklistTemplates })],
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    const banner = card.find('.banner--error')
    expect(banner.attributes('role')).toBe('status')
    banner.find('button').element.focus()

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(card.find('.banner--error').exists()).toBe(false)
    expect(document.activeElement?.id).toBe('checklist-template')
    expect(document.activeElement).not.toBe(document.body)
  })

  it('moves focus to the card heading when Try again loads an empty template list', async () => {
    const { wrapper } = await openForm({
      [templatesRoute]: [problem(500, 'server.error'), json({ items: [] })],
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    card.find<HTMLButtonElement>('.banner--error button').element.focus()

    await card.find('.banner--error button').trigger('click')
    await flushPromises()

    const heading = wrapper.find<HTMLHeadingElement>('#checklist-section-title')
    const select = wrapper.find<HTMLSelectElement>('#checklist-template').element
    expect(card.find('.banner--error').exists()).toBe(false)
    expect(select.disabled).toBe(true)
    expect(select.options[0]?.text.trim()).toBe('No checklist templates are available')
    expect(heading.element.tagName).toBe('H2')
    expect(heading.text()).toBe('Checklist and assignment')
    expect(heading.attributes('tabindex')).toBe('-1')
    expect(document.activeElement).toBe(heading.element)
    expect(
      wrapper
        .findAll('h2')
        .filter((item) => item.attributes('tabindex') !== undefined)
        .map((item) => item.text()),
    ).toEqual(['Checklist and assignment'])
  })

  it('announces a second template failure as an alert and moves focus to the new Try again', async () => {
    const { wrapper } = await openForm({
      [templatesRoute]: [problem(500, 'server.error'), networkFailure()],
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    card.find<HTMLButtonElement>('.banner--error button').element.focus()

    await card.find('.banner--error button').trigger('click')
    await flushPromises()

    const banner = card.find('.banner--error')
    expect(banner.text()).toContain('Checklist templates could not be loaded.')
    expect(banner.attributes('role')).toBe('alert')
    expect(document.activeElement).toBe(banner.find('button').element)
  })

  it('checks required fields before sending and focuses the first one', async () => {
    const { wrapper, stub } = await openForm()

    await submit(wrapper)

    expect(stub.requestsTo(createRoute)).toHaveLength(0)
    expect(errorOf(wrapper, 'job-title')).toBe('Enter a title for the job.')
    expect(errorOf(wrapper, 'job-description')).toBe('Describe the work to be done.')
    expect(errorOf(wrapper, 'customer')).toBe('Enter the customer name.')
    expect(errorOf(wrapper, 'site-address')).toBe(
      'Enter the site address so the technician can find the job.',
    )
    expect(errorOf(wrapper, 'job-date')).toBe('Choose the date of the visit.')
    expect(errorOf(wrapper, 'job-start')).toBe('Choose a start time.')
    expect(errorOf(wrapper, 'job-end')).toBe('Choose an end time.')
    expect(errorOf(wrapper, 'checklist-template')).toBe('Choose a checklist template.')
    expect(errorOf(wrapper, 'site-access-notes')).toBeNull()
    expect(document.activeElement?.id).toBe('job-title')
  })

  it('shows the missing site address on its field, even when only spaces were typed', async () => {
    const { wrapper, stub } = await openForm()
    await fillValidJob(wrapper)
    await field(wrapper, 'site-address').setValue('   ')

    await submit(wrapper)

    expect(stub.requestsTo(createRoute)).toHaveLength(0)
    expect(field(wrapper, 'site-address').attributes()).toMatchObject({
      'aria-invalid': 'true',
      'aria-describedby': 'site-address-error',
    })
    expect(wrapper.find('#site-address-error').text()).toBe(
      'Enter the site address so the technician can find the job.',
    )
    expect(document.activeElement?.id).toBe('site-address')

    await field(wrapper, 'site-address').setValue('500 Riverside Boulevard')
    expect(field(wrapper, 'site-address').attributes('aria-invalid')).toBeUndefined()
  })

  it('refuses an end time that is not after the start', async () => {
    const { wrapper, stub } = await openForm()
    await fillValidJob(wrapper)
    await field(wrapper, 'job-end').setValue('09:00')

    await submit(wrapper)

    expect(stub.requestsTo(createRoute)).toHaveLength(0)
    expect(errorOf(wrapper, 'job-end')).toBe('The end time must be after the start time.')
    expect(document.activeElement?.id).toBe('job-end')
  })

  it('refuses a contact phone the server would refuse', async () => {
    const { wrapper } = await openForm()
    await fillValidJob(wrapper)
    await field(wrapper, 'contact-phone').setValue('555-CALL-NOW')

    await submit(wrapper)

    expect(errorOf(wrapper, 'contact-phone')).toBe(
      'Use digits, spaces, parentheses, dots or hyphens, such as (555) 014-8812.',
    )
  })

  it('posts the job with UTC instants, leaving out empty optional fields and any technician', async () => {
    const created = jobDetails({ id: '01a1549f-79e0-7c56-bc65-86a395b0ccdc', number: 1056 })
    const { wrapper, stub } = await openForm({ [createRoute]: json(created, 201) })
    await fillValidJob(wrapper)
    await field(wrapper, 'site-access-notes').setValue('  ')
    await wrapper.find('#priority-high').setValue(true)

    await submit(wrapper)

    const request = stub.requestsTo(createRoute)[0]
    expect(request?.headers.get('Content-Type')).toContain('application/json')
    expect(JSON.parse(request?.body ?? '{}')).toEqual({
      title: 'Exterior signage lights out',
      description:
        'Both pylon signs at the north entrance are dark after 6 PM. Timer and photocell suspected.',
      priority: 'High',
      customerName: 'Riverside Shopping Center',
      siteAddress: '500 Riverside Boulevard',
      contactName: 'Grace Liu',
      contactPhone: '(555) 014-8812',
      scheduledStart: new Date(2026, 9, 20, 9, 0).toISOString(),
      scheduledEnd: new Date(2026, 9, 20, 11, 0).toISOString(),
      checklistTemplateId: generalElectrical,
    })
  })

  it('shows Creating job… while the request runs and ignores another submit', async () => {
    const slow = deferred()
    const { wrapper, stub } = await openForm({ [createRoute]: slow.reply })
    await fillValidJob(wrapper)

    await submit(wrapper)
    await submit(wrapper)
    const button = wrapper.find('button[type="submit"]')

    expect(button.text()).toBe('Creating job…')
    expect(button.attributes('aria-disabled')).toBe('true')
    expect(stub.requestsTo(createRoute)).toHaveLength(1)

    slow.resolve(new Response(JSON.stringify(jobDetails()), { status: 201 }))
    await flushPromises()
  })

  it('maps server field errors onto their inputs and names them in a summary', async () => {
    const { wrapper } = await openForm({
      [createRoute]: problem(400, 'request.validation-failed', {
        errors: {
          siteAddress: ['Enter the site address.'],
          scheduledEnd: ['The scheduled end must be after the scheduled start.'],
          checklistTemplateId: ['Choose a checklist template that exists.'],
          body: ['The request body could not be read.'],
        },
      }),
    })
    await fillValidJob(wrapper)

    await submit(wrapper)

    expect(field(wrapper, 'site-address').attributes()).toMatchObject({
      'aria-invalid': 'true',
      'aria-describedby': 'site-address-error',
    })
    expect(errorOf(wrapper, 'site-address')).toBe('Enter the site address.')
    expect(field(wrapper, 'job-end').attributes('aria-describedby')).toBe('job-end-error')
    expect(errorOf(wrapper, 'job-end')).toBe('The scheduled end must be after the scheduled start.')
    expect(wrapper.find('#checklist-template').attributes('aria-describedby')).toBe(
      'checklist-template-error',
    )
    expect(errorOf(wrapper, 'checklist-template')).toBe('Choose a checklist template that exists.')
    const summary = wrapper.find('.create-job-form > .banner--error')
    expect(summary.attributes('role')).toBe('alert')
    expect(summary.text()).toBe(
      'Some details need attention. Check Site address, End and Checklist template. The request body could not be read.',
    )
    expect(document.activeElement?.id).toBe('site-address')
  })

  it('explains a failure that is not a validation error, with its reference', async () => {
    const { wrapper } = await openForm({
      [createRoute]: [
        problem(500, 'server.error', { correlationId: reference }),
        networkFailure(),
        problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '42' }),
      ],
    })
    await fillValidJob(wrapper)

    await submit(wrapper)
    const serverFailure = wrapper.find('.create-job-form > .banner--error')
    expect(serverFailure.attributes('role')).toBe('alert')
    expect(serverFailure.text()).toContain('The job could not be created.')
    expect(serverFailure.text()).toContain(`Reference ${reference}`)

    await submit(wrapper)
    expect(wrapper.find('.create-job-form > .banner--error').text()).toBe(
      'FieldService could not be reached. Check your connection, then try again.',
    )

    await submit(wrapper)
    expect(wrapper.find('.create-job-form > .banner--error').text()).toBe(
      'Too many requests. Try again in 42 seconds.',
    )
  })

  it('opens the created job without asking for it again', async () => {
    const created = jobDetails({
      id: '01a1549f-79e0-7c56-bc65-86a395b0ccdc',
      number: 1056,
      title: 'Exterior signage lights out',
      priority: 'Normal',
      customerName: 'Riverside Shopping Center',
      siteAddress: '500 Riverside Boulevard',
      siteAccessNotes: 'Meet security at the north office',
      contactName: 'Grace Liu',
      contactPhone: '(555) 014-8812',
      history: [historyEntry({ id: '01a1549f-79e0-71ae-8e7f-1896175a9d87' })],
    })
    const { wrapper, router, stub } = await openForm({ [createRoute]: json(created, 201) })
    await fillValidJob(wrapper)

    await submit(wrapper)
    await vi.waitFor(() =>
      expect(router.currentRoute.value.fullPath).toBe('/jobs/01a1549f-79e0-7c56-bc65-86a395b0ccdc'),
    )
    await flushPromises()

    expect(wrapper.find('.page-header__number').text()).toBe('JOB-1056')
    expect(wrapper.find('h1').text()).toBe('Exterior signage lights out')
    expect(wrapper.find('.status-chip').text()).toBe('Unassigned')
    expect(wrapper.find('.status-timeline__entry').text()).toBe('10:20 AMCreated by Dana Whitfield')
    expect(
      stub.requests.filter(
        (request) => request.method === 'GET' && request.path.startsWith('/api/jobs'),
      ),
    ).toEqual([])
    expect(document.activeElement).toBe(wrapper.find('h1').element)
  })
})

describe('the technician select on the new job form', () => {
  it('is labelled Assign technician with its help line, and starts at Leave unassigned', async () => {
    const { wrapper } = await openForm()
    const select = technicianSelect(wrapper)

    expect(wrapper.find('label[for="assign-technician"]').text()).toBe('Assign technician')
    expect(select.getAttribute('aria-describedby')).toBe('assign-technician-help')
    expect(wrapper.find('#assign-technician-help').text()).toBe(
      'The technician sees the job in the app as soon as you create it.',
    )
    expect(listedOptions(select)[0]).toEqual({
      group: null,
      text: 'Leave unassigned',
      value: '',
      disabled: false,
    })
    expect(select.value).toBe('')
    expect(select.disabled).toBe(false)
    expect(select.required).toBe(false)
  })

  it('lists the technicians in the assign order, without groups, before a template is chosen', async () => {
    const { wrapper } = await openForm()
    const select = technicianSelect(wrapper)

    expect(select.querySelectorAll('optgroup')).toHaveLength(0)
    expect(listedOptions(select).map(({ text, disabled }) => [text, disabled])).toEqual([
      ['Leave unassigned', false],
      ['Lena Park — Fire safety, 1 open job', false],
      ['Priya Natarajan — Electrical, 2 open jobs', false],
      ['Aisha Karim — Electrical, 3 open jobs', false],
      ['Marco Reyes — HVAC, 3 open jobs', false],
      ['Tom Becker — Plumbing, 4 open jobs', false],
      ['Samuel Osei — HVAC, off shift', true],
    ])
  })

  it('groups the technicians under the template trade, then Different trade', async () => {
    const { wrapper } = await openForm()

    await wrapper.find('#checklist-template').setValue(generalElectrical)
    const select = technicianSelect(wrapper)

    expect(Array.from(select.querySelectorAll('optgroup'), (group) => group.label)).toEqual([
      'Electrical',
      'Different trade',
    ])
    expect(listedOptions(select).map(({ group, text }) => [group, text])).toEqual([
      [null, 'Leave unassigned'],
      ['Electrical', 'Priya Natarajan — Electrical, 2 open jobs'],
      ['Electrical', 'Aisha Karim — Electrical, 3 open jobs'],
      ['Different trade', 'Lena Park — Fire safety, 1 open job'],
      ['Different trade', 'Marco Reyes — HVAC, 3 open jobs'],
      ['Different trade', 'Tom Becker — Plumbing, 4 open jobs'],
      ['Different trade', 'Samuel Osei — HVAC, off shift'],
    ])
  })

  it('re-sorts for another template and keeps the chosen technician', async () => {
    const { wrapper } = await openForm()
    await wrapper.find('#checklist-template').setValue(generalElectrical)
    await wrapper.find('#assign-technician').setValue(aishaTechnician.id)

    await wrapper.find('#checklist-template').setValue(rooftopHvac)
    const select = technicianSelect(wrapper)

    expect(listedOptions(select).map(({ group, text }) => [group, text])).toEqual([
      [null, 'Leave unassigned'],
      ['HVAC', 'Marco Reyes — HVAC, 3 open jobs'],
      ['HVAC', 'Samuel Osei — HVAC, off shift'],
      ['Different trade', 'Lena Park — Fire safety, 1 open job'],
      ['Different trade', 'Priya Natarajan — Electrical, 2 open jobs'],
      ['Different trade', 'Aisha Karim — Electrical, 3 open jobs'],
      ['Different trade', 'Tom Becker — Plumbing, 4 open jobs'],
    ])
    expect(select.value).toBe(aishaTechnician.id)
  })

  it('leaves out a group with nobody in it and names an inactive technician', async () => {
    const inactive = technician({ ...samuelTechnician, isActive: false, isOnShift: true })
    const { wrapper } = await openForm({
      [techniciansRoute]: json({ items: [priyaTechnician, inactive] }),
    })

    await wrapper.find('#checklist-template').setValue(generalElectrical)
    const select = technicianSelect(wrapper)

    expect(
      listedOptions(select).map(({ group, text, disabled }) => [group, text, disabled]),
    ).toEqual([
      [null, 'Leave unassigned', false],
      ['Electrical', 'Priya Natarajan — Electrical, 2 open jobs', false],
      ['Different trade', 'Samuel Osei — HVAC, inactive', true],
    ])

    await wrapper.find('#checklist-template').setValue(rooftopHvac)
    expect(Array.from(select.querySelectorAll('optgroup'), (group) => group.label)).toEqual([
      'HVAC',
      'Different trade',
    ])
  })

  it('sends technicianId only when a technician is chosen', async () => {
    const { wrapper, stub } = await openForm({
      [createRoute]: [problem(500, 'server.error'), problem(500, 'server.error')],
    })
    await fillValidJob(wrapper)

    await wrapper.find('#assign-technician').setValue(aishaTechnician.id)
    await submit(wrapper)
    await wrapper.find('#assign-technician').setValue('')
    await submit(wrapper)

    const bodies = stub
      .requestsTo(createRoute)
      .map((request) => JSON.parse(request.body ?? '{}') as Record<string, unknown>)
    expect(bodies[0]?.technicianId).toBe(aishaTechnician.id)
    expect(bodies[1]).not.toHaveProperty('technicianId')
  })

  it('reads Loading technicians… and stays disabled while the technicians load', async () => {
    const slow = deferred()
    const { wrapper } = await openForm({ [techniciansRoute]: slow.reply })

    expect(technicianSelect(wrapper).disabled).toBe(true)
    expect(listedOptions(technicianSelect(wrapper)).map(({ text }) => text)).toEqual([
      'Loading technicians…',
    ])

    slow.resolve(new Response(JSON.stringify({ items: demoTechnicians }), { status: 200 }))
    await flushPromises()
    expect(technicianSelect(wrapper).disabled).toBe(false)
    expect(technicianSelect(wrapper).value).toBe('')
  })

  it('explains a technicians load failure in the card and still creates the job unassigned', async () => {
    const { wrapper, stub } = await openForm({
      [techniciansRoute]: [
        problem(500, 'server.error', { correlationId: reference }),
        json({ items: demoTechnicians }),
      ],
      [createRoute]: problem(500, 'server.error'),
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    const banner = card.find('.banner--error')

    expect(technicianSelect(wrapper).disabled).toBe(true)
    expect(listedOptions(technicianSelect(wrapper)).map(({ text }) => text)).toEqual([
      'Technicians are unavailable',
    ])
    expect(banner.text()).toContain(
      'Technicians could not be loaded. You can still create the job unassigned.',
    )
    expect(banner.text()).toContain(`Reference ${reference}`)

    await fillValidJob(wrapper)
    await submit(wrapper)
    expect(JSON.parse(stub.requestsTo(createRoute)[0]?.body ?? '{}')).not.toHaveProperty(
      'technicianId',
    )

    await card.find('.banner--error button').trigger('click')
    await flushPromises()
    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(technicianSelect(wrapper).disabled).toBe(false)
    expect(card.find('.banner--error').exists()).toBe(false)
  })

  it('reports a technicians load failure as a status, and Try again moves focus to the technician select', async () => {
    const { wrapper } = await openForm({
      [techniciansRoute]: [problem(500, 'server.error'), json({ items: demoTechnicians })],
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    const banner = card.find('.banner--error')
    expect(banner.attributes('role')).toBe('status')
    banner.find('button').element.focus()

    await banner.find('button').trigger('click')
    await flushPromises()

    expect(card.find('.banner--error').exists()).toBe(false)
    expect(technicianSelect(wrapper).disabled).toBe(false)
    expect(document.activeElement).toBe(technicianSelect(wrapper))
  })

  it('announces a second technicians failure as an alert and moves focus to the new Try again', async () => {
    const { wrapper } = await openForm({
      [techniciansRoute]: [problem(500, 'server.error'), networkFailure()],
    })
    const card = wrapper.find('section[aria-labelledby="checklist-section-title"]')
    card.find<HTMLButtonElement>('.banner--error button').element.focus()

    await card.find('.banner--error button').trigger('click')
    await flushPromises()

    const banner = card.find('.banner--error')
    expect(banner.text()).toContain('Technicians could not be loaded.')
    expect(banner.attributes('role')).toBe('alert')
    expect(document.activeElement).toBe(banner.find('button').element)
    expect(technicianSelect(wrapper).disabled).toBe(true)
  })

  it('marks the select, refreshes the list and focuses it when the technician is no longer available', async () => {
    const { wrapper, stub } = await openForm({
      [createRoute]: problem(409, 'technician.unavailable'),
    })
    await fillValidJob(wrapper)
    await wrapper.find('#assign-technician').setValue(priyaTechnician.id)

    await submit(wrapper)

    expect(stub.requestsTo(techniciansRoute)).toHaveLength(2)
    expect(errorOf(wrapper, 'assign-technician')).toBe(
      'This technician is no longer available. Choose another technician or leave the job unassigned.',
    )
    expect(technicianSelect(wrapper).getAttribute('aria-describedby')).toBe(
      'assign-technician-error assign-technician-help',
    )
    expect(wrapper.find('.create-job-form > .banner--error').exists()).toBe(false)
    expect(document.activeElement?.id).toBe('assign-technician')

    await wrapper.find('#assign-technician').setValue('')
    expect(errorOf(wrapper, 'assign-technician')).toBeNull()
  })

  it('shows a server error about the technician on the select and names it in the summary', async () => {
    const { wrapper } = await openForm({
      [createRoute]: problem(400, 'request.validation-failed', {
        errors: { technicianId: ['Choose a technician who exists.'] },
      }),
    })
    await fillValidJob(wrapper)
    await wrapper.find('#assign-technician').setValue(priyaTechnician.id)

    await submit(wrapper)

    expect(errorOf(wrapper, 'assign-technician')).toBe('Choose a technician who exists.')
    expect(wrapper.find('.create-job-form > .banner--error').text()).toBe(
      'Some details need attention. Check Assign technician.',
    )
    expect(document.activeElement?.id).toBe('assign-technician')
  })
})
