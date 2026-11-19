import type { ItemSummary } from '../api/types'
import type { KbBucket, KbSummary } from '../api/kb'
import type { DesignSummary } from '../api/designs'

// Live suggestions for the jump-to-ref box: what the typed text already
// points at, across items, KB entries and designs. A `bucket:` or `design:`
// head narrows to that space; anything else searches all three by id / ref
// and title. Better matches first: an exact id or ref, then an id / ref
// prefix, then a title word starting with the text, then every word found
// somewhere. Open items rank ahead of closed ones on a tie.

export type JumpMatch =
  | { kind: 'item'; key: string; item: ItemSummary }
  | { kind: 'kb'; key: string; entry: KbSummary }
  | { kind: 'design'; key: string; design: DesignSummary }

export interface JumpPool {
  items: readonly ItemSummary[]
  kb: readonly KbSummary[]
  designs: readonly DesignSummary[]
}

const KB_BUCKETS: ReadonlySet<string> = new Set<KbBucket>(['adr', 'doc', 'feature', 'log', 'idea', 'source'])

const NO_MATCH = Number.POSITIVE_INFINITY

function score(q: string, ids: readonly string[], title: string): number {
  if (ids.some((id) => id === q)) return 0
  if (ids.some((id) => id.startsWith(q))) return 1
  const t = title.toLowerCase()
  if (t.startsWith(q) || t.includes(' ' + q)) return 2
  const words = q.split(/\s+/)
  const hay = [t, ...ids].join(' ')
  return words.every((w) => hay.includes(w)) ? 3 : NO_MATCH
}

function isClosed(m: JumpMatch): boolean {
  return m.kind === 'item' && (m.item.state === 'done' || m.item.state === 'dismissed')
}

export function jumpMatches(input: string, pool: JumpPool, limit = 8): JumpMatch[] {
  const raw = input.trim().toLowerCase()
  if (!raw) return []
  const head = /^([a-z]+):(.*)$/.exec(raw)
  const scope = head && (head[1] === 'design' || KB_BUCKETS.has(head[1]!)) ? head[1]! : null
  const q = scope ? head![2]!.trim() : raw

  const scored: { m: JumpMatch; s: number }[] = []
  const add = (m: JumpMatch, s: number) => { if (s !== NO_MATCH) scored.push({ m, s }) }
  // `adr:` alone lists the bucket; score it as a prefix hit so order stays stable.
  const rate = (ids: readonly string[], title: string) => (q ? score(q, ids, title) : 1)

  if (!scope) {
    for (const item of pool.items) {
      add({ kind: 'item', key: item.id, item }, rate([item.shortId.toLowerCase(), item.id.toLowerCase()], item.title))
    }
  }
  if (!scope || scope !== 'design') {
    for (const entry of pool.kb) {
      if (scope && entry.bucket !== scope) continue
      add({ kind: 'kb', key: entry.ref, entry }, rate([entry.slug, entry.ref], entry.title))
    }
  }
  if (!scope || scope === 'design') {
    for (const design of pool.designs) {
      add({ kind: 'design', key: `design:${design.slug}`, design }, rate([design.slug, `design:${design.slug}`], design.title))
    }
  }

  return scored
    .map((x, i) => ({ ...x, i }))
    .sort((a, b) => a.s - b.s || Number(isClosed(a.m)) - Number(isClosed(b.m)) || a.i - b.i)
    .slice(0, limit)
    .map((x) => x.m)
}
