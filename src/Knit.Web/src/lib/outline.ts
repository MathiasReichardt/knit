import { slugify } from './slug'

export interface OutlineItem {
  /** Heading level — the outline covers h2 and h3 only. */
  depth: 2 | 3
  /** Raw heading text (inline markdown stripped of the leading `##`). */
  text: string
  /** Stable, deduped anchor id — matches the `id=` the renderer emits. */
  id: string
  /** 1-based source line of the heading in the body as given. */
  line: number
}

/**
 * Assigns deduped anchor ids to heading text in document order. The
 * markdown renderer (`lib/markdown.ts`) and `extractOutline` both drive a
 * slugger over the *same* heading sequence, so the rendered `id`s and the
 * outline can never drift — duplicate headings ("Goals", "Dependencies"
 * recur in design docs) get matching `-2` / `-3` suffixes on both sides.
 */
export class HeadingSlugger {
  private readonly seen = new Map<string, number>()

  slug(text: string): string {
    const base = slugify(text)
    const n = this.seen.get(base) ?? 0
    this.seen.set(base, n + 1)
    return n === 0 ? base : `${base}-${n + 1}`
  }
}

/**
 * Parse an h2–h3 heading outline from a markdown body in a single pass.
 * Skips a leading YAML frontmatter block and fenced code blocks (``` and
 * ~~~) so `#` lines inside code — including mermaid diagrams — are never
 * mistaken for headings. The returned ids match the renderer's anchors.
 */
export function extractOutline(body: string): OutlineItem[] {
  const full = (body ?? '').replace(/\r\n/g, '\n')
  const src = stripLeadingFrontmatter(full)
  let lineNo = full.slice(0, full.length - src.length).split('\n').length - 1
  const slugger = new HeadingSlugger()
  const items: OutlineItem[] = []
  let fenceChar: string | null = null

  for (const line of src.split('\n')) {
    lineNo++
    const fence = /^\s*(`{3,}|~{3,})/.exec(line)
    if (fence) {
      const ch = fence[1]![0]!
      if (fenceChar === null) fenceChar = ch
      else if (ch === fenceChar) fenceChar = null
      continue
    }
    if (fenceChar !== null) continue

    const m = /^(#{2,3})\s+(.+?)\s*#*\s*$/.exec(line)
    if (!m) continue
    const depth = m[1]!.length as 2 | 3
    const text = m[2]!.trim()
    if (text.length === 0) continue
    items.push({ depth, text, id: slugger.slug(text), line: lineNo })
  }

  return items
}

/**
 * Best-effort: the anchor id of the h2/h3 section a raw-body
 * character offset falls within, or null when the offset precedes the first
 * heading. Jump-to-occurrence uses it to land a body match near its section
 * — the raw→rendered offset map is otherwise fragile, so a near miss simply
 * degrades to a section-top flash. Drives the same heading sequence/slugger
 * as the renderer, so the returned id matches an emitted anchor. Offsets are
 * counted over the raw string (no `\r\n` normalization) to stay aligned with
 * the server, which scans the body verbatim.
 */
export function headingIdAtOffset(body: string, offset: number): string | null {
  const src = body ?? ''
  const slugger = new HeadingSlugger()
  let fenceChar: string | null = null
  let pos = 0
  let current: string | null = null

  for (const line of src.split('\n')) {
    const lineStart = pos
    pos += line.length + 1 // + the '\n' consumed by split

    const fence = /^\s*(`{3,}|~{3,})/.exec(line)
    if (fence) {
      const ch = fence[1]![0]!
      if (fenceChar === null) fenceChar = ch
      else if (ch === fenceChar) fenceChar = null
      continue
    }
    if (fenceChar !== null) continue

    const m = /^(#{2,3})\s+(.+?)\s*#*\s*$/.exec(line)
    if (!m) continue
    const text = m[2]!.trim()
    if (text.length === 0) continue

    const id = slugger.slug(text)
    if (lineStart <= offset) current = id
    else break
  }

  return current
}

function stripLeadingFrontmatter(input: string): string {
  if (!input.startsWith('---\n')) return input
  const end = input.indexOf('\n---', 3)
  return end >= 0 ? input.slice(end + 4) : input
}
