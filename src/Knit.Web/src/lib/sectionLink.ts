import { SECTION_LINK_ATTR } from './markdown'

/** The section ref to copy when `target` is a heading's copy button, else null. */
export function sectionLinkAt(target: EventTarget | null, docRef: string | undefined): string | null {
  if (!(target instanceof Element) || !docRef) return null
  const id = target.closest(`[${SECTION_LINK_ATTR}]`)?.closest('h2, h3')?.id
  return id ? `${docRef}#${id}` : null
}

export const isSectionLinkClick = (target: EventTarget | null) =>
  target instanceof Element && target.closest(`[${SECTION_LINK_ATTR}]`) !== null

/**
 * Puts the heading being read in the address bar as `#<anchor>`, or clears it
 * above the first heading. Replaces the entry: scrolling isn't navigation, so
 * Back still leaves the doc.
 */
export function replaceUrlHash(id: string | null): void {
  const url = new URL(window.location.href)
  const next = id ? `#${id}` : ''
  if (url.hash === next) return
  url.hash = next
  window.history.replaceState(window.history.state, '', url)
}
