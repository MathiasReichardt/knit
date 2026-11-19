import { DESIGN_REF_RE, isDocRef, KB_REF_RE, splitSection, type KbBucket } from './resolveLink'

// Classifies a pasted ref into one of the address spaces the jump-to-ref
// box handles. Disambiguation: `design:slug` ⇒ design; a
// `bucket:slug` with a known KB bucket head ⇒ KB; anything else is treated
// as an item id or short-id prefix. Resolution against the server (does it
// actually exist?) happens at the call site, mirroring checkLinkExists.

export type RefTarget =
  | { kind: 'kb'; bucket: KbBucket; slug: string }
  | { kind: 'design'; slug: string }
  | { kind: 'item'; id: string }

export function classifyRef(input: string): RefTarget | null {
  const s = input.trim()
  if (!s) return null
  const d = DESIGN_REF_RE.exec(s)
  if (d) return { kind: 'design', slug: d[1]! }
  const m = KB_REF_RE.exec(s)
  if (m) return { kind: 'kb', bucket: m[1] as KbBucket, slug: m[2]! }
  return { kind: 'item', id: s }
}

/**
 * A ref the box opens the way a body link is followed: one with a section, or
 * an `item:` / `project:` ref or `.knit/…` path, which the lookups above don't take.
 */
export function isLinkRef(input: string): boolean {
  const ref = input.trim()
  return isDocRef(ref) && (!!splitSection(ref).section || /^(item:|project:|\.knit\/)/.test(ref))
}
