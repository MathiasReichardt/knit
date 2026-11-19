import { scrollAndFlash } from './headingFlash'
import { headingIdAtOffset } from './outline'
import { paintAndKeep } from './searchHighlight'
import type { JumpIntent } from '../stores/jumpIntent'

interface Regions {
  bodyRegionId: string
  /** Items only; designs/KB have no comments region. */
  commentsRegionId?: string
  /** The frontmatter title element, flashed for a title-field hit. */
  titleRegionId?: string
}

/**
 * Land on a search pick's matched region, shared by the item / design /
 * KB detail panes. Always flashes the target section — the matched body offset
 * snapped to its heading, else the whole region — so the jump is obvious, and
 * additionally paints the matched words (the flash owns the scroll, so the word
 * highlight does not fight it). A title hit flashes the frontmatter title;
 * other non-body/comment fields are a no-op (the entity opened title-first).
 */
export function jumpToOccurrence(root: HTMLElement, body: string, intent: JumpIntent, regions: Regions): void {
  if (intent.field === 'title') {
    if (!regions.titleRegionId) return
    const titleEl = root.querySelector<HTMLElement>(`#${CSS.escape(regions.titleRegionId)}`)
    if (!titleEl) return
    paintAndKeep(titleEl, intent.query)
    scrollAndFlash(root, regions.titleRegionId)
    return
  }
  if (intent.field !== 'body' && intent.field !== 'comment') return
  const regionId = intent.field === 'comment' ? regions.commentsRegionId : regions.bodyRegionId
  if (!regionId) return
  const region = root.querySelector<HTMLElement>(`#${CSS.escape(regionId)}`)
  if (!region) return

  // Highlight the words for precision, but let the flash drive the scroll. Keep
  // it alive across the body re-render that would otherwise wipe it.
  paintAndKeep(region, intent.query)

  // Flash the section heading the offset falls in (best-effort), else the region.
  let flashId = regionId
  if (intent.field === 'body' && intent.bodyOffset != null) {
    const headingId = headingIdAtOffset(body, intent.bodyOffset)
    if (headingId && root.querySelector(`#${CSS.escape(headingId)}`)) flashId = headingId
  }
  scrollAndFlash(root, flashId)
}
