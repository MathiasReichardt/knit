/**
 * A spot in a body shared by the rendered view and the editor: the 1-based
 * source line, an optional column, and the viewport y the spot sits at, so
 * switching between the two keeps it where the reader was looking.
 */
export interface EditPlace {
  line: number
  column?: number
  top: number
}

/** Where the view lands after the editor closes; waits until it renders `text` when given. */
export interface Landing extends EditPlace {
  text?: string
}

const lineOf = (el: Element) => Number(el.getAttribute('data-line'))

/**
 * The editor place for a double-click in a rendered body: the block holding
 * `target` (else the nearest block above it), refined to the line of the
 * selected word when the word occurs once in the block's source.
 */
export function editPlaceAt(container: HTMLElement, target: EventTarget | null, body: string): EditPlace | undefined {
  if (!(target instanceof Node)) return undefined
  const blocks = Array.from(container.querySelectorAll('[data-line]'))
  const el = target instanceof Element ? target : target.parentElement
  const inside = el?.closest('[data-line]')
  const block =
    inside && container.contains(inside)
      ? inside
      : blocks.filter((b) => b.compareDocumentPosition(target) & Node.DOCUMENT_POSITION_FOLLOWING).at(-1)
  if (!block) return undefined
  const line = lineOf(block)
  const word = wordIn(block)
  if (word) {
    const next = Math.min(...blocks.map(lineOf).filter((l) => l > line))
    const hit = uniqueWord(body, word.text, line, Number.isFinite(next) ? next : Infinity)
    if (hit) return { ...hit, top: word.top }
  }
  return { line, top: block.getBoundingClientRect().top }
}

function wordIn(block: Element): { text: string; top: number } | undefined {
  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0 || !sel.anchorNode || !block.contains(sel.anchorNode)) return undefined
  const text = sel.toString().trim()
  if (!/^[\p{L}\p{N}_]+$/u.test(text)) return undefined
  return { text, top: sel.getRangeAt(0).getBoundingClientRect().top }
}

/** The line and column of `word` in body lines [from, to) when it occurs there exactly once. */
export function uniqueWord(body: string, word: string, from: number, to: number): { line: number; column: number } | undefined {
  const lines = body.replace(/\r\n/g, '\n').split('\n')
  const re = new RegExp(`(?<![\\p{L}\\p{N}_])${word}(?![\\p{L}\\p{N}_])`, 'gu')
  let hit: { line: number; column: number } | undefined
  for (let n = from; n < Math.min(to, lines.length + 1); n++) {
    for (const m of lines[n - 1]!.matchAll(re)) {
      if (hit) return undefined
      hit = { line: n, column: m.index }
    }
  }
  return hit
}

/** The rendered block for a source line: the last block starting at or above it. */
export function blockForLine(container: HTMLElement, line: number): HTMLElement | undefined {
  let best: HTMLElement | undefined
  for (const el of container.querySelectorAll<HTMLElement>('[data-line]')) {
    if (lineOf(el) <= line && (!best || lineOf(el) >= lineOf(best))) best = el
  }
  return best
}

export function scrollParent(el: HTMLElement): HTMLElement {
  for (let p = el.parentElement; p; p = p.parentElement) {
    if (/(auto|scroll)/.test(getComputedStyle(p).overflowY)) return p
  }
  return (document.scrollingElement as HTMLElement | null) ?? document.documentElement
}

/** Scrolls the block for `place.line` to the viewport y `place.top`. */
export function alignLine(container: HTMLElement, place: EditPlace): void {
  const block = blockForLine(container, place.line)
  if (!block) return
  scrollParent(block).scrollTop += block.getBoundingClientRect().top - place.top
}

const HOLD_MS = 1500

/**
 * Aligns the place now and again whenever the body's size changes (a diagram
 * or image rendering late), for a short window or until the user scrolls.
 * Returns the cleanup; `done` fires once the hold ends on its own.
 */
export function holdLine(container: HTMLElement, place: EditPlace, done: () => void): () => void {
  alignLine(container, place)
  const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => alignLine(container, place))
  observer?.observe(container)
  const events = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const
  const finish = () => {
    cleanup()
    done()
  }
  const timer = setTimeout(finish, HOLD_MS)
  for (const ev of events) window.addEventListener(ev, finish, { capture: true, passive: true })
  function cleanup() {
    observer?.disconnect()
    clearTimeout(timer)
    for (const ev of events) window.removeEventListener(ev, finish, { capture: true })
  }
  return cleanup
}

const same = (a: string, b: string) => a.replace(/\r\n/g, '\n').trimEnd() === b.replace(/\r\n/g, '\n').trimEnd()

/** True once the view shows the text the landing waits for. */
export function landingReady(landing: Landing, body: string): boolean {
  return landing.text === undefined || same(landing.text, body)
}

/** The viewport y of the scroller's top edge — where a heading lands when scrolled to. */
export function scrollerTop(scroller: HTMLElement): number {
  return scroller === document.scrollingElement ? 0 : scroller.getBoundingClientRect().top
}

/** Scrolls `heading` to the top of its scroller and holds it there while late content renders. */
export function holdHeading(container: HTMLElement, heading: HTMLElement, done: () => void): () => void {
  const line = Number(heading.getAttribute('data-line'))
  if (!line) {
    heading.scrollIntoView({ block: 'start' })
    done()
    return () => {}
  }
  return holdLine(container, { line, top: scrollerTop(scrollParent(heading)) }, done)
}

// A heading counts as the one being read once its top is this close to the scroller's top.
const ACTIVE_SLACK_PX = 16

/** The id of the last h2/h3 (or `selector` match) at or above the scroller's top edge, or null above the first one. */
export function activeHeadingId(container: HTMLElement, scroller: HTMLElement, selector = 'h2[id], h3[id]'): string | null {
  const top = scrollerTop(scroller) + ACTIVE_SLACK_PX
  let active: string | null = null
  for (const h of container.querySelectorAll<HTMLElement>(selector)) {
    if (h.getBoundingClientRect().top > top) break
    active = h.id
  }
  return active
}
