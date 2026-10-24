import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'

import App from '@/app/App.vue'
import { createAppRouter, installNavigationGuards } from '@/app/router'
import { ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'
import { sessionResponse } from '@/features/auth/__tests__/session-fixtures'
import { signedIn } from '@/features/realtime/__tests__/realtime-fixtures'
import { useToasts } from '@/shared/composables/use-toasts'
import { noBreakSpace as noBreak } from '@/shared/format/date-time'
import { contractProblems } from '@/shared/testing/contract-schema'
import { clickBackdrop, isModal, pressEscape } from '@/shared/testing/dialog'
import {
  deferred,
  json,
  networkFailure,
  problem,
  stubFetch,
  type FetchStub,
  type Reply,
  type RouteTable,
} from '@/shared/testing/fetch-stub'

import type { JobDetails } from '../jobs-api'
import { isOwnCancellation, useJobsStore } from '../jobs-store'
import {
  administratorSubmit,
  dana,
  demoTechnicians,
  demoToday,
  historyEntry,
  jobDetails,
  marco,
  onDemoDay,
  readOnlyAffordances,
} from './job-fixtures'

const job = jobDetails()

const jobRoute = `GET /api/jobs/${job.id}`

const cancelRoute = `POST /api/jobs/${job.id}/cancellation`

const reason = 'Building management replaced the filters under its own maintenance contract.'

const otherDispatcher = { id: '01a0f6b1-4000-7000-8000-000000000099', displayName: 'Ravi Shah' }

function cancelledJob(
  actor: { readonly id: string; readonly displayName: string } = dana,
  note: string = reason,
  version: number = job.version + 1,
): JobDetails {
  return jobDetails({
    status: 'Cancelled',
    updatedAt: demoToday.toISOString(),
    cancelledAt: demoToday.toISOString(),
    history: [
      ...job.history,
      historyEntry({
        id: '01a1549c-bac0-7c11-8f00-000000001051',
        action: 'Cancelled',
        fromStatus: 'Unassigned',
        toStatus: 'Cancelled',
        actor,
        note,
        occurredAt: demoToday.toISOString(),
      }),
    ],
    version,
    affordances: readOnlyAffordances(),
  })
}

function completedJob(): JobDetails {
  return jobDetails({
    status: 'Completed',
    assignedTechnician: marco,
    completedAt: demoToday.toISOString(),
    updatedAt: demoToday.toISOString(),
    version: job.version + 5,
    affordances: readOnlyAffordances(),
  })
}

async function openJob(routes: RouteTable): Promise<FetchStub> {
  const stub = await signedIn({ [jobRoute]: json(job), ...routes })
  await useJobsStore().openJob(job.id)
  return stub
}

function sentBodies(stub: FetchStub): unknown[] {
  return stub.requestsTo(cancelRoute).map((request) => JSON.parse(request.body) as unknown)
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(demoToday)
})

describe('cancelling a job', () => {
  it('sends the trimmed reason with the version of the job on the page, and shows the cancelled job read-only', async () => {
    const stub = await openJob({ [cancelRoute]: json(cancelledJob()) })
    const store = useJobsStore()

    const outcome = await store.cancelJob(`  ${reason}\n`)

    expect(sentBodies(stub)).toEqual([{ reason, version: job.version }])
    expect(contractProblems('CancelJobRequest', sentBodies(stub)[0])).toEqual([])
    expect(outcome).toEqual({ kind: 'cancelled', job: cancelledJob() })
    expect(store.job?.status).toBe('Cancelled')
    expect(store.job?.affordances).toMatchObject({ canAssign: false, canCancel: false })
    expect(store.job?.version).toBe(job.version + 1)
    expect(store.jobStatus).toBe('ready')
    expect(store.jobNotice).toBeNull()
    expect(stub.requestsTo(jobRoute)).toHaveLength(1)
  })

  it('reads a job with canCancel from the contract, and a cancelled job without it', () => {
    expect(contractProblems('JobDetailsResponse', job)).toEqual([])
    expect(contractProblems('JobDetailsResponse', cancelledJob())).toEqual([])
    expect(job.affordances.canCancel).toBe(true)
  })

  it.each([
    ['another dispatcher', otherDispatcher, reason],
    ['the same dispatcher with another reason', dana, 'Customer cancelled by phone.'],
  ] as const)(
    'tells an already-cancelled job apart when the newest cancellation is by %s',
    async (_name, actor, note) => {
      const already = cancelledJob(actor, note, job.version + 3)
      await openJob({ [cancelRoute]: json(already) })
      const store = useJobsStore()

      const outcome = await store.cancelJob(reason)

      expect(outcome).toEqual({ kind: 'already-cancelled', job: already })
      expect(store.job).toEqual(already)
      expect(store.jobNotice).toEqual({
        id: expect.any(Number),
        code: 'already-cancelled',
        jobNumber: job.number,
      })
    },
  )

  it('returns the server message of a 400 keyed reason, keeping the job and sending nothing else', async () => {
    const message = 'Enter a reason for cancelling the job.'
    const stub = await openJob({
      [cancelRoute]: problem(400, 'request.validation-failed', { errors: { reason: [message] } }),
    })
    const store = useJobsStore()

    const outcome = await store.cancelJob('   ')

    expect(outcome).toEqual({ kind: 'reason-rejected', message })
    expect(sentBodies(stub)).toEqual([{ reason: '', version: job.version }])
    expect(store.job).toEqual(job)
    expect(store.jobNotice).toBeNull()
    expect(stub.requestsTo(jobRoute)).toHaveLength(1)
  })

  it('shows the missing-job state after a 404', async () => {
    await openJob({ [cancelRoute]: problem(404, 'job.not-found') })
    const store = useJobsStore()

    const outcome = await store.cancelJob(reason)

    expect(outcome).toEqual({ kind: 'not-found' })
    expect(store.job).toBeNull()
    expect(store.jobStatus).toBe('not-found')
  })

  it('refetches the job after 409 job.invalid-transition and explains it at the top of the page', async () => {
    const completed = completedJob()
    const stub = await openJob({
      [jobRoute]: [json(job), json(completed)],
      [cancelRoute]: problem(409, 'job.invalid-transition'),
    })
    const store = useJobsStore()

    const outcome = await store.cancelJob(reason)

    expect(outcome).toEqual({ kind: 'invalid-transition' })
    expect(stub.requestsTo(jobRoute)).toHaveLength(2)
    expect(store.job).toEqual(completed)
    expect(store.jobNotice).toEqual({
      id: expect.any(Number),
      code: 'job.invalid-transition',
      jobNumber: job.number,
    })
  })

  it('refetches the job after 409 concurrency.conflict, and the next press sends the refetched version', async () => {
    const changed = jobDetails({
      status: 'Assigned',
      assignedTechnician: marco,
      version: job.version + 2,
      affordances: {
        allowedStatusChanges: [],
        canSubmit: administratorSubmit(),
        canAssign: true,
        canCancel: true,
      },
    })
    const stub = await openJob({
      [jobRoute]: [json(job), json(changed)],
      [cancelRoute]: [problem(409, 'concurrency.conflict'), json(cancelledJob())],
    })
    const store = useJobsStore()

    const first = await store.cancelJob(reason)

    expect(first).toEqual({ kind: 'conflict' })
    expect(store.job).toEqual(changed)
    expect(store.jobNotice).toBeNull()

    const second = await store.cancelJob(reason)

    expect(second.kind).toBe('cancelled')
    expect(sentBodies(stub)).toEqual([
      { reason, version: job.version },
      { reason, version: job.version + 2 },
    ])
  })

  it('treats a conflict whose refetched job can no longer be cancelled as an invalid transition', async () => {
    const completed = completedJob()
    await openJob({
      [jobRoute]: [json(job), json(completed)],
      [cancelRoute]: problem(409, 'concurrency.conflict'),
    })
    const store = useJobsStore()

    const outcome = await store.cancelJob(reason)

    expect(outcome).toEqual({ kind: 'invalid-transition' })
    expect(store.jobNotice?.code).toBe('job.invalid-transition')
  })

  it('shows the missing-job state when the conflict refetch finds no job', async () => {
    await openJob({
      [jobRoute]: [json(job), problem(404, 'job.not-found')],
      [cancelRoute]: problem(409, 'concurrency.conflict'),
    })
    const store = useJobsStore()

    const outcome = await store.cancelJob(reason)

    expect(outcome).toEqual({ kind: 'not-found' })
    expect(store.jobStatus).toBe('not-found')
  })

  it.each([
    [
      '400 with another key',
      problem(400, 'request.validation-failed', { errors: { version: ['x'] } }),
      400,
    ],
    ['403', problem(403, 'auth.forbidden'), 403],
    ['409 with another code', problem(409, 'job.not-assignable'), 409],
    ['429', problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '12' }), 429],
    ['500', problem(500, 'server.error'), 500],
    ['no response', networkFailure(), 0],
  ] as const)(
    'returns the failure of %s with its status, sending once and keeping the job',
    async (_name, reply: Reply, status) => {
      const stub = await openJob({ [cancelRoute]: reply })
      const store = useJobsStore()

      const outcome = await store.cancelJob(reason)
      await flushPromises()

      const error = outcome.kind === 'failed' ? outcome.error : null
      expect(outcome).toEqual({ kind: 'failed', error: expect.objectContaining({ status }) })
      expect(error?.correlationId === null).toBe(status === 0)
      expect(stub.requestsTo(cancelRoute)).toHaveLength(1)
      expect(stub.requestsTo(jobRoute)).toHaveLength(1)
      expect(store.job).toEqual(job)
      expect(store.jobNotice).toBeNull()
    },
  )

  it('keeps the Retry-After of a 429 for the wait copy', async () => {
    await openJob({
      [cancelRoute]: problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '12' }),
    })

    const outcome = await useJobsStore().cancelJob(reason)

    expect(outcome.kind === 'failed' && outcome.error.retryAfterSeconds).toBe(12)
  })

  it('leaves a page that moved on alone when the cancellation answers late', async () => {
    const late = deferred()
    await openJob({ [cancelRoute]: late.reply })
    const store = useJobsStore()

    const pending = store.cancelJob(reason)
    await late.requested
    store.closeJob()
    late.resolve(
      new Response(JSON.stringify(cancelledJob()), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    expect((await pending).kind).toBe('cancelled')
    expect(store.job).toBeNull()
    expect(store.jobNotice).toBeNull()
  })

  it('refuses to cancel when no job is open', async () => {
    await signedIn()

    await expect(useJobsStore().cancelJob(reason)).rejects.toThrow('No job is open to cancel')
  })
})

describe('telling an own cancellation apart', () => {
  it('needs a Cancelled job whose newest Cancelled entry has the actor and the reason sent', () => {
    expect(isOwnCancellation(cancelledJob(), dana.id, reason)).toBe(true)
    expect(isOwnCancellation(cancelledJob(), otherDispatcher.id, reason)).toBe(false)
    expect(isOwnCancellation(cancelledJob(), dana.id, `${reason} `)).toBe(false)
    expect(isOwnCancellation(cancelledJob(), null, reason)).toBe(false)
    expect(isOwnCancellation(job, dana.id, reason)).toBe(false)
  })

  it('reads the newest Cancelled entry when the history holds more than one', () => {
    const older = cancelledJob(otherDispatcher, 'An older reason.')
    const twice: JobDetails = {
      ...older,
      history: [
        ...older.history,
        historyEntry({
          id: '01a1549c-bac0-7c11-8f00-000000002051',
          action: 'Cancelled',
          fromStatus: 'Cancelled',
          toStatus: 'Cancelled',
          actor: dana,
          note: reason,
          occurredAt: demoToday.toISOString(),
        }),
      ],
    }

    expect(isOwnCancellation(twice, dana.id, reason)).toBe(true)
    expect(isOwnCancellation(older, dana.id, reason)).toBe(false)
  })
})

describe('the cancel dialog on the job page', () => {
  let page: VueWrapper | null = null

  afterEach(() => {
    page?.unmount()
    page = null
  })

  async function openPage(
    details: JobDetails = job,
    routes: RouteTable = {},
  ): Promise<{ readonly wrapper: VueWrapper; readonly stub: FetchStub }> {
    const stub = stubFetch({
      'POST /api/auth/refresh': json(sessionResponse()),
      [`GET /api/jobs/${details.id}`]: json(details),
      'GET /api/technicians': json({ items: demoTechnicians }),
      ...routes,
    })
    const router = createAppRouter(createMemoryHistory())
    installNavigationGuards(router)
    await router.push(`/jobs/${details.id}`)
    const wrapper = mount(App, { global: { plugins: [router] }, attachTo: document.body })
    page = wrapper
    await flushPromises()
    return { wrapper, stub }
  }

  function trigger(wrapper: VueWrapper): DOMWrapper<HTMLButtonElement> | undefined {
    return wrapper
      .findAll<HTMLButtonElement>('main button')
      .find((button) => button.text() === 'Cancel job' && button.element.closest('dialog') === null)
  }

  function dialog(wrapper: VueWrapper): DOMWrapper<HTMLDialogElement> {
    return wrapper.find<HTMLDialogElement>('dialog')
  }

  function reasonField(wrapper: VueWrapper): DOMWrapper<HTMLTextAreaElement> {
    return dialog(wrapper).find<HTMLTextAreaElement>('textarea')
  }

  function dialogButton(wrapper: VueWrapper, label: string): DOMWrapper<HTMLButtonElement> {
    const found = dialog(wrapper)
      .findAll<HTMLButtonElement>('button')
      .find((button) => button.text() === label)
    if (found === undefined) {
      throw new Error(`No dialog button ${label}`)
    }
    return found
  }

  function words(text: string): string {
    return text.split(noBreak).join(' ').replace(/\s+/g, ' ').trim()
  }

  function textOf(element: Element | null | undefined): string {
    if (element === null || element === undefined) {
      return ''
    }
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
    const parts: string[] = []
    for (let current = walker.nextNode(); current !== null; current = walker.nextNode()) {
      parts.push(current.textContent ?? '')
    }
    return words(parts.join(' '))
  }

  function announcements(wrapper: VueWrapper): string[] {
    return wrapper
      .findAll('main p.visually-hidden[role="status"]')
      .map((status) => status.text())
      .filter((text) => text.length > 0)
  }

  function topOfPage(wrapper: VueWrapper): string[] {
    const content = wrapper.find('main header.page-header').element.parentElement
    return Array.from(content?.children ?? [], (child) => {
      if (child.classList.contains('page-header')) {
        return 'header'
      }
      if (child.classList.contains('job-detail__cancelled')) {
        return 'cancelled notice'
      }
      if (child.classList.contains('banner--warning')) {
        return 'conflict banner'
      }
      return child.classList.contains('job-detail') ? 'job' : ''
    }).filter((part) => part.length > 0)
  }

  async function openDialog(wrapper: VueWrapper): Promise<void> {
    await trigger(wrapper)?.trigger('click')
    await flushPromises()
  }

  async function confirmWith(wrapper: VueWrapper, text: string): Promise<void> {
    await reasonField(wrapper).setValue(text)
    await dialogButton(wrapper, 'Cancel job').trigger('click')
    await flushPromises()
  }

  it('shows Cancel job only while canCancel is true, as the last item of the left column', async () => {
    const { wrapper } = await openPage()
    const button = trigger(wrapper)

    expect(button?.classes()).toEqual(
      expect.arrayContaining(['app-button--danger', 'job-detail__cancel']),
    )
    expect(button?.element.parentElement?.lastElementChild).toBe(button?.element)
    expect(button?.element.parentElement?.classList.contains('job-detail__main')).toBe(true)
    expect(dialog(wrapper).exists()).toBe(false)
    page?.unmount()

    const { wrapper: readOnly } = await openPage(completedJob())
    expect(trigger(readOnly)).toBeUndefined()
    expect(readOnly.find('dialog').exists()).toBe(false)
  })

  it('opens a modal dialog described by the job line, the status row and the consequence, with focus in the reason', async () => {
    const assigned = jobDetails({ status: 'Assigned', assignedTechnician: marco })
    const { wrapper } = await openPage(assigned)

    await openDialog(wrapper)

    const element = dialog(wrapper).element
    const described = (element.getAttribute('aria-describedby') ?? '')
      .split(' ')
      .map((id) => textOf(document.getElementById(id)))
    expect(isModal(element)).toBe(true)
    expect(
      document.getElementById(element.getAttribute('aria-labelledby') ?? '')?.textContent,
    ).toBe('Cancel this job?')
    expect(described).toEqual([
      'JOB-1051 · , Replace faulty breaker panel',
      'Assigned Marco Reyes',
      'Marco Reyes sees it as cancelled in the technician app. Cancelled jobs are read-only. This cannot be undone.',
    ])
    expect(
      dialog(wrapper).find('.cancel-dialog__job [aria-hidden="true"]').element.textContent,
    ).toBe('\u{00a0}\u{00b7} ')
    expect(document.activeElement).toBe(reasonField(wrapper).element)
    expect(reasonField(wrapper).attributes()).toMatchObject({
      'aria-required': 'true',
      maxlength: '500',
    })
    expect(dialog(wrapper).find('label').text()).toBe('Reason for cancelling')
    expect(dialog(wrapper).find('.text-area__help').text()).toBe(
      "The reason is saved in the job's status history.",
    )
    expect(dialog(wrapper).find('.banner').exists()).toBe(false)
    expect(
      dialog(wrapper)
        .findAll('.confirm-dialog__actions button')
        .map((button) => button.text()),
    ).toEqual(['Keep job', 'Cancel job'])
  })

  it('names only the consequence when the job has no technician', async () => {
    const { wrapper } = await openPage()

    await openDialog(wrapper)

    expect(dialog(wrapper).find('.cancel-dialog__status').text()).toBe('Unassigned')
    expect(dialog(wrapper).find('.cancel-dialog__consequence').text()).toBe(
      'Cancelled jobs are read-only. This cannot be undone.',
    )
  })

  it('closes on Keep job and on Esc with focus back on Cancel job, keeps the reason for the next opening, and ignores a backdrop click', async () => {
    const { wrapper, stub } = await openPage()

    await openDialog(wrapper)
    clickBackdrop(dialog(wrapper).element)
    await flushPromises()
    expect(dialog(wrapper).exists()).toBe(true)

    await reasonField(wrapper).setValue('Customer asked to rebook')
    await dialogButton(wrapper, 'Keep job').trigger('click')
    await flushPromises()
    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(trigger(wrapper)?.element)

    await openDialog(wrapper)
    expect(reasonField(wrapper).element.value).toBe('Customer asked to rebook')
    pressEscape(dialog(wrapper).element)
    await flushPromises()
    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(trigger(wrapper)?.element)

    await openDialog(wrapper)
    expect(reasonField(wrapper).element.value).toBe('Customer asked to rebook')
    expect(stub.requestsTo(cancelRoute)).toHaveLength(0)
  })

  it('asks for a reason without sending when it is blank, and drops the error once the reason holds text', async () => {
    const { wrapper, stub } = await openPage()
    await openDialog(wrapper)

    await confirmWith(wrapper, '  \n ')

    const field = reasonField(wrapper)
    const error = dialog(wrapper).find('.text-area__error')
    expect(words(error.text())).toBe('Enter a reason for cancelling the job.')
    expect(field.attributes('aria-invalid')).toBe('true')
    expect(field.attributes('aria-describedby')?.split(' ')).toEqual([
      error.attributes('id'),
      dialog(wrapper).find('.text-area__help').attributes('id'),
    ])
    expect(document.activeElement).toBe(field.element)
    expect(stub.requestsTo(cancelRoute)).toHaveLength(0)

    await field.setValue(' x')
    expect(dialog(wrapper).find('.text-area__error').exists()).toBe(false)
  })

  it('shows the count line from 400 characters, announced when it appears and at the limit, never per keystroke', async () => {
    const { wrapper } = await openPage()
    await openDialog(wrapper)
    const status = (): string =>
      dialog(wrapper).find('.cancel-dialog__field p.visually-hidden[role="status"]').text()

    await reasonField(wrapper).setValue('a'.repeat(399))
    await flushPromises()
    expect(dialog(wrapper).find('.text-area__count').exists()).toBe(false)
    expect(status()).toBe('')

    await reasonField(wrapper).setValue('a'.repeat(400))
    await flushPromises()
    expect(dialog(wrapper).find('.text-area__count').text()).toBe('100 characters left')
    expect(status()).toBe('100 characters left')
    expect(reasonField(wrapper).attributes('aria-describedby')?.split(' ')).toHaveLength(2)

    await reasonField(wrapper).setValue('a'.repeat(450))
    await flushPromises()
    expect(dialog(wrapper).find('.text-area__count').text()).toBe('50 characters left')
    expect(status()).toBe('100 characters left')

    await reasonField(wrapper).setValue('a'.repeat(499))
    await flushPromises()
    expect(dialog(wrapper).find('.text-area__count').text()).toBe('1 character left')

    await reasonField(wrapper).setValue('a'.repeat(500))
    await flushPromises()
    expect(dialog(wrapper).find('.text-area__count').text()).toBe('No characters left')
    expect(status()).toBe('No characters left')
  })

  it('reads Cancelling… with a read-only reason while the request runs, and ignores Keep job and Esc', async () => {
    const reply = deferred()
    const { wrapper } = await openPage(job, { [cancelRoute]: reply.reply })
    await openDialog(wrapper)
    await reasonField(wrapper).setValue(reason)
    const confirm = dialogButton(wrapper, 'Cancel job')
    confirm.element.focus()

    await confirm.trigger('click')
    await reply.requested
    await flushPromises()

    const busy = dialog(wrapper)
      .findAll('.confirm-dialog__actions button')
      .find((button) => button.text() === 'Cancelling…')
    expect(busy?.attributes('aria-disabled')).toBe('true')
    expect(reasonField(wrapper).attributes('readonly')).toBeDefined()
    await dialogButton(wrapper, 'Keep job').trigger('click')
    pressEscape(dialog(wrapper).element)
    await flushPromises()
    expect(dialog(wrapper).exists()).toBe(true)
    expect(dialog(wrapper).element.open).toBe(true)
    expect(document.activeElement).toBe(busy?.element)

    reply.resolve(
      new Response(JSON.stringify(cancelledJob()), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    await flushPromises()
    expect(dialog(wrapper).exists()).toBe(false)
  })

  it('after a cancellation closes, announces it, focuses the h1 and shows the read-only page with its notice', async () => {
    const { wrapper, stub } = await openPage(job, { [cancelRoute]: json(cancelledJob()) })
    await openDialog(wrapper)

    await confirmWith(wrapper, `  ${reason}`)

    expect(sentBodies(stub)).toEqual([{ reason, version: job.version }])
    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    expect(announcements(wrapper)).toEqual(['JOB-1051 cancelled.'])
    const notice = wrapper.find('.job-detail__cancelled')
    expect(notice.attributes('role')).toBeUndefined()
    expect(words(notice.find('.banner__title').text())).toBe(
      'Cancelled at 10:42 AM by Dana Whitfield.',
    )
    expect(notice.find('.banner__detail').text()).toBe(`“${reason}”`)
    expect(wrapper.find('.page-header__meta .status-chip').text()).toBe('Cancelled')
    expect(trigger(wrapper)).toBeUndefined()
    expect(wrapper.find('.job-detail__side').exists()).toBe(false)
    expect(wrapper.find('.job-detail__live-line').exists()).toBe(false)
    expect(wrapper.find('main .banner--warning').exists()).toBe(false)
    expect(useToasts().toasts).toHaveLength(0)
  })

  it('reads an already-cancelled answer as a conflict at the top of the page, with the notice naming who cancelled it', async () => {
    const { wrapper } = await openPage(job, {
      [cancelRoute]: json(cancelledJob(otherDispatcher, 'Duplicate of JOB-1050.')),
    })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    const banner = wrapper.find('main .banner--warning')
    expect(banner.attributes('role')).toBe('alert')
    expect(words(banner.text())).toBe(
      'JOB-1051 was already cancelled. The status history shows who cancelled it and why.',
    )
    expect(textOf(wrapper.find('.job-detail__cancelled').element)).toBe(
      'Cancelled at 10:42 AM by Ravi Shah. “Duplicate of JOB-1050.”',
    )
    expect(announcements(wrapper)).toEqual([])
  })

  it('puts the conflict banner of an already-cancelled answer directly under the header, above the Cancelled notice', async () => {
    const { wrapper } = await openPage(job, {
      [cancelRoute]: json(cancelledJob(otherDispatcher, 'x'.repeat(500))),
    })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    const banner = wrapper.find('main .banner--warning').element
    const notice = wrapper.find('.job-detail__cancelled').element
    expect(topOfPage(wrapper)).toEqual(['header', 'conflict banner', 'cancelled notice', 'job'])
    expect(banner.compareDocumentPosition(notice) & Node.DOCUMENT_POSITION_FOLLOWING).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
  })

  it('puts the conflict banner above the Cancelled notice when a live refresh closes an idle dialog on a job cancelled meanwhile', async () => {
    const { wrapper } = await openPage()
    await openDialog(wrapper)
    await reasonField(wrapper).setValue(reason)

    useJobsStore().applyLiveJob(cancelledJob(otherDispatcher, 'Duplicate of JOB-1050.'))
    await flushPromises()

    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    expect(words(wrapper.find('main .banner--warning').text())).toContain(
      'JOB-1051 can no longer be cancelled.',
    )
    expect(textOf(wrapper.find('.job-detail__cancelled').element)).toBe(
      'Cancelled at 10:42 AM by Ravi Shah. “Duplicate of JOB-1050.”',
    )
    expect(topOfPage(wrapper)).toEqual(['header', 'conflict banner', 'cancelled notice', 'job'])
  })

  it('shows the Cancelled notice alone under the header when no conflict banner shows', async () => {
    const { wrapper } = await openPage(cancelledJob())

    expect(topOfPage(wrapper)).toEqual(['header', 'cancelled notice', 'job'])
  })

  it('shows the server message on the reason after a 400 keyed reason, with focus in the field', async () => {
    const message = 'The reason may hold at most 500 characters.'
    const { wrapper } = await openPage(job, {
      [cancelRoute]: problem(400, 'request.validation-failed', { errors: { reason: [message] } }),
    })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    expect(words(dialog(wrapper).find('.text-area__error').text())).toBe(message)
    expect(document.activeElement).toBe(reasonField(wrapper).element)
    expect(dialog(wrapper).find('.banner').exists()).toBe(false)

    await reasonField(wrapper).setValue(`${reason}.`)
    expect(dialog(wrapper).find('.text-area__error').exists()).toBe(false)
  })

  it.each([
    [
      'a 400 with another key',
      problem(400, 'request.validation-failed', { errors: { version: ['Send the version.'] } }),
      'The job could not be cancelled. Try again in a moment.',
      true,
    ],
    ['a 403', problem(403, 'auth.forbidden'), 'Your account cannot cancel jobs.', true],
    [
      'a 429',
      problem(429, 'rate-limit.exceeded', {}, { 'Retry-After': '30' }),
      'Too many requests. Try again in 30 seconds.',
      false,
    ],
    [
      'no response',
      networkFailure(),
      'FieldService could not be reached. Check your connection, then try again.',
      false,
    ],
    [
      'another failure',
      problem(500, 'server.error'),
      'The job could not be cancelled. Try again in a moment.',
      true,
    ],
  ] as const)(
    'keeps the dialog open with the reason after %s, the banner above the actions and focus on Cancel job',
    async (_name, reply, text, referenced) => {
      const { wrapper } = await openPage(job, { [cancelRoute]: reply })
      await openDialog(wrapper)

      await confirmWith(wrapper, reason)

      const banner = dialog(wrapper).find('.confirm-dialog__banner .banner--error')
      expect(banner.attributes('role')).toBe('alert')
      expect(words(banner.find('.banner__text').text())).toBe(text)
      expect(banner.find('.banner__reference').exists()).toBe(referenced)
      expect(banner.find('button').exists()).toBe(false)
      expect(reasonField(wrapper).element.value).toBe(reason)
      expect(document.activeElement).toBe(dialogButton(wrapper, 'Cancel job').element)
      const parts = Array.from(dialog(wrapper).element.children, (child) => child.className)
      expect(parts.indexOf('confirm-dialog__banner')).toBe(
        parts.indexOf('confirm-dialog__actions') - 1,
      )
    },
  )

  it('shows the missing-job page after a 404, with focus on its h1', async () => {
    const { wrapper } = await openPage(job, { [cancelRoute]: problem(404, 'job.not-found') })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    expect(dialog(wrapper).exists()).toBe(false)
    expect(wrapper.find('h1').text()).toBe('Job not found')
    expect(document.activeElement).toBe(wrapper.find('h1').element)
  })

  it('closes after job.invalid-transition with the banner at the top of the page and focus on the h1', async () => {
    const { wrapper } = await openPage(job, {
      [jobRoute]: [json(job), json(completedJob())],
      [cancelRoute]: problem(409, 'job.invalid-transition'),
    })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    expect(words(wrapper.find('main .banner--warning').text())).toBe(
      'JOB-1051 can no longer be cancelled. Its status changed after you opened it. The latest version is shown.',
    )
    expect(wrapper.find('.page-header__meta .status-chip').text()).toBe('Completed')
    expect(trigger(wrapper)).toBeUndefined()
  })

  it('stays open after concurrency.conflict with the refetched status in it, and the next press sends its version', async () => {
    const changed = jobDetails({
      status: 'Assigned',
      assignedTechnician: marco,
      version: job.version + 2,
    })
    const { wrapper, stub } = await openPage(job, {
      [jobRoute]: [json(job), json(changed)],
      [cancelRoute]: [problem(409, 'concurrency.conflict'), json(cancelledJob())],
    })
    await openDialog(wrapper)

    await confirmWith(wrapper, reason)

    const banner = dialog(wrapper).find('.banner--warning')
    expect(banner.attributes('role')).toBe('alert')
    expect(words(banner.text())).toBe(
      'JOB-1051 changed after you opened it. Its current status is shown above. Cancel it again if you still want to.',
    )
    expect(banner.find('.banner__reference').exists()).toBe(false)
    expect(textOf(dialog(wrapper).find('.cancel-dialog__status').element)).toBe(
      'Assigned Marco Reyes',
    )
    expect(reasonField(wrapper).element.value).toBe(reason)
    expect(document.activeElement).toBe(dialogButton(wrapper, 'Cancel job').element)
    expect(wrapper.find('main > .app-shell__content > .banner--warning').exists()).toBe(false)

    await dialogButton(wrapper, 'Cancel job').trigger('click')
    await flushPromises()

    expect(sentBodies(stub)).toEqual([
      { reason, version: job.version },
      { reason, version: job.version + 2 },
    ])
    expect(dialog(wrapper).exists()).toBe(false)
  })

  it('closes an idle dialog when a live refresh removes Cancel job, with the invalid-transition banner and focus on the h1', async () => {
    const { wrapper } = await openPage()
    await openDialog(wrapper)
    await reasonField(wrapper).setValue(reason)

    useJobsStore().applyLiveJob(completedJob())
    await flushPromises()

    expect(dialog(wrapper).exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    expect(words(wrapper.find('main .banner--warning').text())).toContain(
      'JOB-1051 can no longer be cancelled.',
    )
  })

  it('moves focus from a Cancel job that a live refresh removed to the h1, without a banner', async () => {
    const { wrapper } = await openPage()
    trigger(wrapper)?.element.focus()

    useJobsStore().applyLiveJob(completedJob())
    await flushPromises()

    expect(trigger(wrapper)).toBeUndefined()
    expect(document.activeElement).toBe(wrapper.find('h1').element)
    expect(wrapper.find('main .banner--warning').exists()).toBe(false)
  })

  it('shows the Cancelled notice with the day, actor and reason for a job cancelled earlier', async () => {
    const earlier = jobDetails({
      status: 'Cancelled',
      assignedTechnician: marco,
      cancelledAt: onDemoDay(-3, 9, 20),
      updatedAt: onDemoDay(-3, 9, 20),
      history: [
        historyEntry({ occurredAt: onDemoDay(-4, 9, 40) }),
        historyEntry({
          id: '01a1549c-bac0-7c11-8f00-000000001036',
          action: 'Cancelled',
          fromStatus: 'Assigned',
          toStatus: 'Cancelled',
          note: 'Building management replaced the filters under its own maintenance contract.',
          occurredAt: onDemoDay(-3, 9, 20),
        }),
      ],
      affordances: readOnlyAffordances(),
    })
    const { wrapper } = await openPage(earlier)
    const notice = wrapper.find('.job-detail__cancelled')

    expect(words(notice.find('.banner__title').text())).toBe(
      'Cancelled on Fri 16 Oct at 9:20 AM by Dana Whitfield.',
    )
    expect(notice.find('.banner__title').text()).toContain(`Fri${noBreak}16${noBreak}Oct`)
    expect(notice.find('.banner__detail').text()).toBe(
      '“Building management replaced the filters under its own maintenance contract.”',
    )
    expect(notice.find('.banner__tile svg').exists()).toBe(true)
    expect(notice.classes()).toContain('banner--stacked')
    expect(trigger(wrapper)).toBeUndefined()
    expect(wrapper.find('main .job-detail .job-detail__cancelled').exists()).toBe(false)
  })

  it('sizes the dialog for phones, stacks its actions below 400px and dims the page behind it', () => {
    const file = 'shared/ui/ConfirmDialog.vue'

    expect(ruleDeclarations(file, '.confirm-dialog')).toMatchObject({
      width: 'min(440px, calc(100% - 32px))',
      'max-height': 'calc(100dvh - 32px)',
      'overflow-y': 'auto',
      background: 'var(--fs-glass-sheet-bg)',
      'container-type': 'inline-size',
    })
    expect(ruleDeclarations(file, '.confirm-dialog::backdrop')).toEqual({
      background: 'var(--color-fs-overlay)',
    })
    expect(
      ruleDeclarations(file, '.confirm-dialog__actions', '@container (width < 350px)'),
    ).toEqual({ 'flex-direction': 'column', 'align-items': 'stretch' })
    expect(
      ruleDeclarations('features/jobs/pages/JobDetailPage.vue', '.job-detail__cancel'),
    ).toEqual({ 'align-self': 'flex-start' })
  })
})
