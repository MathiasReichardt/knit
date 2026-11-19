// Edges and messages are 1–2 px strokes, and SVG hit-tests only painted
// pixels. Each gets an invisible wide copy right after it; picking maps the copy
// back to the original, so the resolver and the visible highlight never see it.

export const HIT_CLASS = 'mermaid-edge-hit'

const LINES =
  '[data-et="edge"], [data-et="message"], .architecture-edges .edge, ' +
  '[data-kind="edge"]:is(path, line, polyline), [data-kind="edge"] :is(path, line, polyline)'
const GEOMETRY_TAGS = new Set(['path', 'line', 'polyline'])
const GEOMETRY_ATTRS = ['d', 'x1', 'y1', 'x2', 'y2', 'points', 'transform']
// Inline, so mermaid's own `#<svgId> …` rules can't restyle the copy, and no
// marker inherited from a parent `<g>` draws on it at the wide stroke's scale.
const HIT_STYLE =
  'stroke: transparent; stroke-width: 12px; fill: none; marker: none; pointer-events: stroke; ' +
  'vector-effect: non-scaling-stroke'

export function addEdgeHitAreas(svg: Element) {
  svg.querySelectorAll(LINES).forEach((line) => {
    const tag = line.tagName.toLowerCase()
    if (!GEOMETRY_TAGS.has(tag) || line.nextElementSibling?.classList.contains(HIT_CLASS)) return
    const hit = document.createElementNS('http://www.w3.org/2000/svg', tag)
    for (const a of GEOMETRY_ATTRS) {
      const v = line.getAttribute(a)
      if (v !== null) hit.setAttribute(a, v)
    }
    hit.setAttribute('class', HIT_CLASS)
    hit.setAttribute('style', HIT_STYLE)
    line.after(hit)
  })
}

/** The element a pointer event really targets: a hit copy stands for the line before it. */
export function pickTarget(target: Element): Element {
  return target.classList.contains(HIT_CLASS) ? (target.previousElementSibling ?? target) : target
}
