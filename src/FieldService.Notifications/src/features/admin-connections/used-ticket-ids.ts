import type { Clock } from '../../shared/clock.ts'
import { createTtlSet, type TtlSetEntry } from '../../shared/ttl-set.ts'

export const usedTicketIdCapacity = 10_000
export const usedTicketIdRetentionMs = 120_000
export const minimumEvictableAgeMs = 65_000
export const ticketVerifiableSeconds = 65

export type TicketIdOutcome = 'accepted' | 'reused' | 'full'

export interface UsedTicketIds {
  add(ticketId: string, issuedAtSeconds: number): TicketIdOutcome
}

interface UsedTicket {
  readonly issuedAtSeconds: number
}

function canNoLongerVerify(entry: TtlSetEntry<UsedTicket>, now: number): boolean {
  const olderThanMinimum = now - entry.addedAt > minimumEvictableAgeMs
  const pastVerifiableWindow =
    now >= (entry.value.issuedAtSeconds + ticketVerifiableSeconds + 1) * 1000
  return olderThanMinimum && pastVerifiableWindow
}

export function createUsedTicketIds(clock: Clock): UsedTicketIds {
  const ticketIds = createTtlSet<UsedTicket>({
    clock,
    ttlMs: usedTicketIdRetentionMs,
    capacity: usedTicketIdCapacity,
  })

  return {
    add: (ticketId, issuedAtSeconds) => {
      if (ticketIds.has(ticketId)) {
        return 'reused'
      }
      if (ticketIds.isFull() && !ticketIds.evictFirst(canNoLongerVerify)) {
        return 'full'
      }
      ticketIds.add(ticketId, { issuedAtSeconds })
      return 'accepted'
    },
  }
}
