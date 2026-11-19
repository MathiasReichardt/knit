// Forge heading anchors (github-slugger's rule), for a page written to be read
// on a forge: the generated status page's tables of contents link to these.
// Knit's own section anchors (lib/outline.ts) fold and cap differently, so a
// page using both would never line up. Mirrors GfmText in Knit.Data.

const KEEP = /[\p{L}\p{M}\p{Nd}\p{Nl}\p{Pc}-]/u

export function gfmAnchorBase(heading: string): string {
  let out = ''
  for (const ch of heading.toLowerCase()) {
    if (ch === ' ') out += '-'
    else if (KEEP.test(ch)) out += ch
  }
  return out
}

/** A repeat gets `-1`, `-2`, counted over every heading in the document, skipping a suffix an earlier heading took. */
export class GfmSlugger {
  private readonly seen = new Map<string, number>()

  next(heading: string): string {
    const original = gfmAnchorBase(heading)
    let slug = original
    while (this.seen.has(slug)) {
      const n = (this.seen.get(original) ?? 0) + 1
      this.seen.set(original, n)
      slug = `${original}-${n}`
    }
    this.seen.set(slug, 0)
    return slug
  }
}

/** Gives every h1–h6 its forge anchor, in document order. */
export function withGfmHeadingIds(html: string, slugger: GfmSlugger): string {
  const tpl = document.createElement('template')
  tpl.innerHTML = html
  for (const h of tpl.content.querySelectorAll('h1, h2, h3, h4, h5, h6'))
    h.id = slugger.next(h.textContent ?? '')
  return tpl.innerHTML
}
