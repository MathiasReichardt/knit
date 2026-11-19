/**
 * Jump-to-occurrence word highlighting. Rather than map the server's
 * raw-markdown offset onto the rendered DOM (fragile),
 * this re-finds the query tokens directly in the rendered text and paints them
 * with the CSS Custom Highlight API. That paints out-of-band ranges, so it
 * never mutates the React-managed DOM and can't be clobbered by a re-render.
 *
 * Matching mirrors the server scanner: tokens fold diacritics + case and match
 * as plain substrings (`hair` hits "hair", "haircut" and "mohair") — one
 * Ctrl+F-like rule shared end-to-end. The fold is length-preserving
 * (1 code unit → 1) so range offsets map back to the live text node unchanged.
 *
 * Where the API is unavailable (older engines) the caller falls back to a
 * region flash; this returns -1 to signal that.
 */

const HL_NAME = 'knit-search-hit'

interface HighlightRegistry {
  set(name: string, highlight: unknown): void
  delete(name: string): void
}

const HighlightCtor = (globalThis as unknown as {
  Highlight?: new (...ranges: Range[]) => unknown
}).Highlight

function registry(): HighlightRegistry | null {
  if (typeof CSS === 'undefined') return null
  const reg = (CSS as unknown as { highlights?: HighlightRegistry }).highlights
  return reg ?? null
}

const SUPPORTED = !!HighlightCtor && !!registry()

// Length-preserving fold: lowercase + common Latin-1 accents → base letter.
// Multi-char expansions (æ→ae) are deliberately skipped to keep offsets aligned.
const ACCENTS: Record<string, string> = {
  à: 'a', á: 'a', â: 'a', ã: 'a', ä: 'a', å: 'a',
  ç: 'c',
  è: 'e', é: 'e', ê: 'e', ë: 'e',
  ì: 'i', í: 'i', î: 'i', ï: 'i',
  ñ: 'n',
  ò: 'o', ó: 'o', ô: 'o', õ: 'o', ö: 'o', ø: 'o',
  ù: 'u', ú: 'u', û: 'u', ü: 'u',
  ý: 'y', ÿ: 'y',
}

function fold(s: string): string {
  let out = ''
  for (let i = 0; i < s.length; i++) {
    const lower = s[i]!.toLowerCase()
    out += ACCENTS[lower] ?? lower
  }
  return out
}

// Exported for unit tests — the folding + word-start matching is the part
// worth locking down; the DOM/Highlight-API glue can't run under jsdom.
export function tokenize(query: string): string[] {
  return fold(query)
    .split(/[^\p{L}\p{N}]+/u)
    .filter((t) => t.length > 0)
}

export function rangesInText(text: string, tokens: string[]): [number, number][] {
  const folded = fold(text)
  const hits: [number, number][] = []
  for (const tok of tokens) {
    let from = 0
    for (;;) {
      const i = folded.indexOf(tok, from)
      if (i < 0) break
      hits.push([i, i + tok.length])
      from = i + tok.length
    }
  }
  return hits
}

/**
 * Split `text` into plain/highlighted segments for inline rendering (the search
 * result list). Same matcher as the detail-pane highlight — `rangesInText`
 * — but the spans are merged + sorted into a non-overlapping cover so the caller
 * can map them straight to React nodes (no `dangerouslySetInnerHTML`). Returns a
 * single unmarked segment when nothing matches.
 */
export function highlightSegments(
  text: string,
  query: string,
): { text: string; mark: boolean }[] {
  const ranges = mergeRanges(rangesInText(text, tokenize(query)))
  if (ranges.length === 0) return [{ text, mark: false }]
  const out: { text: string; mark: boolean }[] = []
  let cursor = 0
  for (const [start, end] of ranges) {
    if (start > cursor) out.push({ text: text.slice(cursor, start), mark: false })
    out.push({ text: text.slice(start, end), mark: true })
    cursor = end
  }
  if (cursor < text.length) out.push({ text: text.slice(cursor), mark: false })
  return out
}

// Sort by start, then fuse overlapping/adjacent spans (different tokens can
// overlap — e.g. "cat"+"ate" in "category") into a clean left-to-right cover.
function mergeRanges(ranges: [number, number][]): [number, number][] {
  if (ranges.length <= 1) return ranges
  const sorted = [...ranges].sort((a, b) => a[0] - b[0])
  const out: [number, number][] = [sorted[0]!]
  for (let i = 1; i < sorted.length; i++) {
    const last = out[out.length - 1]!
    const cur = sorted[i]!
    if (cur[0] <= last[1]) last[1] = Math.max(last[1], cur[1])
    else out.push(cur)
  }
  return out
}

// Keepalive for the painted highlight. The body prose is rendered via
// `dangerouslySetInnerHTML`; a background refetch re-sets that innerHTML ~0.3s
// after a jump, detaching the text nodes our ranges point at — the highlight
// silently stops painting (the same re-render that would kill the heading flash).
// We watch the region and re-run the match after each such re-render. Only one
// highlight is ever active (a single global registry entry), so module-level
// singleton state is correct.
let keepAlive: { obs: MutationObserver } | null = null

// Active-highlight state surfaced to the UI for an explicit "clear" affordance.
// The lib stays React-free; the chip subscribes via
// useSyncExternalStore over `subscribeHighlight` + the scalar getters.
let active = false
let activeQuery = ''
const listeners = new Set<() => void>()

function setActive(next: boolean, query = ''): void {
  const nextQuery = next ? query : ''
  if (active === next && activeQuery === nextQuery) return
  active = next
  activeQuery = nextQuery
  for (const l of listeners) l()
}

export function subscribeHighlight(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** Whether a search-jump highlight is currently painted. */
export function isHighlightActive(): boolean {
  return active
}

/** The query backing the active highlight (empty when none). */
export function highlightQuery(): string {
  return activeQuery
}

// Clear only the painted ranges, not the keepalive observer — called by
// highlightMatches on every (re-)paint, so it must not tear down its own
// re-apply loop.
function clearPaint(): void {
  registry()?.delete(HL_NAME)
}

/** Drop any active highlight + its keepalive (no-op where the API is unavailable). */
export function clearSearchHighlight(): void {
  if (keepAlive) {
    keepAlive.obs.disconnect()
    keepAlive = null
  }
  clearPaint()
  setActive(false)
}

/**
 * Paint the query matches in `region` and keep them painted across re-renders.
 * Returns the initial match count (see highlightMatches). When the
 * API is unavailable (-1) or nothing matched (0) no observer is installed — the
 * caller's region flash already owns that case. The highlight persists until
 * `clearSearchHighlight()` (pane unmount, or an explicit clear).
 */
export function paintAndKeep(region: HTMLElement, query: string): number {
  // Replace any prior keepalive — a new jump supersedes the old highlight.
  if (keepAlive) {
    keepAlive.obs.disconnect()
    keepAlive = null
  }
  const count = highlightMatches(region, query, false)
  if (count <= 0) {
    setActive(false)
    return count
  }

  // Re-running highlightMatches paints out-of-band (no DOM mutation), so it
  // can't re-trigger this observer — no feedback loop.
  const obs = new MutationObserver(() => {
    highlightMatches(region, query, false)
  })
  obs.observe(region, { childList: true, subtree: true, characterData: true })
  keepAlive = { obs }
  setActive(true, query)
  return count
}

/**
 * Highlight every word-start match of `query` within `root` and return the
 * match count. Returns -1 when the highlight API is unavailable, 0 when
 * supported but nothing matched. When `scrollToFirst` is true the first match
 * is scrolled into view; pass false when a section flash owns the scroll.
 */
export function highlightMatches(root: HTMLElement, query: string, scrollToFirst = true): number {
  if (!SUPPORTED) return -1
  clearPaint()
  const tokens = tokenize(query)
  if (tokens.length === 0) return 0

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  const ranges: Range[] = []
  let first: Range | null = null
  let node: Node | null
  while ((node = walker.nextNode())) {
    const tag = node.parentElement?.tagName
    if (tag === 'SCRIPT' || tag === 'STYLE') continue
    const text = node.nodeValue ?? ''
    if (text.length === 0) continue
    for (const [start, end] of rangesInText(text, tokens)) {
      const range = document.createRange()
      range.setStart(node, start)
      range.setEnd(node, end)
      ranges.push(range)
      first ??= range
    }
  }
  if (ranges.length === 0) return 0

  registry()!.set(HL_NAME, new HighlightCtor!(...ranges))
  if (scrollToFirst) first?.startContainer.parentElement?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  return ranges.length
}
