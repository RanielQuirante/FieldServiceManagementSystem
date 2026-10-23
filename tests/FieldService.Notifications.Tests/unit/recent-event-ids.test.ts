import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import {
  createRecentEventIds,
  recentEventIdCapacity,
  retentionSecondsFor,
} from '../../../src/FieldService.Notifications/src/features/job-events/recent-event-ids.ts'
import { createFakeClock } from '../support/clock.ts'
import { newEventId } from '../support/signing.ts'

describe('recent event ids retention', () => {
  test('keeps ids for 2 x tolerance + 60 s', () => {
    assert.equal(retentionSecondsFor(300), 660)
    assert.equal(retentionSecondsFor(30), 120)
    assert.equal(retentionSecondsFor(1), 62)
    assert.equal(createRecentEventIds(createFakeClock(), 300).retentionMs, 660_000)
  })

  for (const tolerance of [0, 301, 1.5, -1, Number.NaN]) {
    test(`refuses a tolerance of ${tolerance}, outside the cap of 1 to 300`, () => {
      assert.throws(() => createRecentEventIds(createFakeClock(), tolerance), RangeError)
    })
  }

  test('refuses a duplicate inside 660 s and accepts the id again after it', () => {
    const clock = createFakeClock()
    const ids = createRecentEventIds(clock, 300)
    const id = newEventId()

    assert.equal(ids.add(id), true)
    assert.equal(ids.add(id), false)
    clock.advance(659_999)
    assert.equal(ids.add(id), false)
    clock.advance(1)
    assert.equal(ids.add(id), true)
    assert.equal(ids.add(id), false)
  })

  test('uses a 120 s window with a tolerance of 30 s', () => {
    const clock = createFakeClock()
    const ids = createRecentEventIds(clock, 30)
    const id = newEventId()

    assert.equal(ids.add(id), true)
    clock.advance(119_999)
    assert.equal(ids.add(id), false)
    clock.advance(1)
    assert.equal(ids.add(id), true)
  })

  test('keeps each id from its own first acceptance', () => {
    const clock = createFakeClock()
    const ids = createRecentEventIds(clock, 300)
    const first = newEventId()
    const second = newEventId()

    ids.add(first)
    clock.advance(300_000)
    ids.add(second)
    clock.advance(360_000)

    assert.equal(ids.add(first), true)
    assert.equal(ids.add(second), false)
  })

  test('tells different ids apart', () => {
    const ids = createRecentEventIds(createFakeClock(), 300)

    assert.equal(ids.add(newEventId()), true)
    assert.equal(ids.add(newEventId()), true)
  })
})

describe('recent event ids capacity', () => {
  test('holds at most 10,000 ids and evicts the oldest first inside the window', () => {
    const clock = createFakeClock()
    const ids = createRecentEventIds(clock, 300)
    const added = Array.from({ length: recentEventIdCapacity }, () => newEventId())

    assert.equal(recentEventIdCapacity, 10_000)
    for (const id of added) {
      assert.equal(ids.add(id), true)
      clock.advance(1)
    }

    const newest = newEventId()
    assert.equal(ids.add(newest), true)
    assert.equal(ids.add(newest), false)
    assert.equal(ids.add(added[1] ?? ''), false)
    assert.equal(ids.add(added.at(-1) ?? ''), false)
    assert.equal(ids.add(added[0] ?? ''), true, 'the evicted oldest id is accepted again')
  })

  test('makes room from expired ids before evicting a live one', () => {
    const clock = createFakeClock()
    const ids = createRecentEventIds(clock, 300)
    const early = newEventId()

    ids.add(early)
    clock.advance(600_000)
    const live = Array.from({ length: recentEventIdCapacity - 1 }, () => newEventId())
    for (const id of live) {
      ids.add(id)
    }
    clock.advance(60_000)

    assert.equal(ids.add(newEventId()), true)
    assert.equal(ids.add(live[0] ?? ''), false, 'no live id was evicted')
    assert.equal(ids.add(early), true)
  })
})
