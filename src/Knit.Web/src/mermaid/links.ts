/** A node's link, read from the fence: `click <id> [href] "<target>"` or `link <id> "<target>"`. */
export interface DiagramLink {
  id: string
  target: string
}

// Mirrors RepoVerifier.MermaidLinkRe: a callback `click` has no quoted target.
const LINK_LINE_RE = /^\s*(?:click\s+(\S+)\s+(?:href\s+)?|link\s+(\S+)\s+)"([^"]+)"/

/**
 * The link lines of a fence, from the source rather than mermaid's rendered
 * `<a>`: each diagram type wraps links its own way, and gantt draws none.
 */
export function parseLinks(code: string): DiagramLink[] {
  const links: DiagramLink[] = []
  for (const line of code.split('\n')) {
    const m = LINK_LINE_RE.exec(line)
    if (!m) continue
    const target = m[3]!
    for (const id of (m[1] ?? m[2])!.split(',')) {
      if (id) links.push({ id, target })
    }
  }
  return links
}

/**
 * Replaces each mermaid `<a>` with a plain `<g>`: a ref is meaningless to the
 * browser (and outside the app), so neither the view nor an export links.
 * The `<g>` keeps the `<a>`'s attributes — mermaid positions a linked node by
 * the `<a>`'s transform.
 */
export function unwrapLinks(svg: Element): void {
  for (const a of Array.from(svg.querySelectorAll('a'))) {
    const g = a.ownerDocument.createElementNS('http://www.w3.org/2000/svg', 'g')
    for (const attr of Array.from(a.attributes)) {
      if (!/^(xlink:)?href$|^target$/.test(attr.name)) g.setAttribute(attr.name, attr.value)
    }
    g.append(...Array.from(a.childNodes))
    a.replaceWith(g)
  }
}
