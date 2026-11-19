// Maps a click inside a rendered mermaid SVG back to the DSL element it drew.
// Everything comes from the DOM: mermaid serializes the SVG to a string, so no
// d3 `__data__` survives, and its parsed db is only reachable through the
// internal, deprecated `mermaidAPI`. The id schemes below were confirmed
// against mermaid 11.17's rendered output.

export interface Pick {
  kind: string
  dslId?: string
  label?: string
  from?: string
  to?: string
}

const MAX_LABEL = 80

const BREAK_TAGS = new Set(['br', 'tspan', 'text', 'p', 'div', 'span', 'li', 'foreignObject'])

/** Text of an element with line breaks (`<br>`, tspans, html blocks) joined by spaces. */
export function labelText(el: Element): string {
  const parts: string[] = []
  const walk = (n: Node) => {
    if (n.nodeType === Node.TEXT_NODE) {
      parts.push(n.textContent ?? '')
      return
    }
    if (n.nodeType !== Node.ELEMENT_NODE) return
    const tag = (n as Element).tagName
    if (tag === 'style' || tag === 'title') return
    const breaks = BREAK_TAGS.has(tag) || BREAK_TAGS.has(tag.toLowerCase())
    if (breaks) parts.push(' ')
    n.childNodes.forEach(walk)
    if (breaks) parts.push(' ')
  }
  walk(el)
  const text = parts.join('').replace(/\s+/g, ' ').trim()
  return text.length > MAX_LABEL ? `${text.slice(0, MAX_LABEL - 1)}…` : text
}

function hasClass(el: Element, cls: string): boolean {
  return (el.getAttribute('class') ?? '').split(/\s+/).includes(cls)
}

function firstClass(el: Element): string | undefined {
  return (el.getAttribute('class') ?? '').split(/\s+/).find(Boolean)
}

function byDataId(svg: Element, attr: string, value: string, sel = '[data-id]'): Element | undefined {
  return Array.from(svg.querySelectorAll(sel)).find((e) => e.getAttribute(attr) === value)
}

// Node ids are `<svgId>-<prefix><dslId>-<n>` for the unified-renderer types
// that number their nodes, `<svgId>-<prefix><dslId>` otherwise.
const NUMBERED_PREFIXES = ['flowchart-', 'classId-', 'state-', 'entity-']
const PLAIN_PREFIXES = ['service-', 'group-', 'junction-']
// Diagram types whose element ids are internal counters, not DSL ids.
const NO_DSL_IDS = new Set(['mindmap', 'timeline', 'sankey'])

function localId(el: Element, svgId: string): string | undefined {
  const id = el.getAttribute('id')
  if (!id || !id.startsWith(`${svgId}-`)) return undefined
  return id.slice(svgId.length + 1)
}

// An architecture junction draws an invisible `<rect id="<svgId>-node-<dslId>">`.
function isJunctionRect(el: Element): boolean {
  return el.tagName.toLowerCase() === 'rect' && !!el.parentElement?.closest('.architecture-junction')
}

function dslIdOf(local: string): string {
  for (const p of NUMBERED_PREFIXES) {
    if (local.startsWith(p)) {
      const id = local.slice(p.length).replace(/-\d+$/, '')
      return id.endsWith('_start') || id.endsWith('_end') ? '[*]' : id
    }
  }
  for (const p of PLAIN_PREFIXES) {
    if (local.startsWith(p)) return local.slice(p.length)
  }
  return local
}

interface NodeKey {
  raw: string
  dslId: string
}

function nodeKeys(svg: Element, svgId: string): NodeKey[] {
  const keys: NodeKey[] = []
  const shapes = 'g.node, g.cluster, .architecture-service, .architecture-junction, .architecture-junction rect'
  svg.querySelectorAll(shapes).forEach((n) => {
    const raw = localId(n, svgId)
    if (!raw) return
    const dslId = isJunctionRect(n) ? raw.replace(/^node-/, '') : dslIdOf(raw)
    keys.push({ raw, dslId })
    if (dslId !== raw) keys.push({ raw: dslId, dslId })
  })
  // Longest first, so `QA` wins over `Q` when both are node ids.
  return keys.sort((a, b) => b.raw.length - a.raw.length)
}

/**
 * Edge ids encode their endpoints — `L_F_S_0` (flowchart, architecture),
 * `id_Robot_Bobbin_1` (class), `id_entity-ROBOT-0_entity-PART-1_0` (ER),
 * `test_entity-test_req-0` (requirement), `<svgId>-1-a-b` (block). Ids may
 * themselves contain `_` / `-`, so the split is matched against the node ids
 * present in the same SVG rather than parsed.
 */
export function edgeEndpoints(edgeId: string, svg: Element, svgId: string): { from: string; to: string } | undefined {
  let rest = edgeId.startsWith(`${svgId}-`) ? edgeId.slice(svgId.length + 1) : edgeId
  rest = rest.replace(/^(L_|id_|L-)/, '').replace(/^\d+-/, '')
  const keys = nodeKeys(svg, svgId)
  for (const a of keys) {
    if (!rest.startsWith(a.raw)) continue
    const tail = rest.slice(a.raw.length + 1)
    if (!/^[_-]/.test(rest.slice(a.raw.length))) continue
    for (const b of keys) {
      if (tail === b.raw || (tail.startsWith(b.raw) && /^[_-]\d+$/.test(tail.slice(b.raw.length)))) {
        return { from: a.dslId, to: b.dslId }
      }
    }
  }
  return undefined
}

function edgePick(svg: Element, svgId: string, edgeId: string): Pick {
  const pick: Pick = { kind: 'edge' }
  const ends = edgeEndpoints(edgeId, svg, svgId)
  if (ends) Object.assign(pick, ends)
  const labelEl = byDataId(svg, 'data-id', edgeId, '.edgeLabel [data-id]')
  const label = labelEl ? labelText(labelEl) : ''
  if (label) pick.label = label
  return pick
}

function withLabel(pick: Pick, el: Element | null | undefined): Pick {
  const label = el ? labelText(el) : ''
  if (label) pick.label = label
  return pick
}

function participantPick(svg: Element, id: string): Pick {
  const box = byDataId(svg, 'data-id', id, '[data-et="participant"]')
  return withLabel({ kind: 'participant', dslId: id }, box?.querySelector('text') ?? box)
}

// The bottom actor boxes repeat the top ones without data attributes; match by name.
function bottomActorPick(svg: Element, el: Element): Pick | undefined {
  const box = el.tagName.toLowerCase() === 'g' ? el : el.parentElement
  const name = box?.querySelector('text') ? labelText(box.querySelector('text')!) : ''
  if (!name) return undefined
  const top = Array.from(svg.querySelectorAll('[data-et="participant"]')).find(
    (p) => labelText(p.querySelector('text') ?? p) === name,
  )
  const id = top?.getAttribute('data-id')
  return id ? participantPick(svg, id) : { kind: 'participant', label: name }
}

function sequencePick(svg: Element, el: Element): Pick | undefined {
  const et = el.getAttribute('data-et')
  const id = el.getAttribute('data-id') ?? undefined
  switch (et) {
    case 'participant':
    case 'life-line':
      return id ? participantPick(svg, id) : undefined
    case 'message': {
      const pick: Pick = { kind: 'message' }
      const from = el.getAttribute('data-from')
      const to = el.getAttribute('data-to')
      if (from) pick.from = from
      if (to) pick.to = to
      const text = el.previousElementSibling
      return withLabel(pick, text && hasClass(text, 'messageText') ? text : null)
    }
    case 'note':
      return withLabel({ kind: 'note' }, el)
    case 'control-structure':
      return withLabel(
        { kind: el.querySelector('.labelText')?.textContent?.trim() || 'block' },
        el.querySelector('.loopText'),
      )
  }
  return undefined
}

// In priority order: class name over members, ER / C4 name over attributes,
// a requirement's name over its `<<Requirement>>` stereotype line.
const LABEL_SELECTORS = ['.label-group', '.label.name', '.c4-name', '.nodeLabel', '.cluster-label']

function nodeLabelEl(node: Element): Element {
  for (const sel of [...LABEL_SELECTORS, '.label']) {
    const el = Array.from(node.querySelectorAll(sel)).find((l) => {
      const text = labelText(l)
      return text && !text.startsWith('<<')
    })
    if (el) return el
  }
  return node
}

function idPick(svg: Element, svgId: string, el: Element, diagramType: string): Pick | undefined {
  const local = localId(el, svgId)
  if (!local) return undefined
  const junction = isJunctionRect(el)
  let kind = 'node'
  // A block composite is a g.node around a rect.composite.
  if (hasClass(el, 'cluster') || hasClass(el, 'statediagram-cluster') || el.querySelector(':scope > .composite'))
    kind = 'cluster'
  else if (local.startsWith('service-')) kind = 'service'
  else if (local.startsWith('group-')) kind = 'group'
  else if (local.startsWith('junction-') || junction) kind = 'junction'
  else if (hasClass(el, 'task')) kind = 'task'
  const pick: Pick = { kind }
  const dslId = el.getAttribute('data-id') ?? (junction ? local.replace(/^node-/, '') : dslIdOf(local))
  if (!NO_DSL_IDS.has(diagramType) && !(kind === 'task' && /^task\d+$/.test(dslId))) pick.dslId = dslId
  if (kind === 'task') return withLabel(pick, svg.querySelector(`[id="${svgId}-${local}-text"]`))
  // A group's icon and title follow its rect as the next sibling.
  if (kind === 'group') return withLabel(pick, el.nextElementSibling?.querySelector('text'))
  return withLabel(pick, nodeLabelEl(el))
}

/** The element drawing the node with DSL id `dslId` (a flowchart / class / state node, a gantt task). */
export function nodeElFor(svg: Element, diagramType: string, dslId: string): Element | undefined {
  const svgId = svg.getAttribute('id') ?? ''
  return Array.from(svg.querySelectorAll('g.node, rect.task')).find(
    (el) => idPick(svg, svgId, el, diagramType)?.dslId === dslId,
  )
}

function isGroupRect(el: Element): boolean {
  return hasClass(el, 'node-bkg') && !!el.getAttribute('id')?.includes('-group-')
}

/** The shape a click resolves on: an architecture group's icon and title stand for its rect. */
function shapeOf(el: Element): Element {
  const prev = el.previousElementSibling
  return el.parentElement && hasClass(el.parentElement, 'architecture-groups') && prev && isGroupRect(prev) ? prev : el
}

function isIdShape(el: Element): boolean {
  return (
    hasClass(el, 'node') ||
    hasClass(el, 'cluster') ||
    hasClass(el, 'statediagram-cluster') ||
    hasClass(el, 'architecture-service') ||
    hasClass(el, 'architecture-junction') ||
    isGroupRect(el) ||
    (isJunctionRect(el) && !!el.getAttribute('id')) ||
    (hasClass(el, 'task') && el.tagName.toLowerCase() === 'rect')
  )
}

/** A resolved click: what it names, and the element that drew it (for hover / flash). */
export interface Hit {
  pick: Pick
  el: Element
}

/** Label-only tier: the clicked text (or the one text in its group) plus its class as the kind. */
function labelHit(svg: Element, target: Element): Hit | undefined {
  if (hasClass(target, 'pieCircle')) {
    const slices = Array.from(svg.querySelectorAll('path.pieCircle'))
    const legend = svg.querySelectorAll('g.legend')[slices.indexOf(target)]
    return { pick: withLabel({ kind: 'slice' }, legend?.querySelector('text')), el: target }
  }
  let textEl: Element | null = target.closest('text, foreignObject')
  if (!textEl && target.parentElement) {
    const texts = target.parentElement.querySelectorAll('text')
    if (texts.length === 1) textEl = texts[0]!
  }
  if (!textEl) return undefined
  const label = labelText(textEl)
  if (!label) return undefined
  let kind: string | undefined
  for (let e: Element | null = textEl; e && e !== svg && !kind; e = e.parentElement) kind = firstClass(e)
  return { pick: { kind: kind ?? 'text', label }, el: textEl }
}

function pickAt(svg: Element, svgId: string, el: Element, diagramType: string): Pick | undefined {
  const et = el.getAttribute('data-et')
  if (et === 'edge') return edgePick(svg, svgId, el.getAttribute('data-id') ?? '')
  if (et) {
    const pick = sequencePick(svg, el)
    if (pick) return pick
  }
  if (hasClass(el, 'taskText')) {
    const bar = svg.querySelector(`[id="${(el.getAttribute('id') ?? '').replace(/-text$/, '')}"]`)
    if (bar) return idPick(svg, svgId, bar, diagramType)
  }
  if (hasClass(el, 'actor-bottom') || el.querySelector(':scope > .actor-bottom')) {
    const pick = bottomActorPick(svg, el)
    if (pick) return pick
  }
  if (hasClass(el, 'messageText')) {
    const line = el.nextElementSibling
    if (line?.getAttribute('data-et') === 'message') return sequencePick(svg, line)
  }
  const edgeLabelId = el.parentElement && hasClass(el.parentElement, 'edgeLabel') ? el.getAttribute('data-id') : null
  if (edgeLabelId) return edgePick(svg, svgId, edgeLabelId)
  if (el.parentElement?.closest('.architecture-edges')) {
    const line = hasClass(el, 'arrow') ? el.parentElement.querySelector('.edge') : el
    const local = line && hasClass(line, 'edge') ? localId(line, svgId) : undefined
    if (local) return edgePick(svg, svgId, local)
  }
  const sets = el.getAttribute('data-venn-sets')
  if (sets) return withLabel({ kind: 'set', dslId: sets }, el.querySelector('text'))
  if (isIdShape(el)) return idPick(svg, svgId, el, diagramType)
  return undefined
}

/**
 * Resolves a click target inside `svg` (the rendered diagram root, whose `id`
 * prefixes every element id) to the element it drew. `undefined` means the
 * diagram background — the caller copies the block pointer alone.
 */
export function resolveHit(target: Element, svg: SVGSVGElement | Element, diagramType: string): Hit | undefined {
  const svgId = svg.getAttribute('id') ?? ''
  for (let el: Element | null = target; el && el !== svg; el = el.parentElement) {
    const shape = shapeOf(el)
    const pick = pickAt(svg, svgId, shape, diagramType)
    if (pick) return { pick, el: shape }
  }
  return labelHit(svg, target)
}

/** `flowchart-v2` → `flowchart`. */
export function diagramName(diagramType: string): string {
  return diagramType.replace(/-v\d+$/, '')
}

const quote = (s: string) => `"${s.replace(/[\\"]/g, '\\$&')}"`

// Mermaid ids are word characters, `.` and `-`; anything else (`[*]`) is quoted
// so it can't run into the label's brackets or the next path segment.
const id = (s: string) => (/^[\w.-]+$/.test(s) ? s : quote(s))

/**
 * One address token: `<ref>#mermaid-N/<kind>/<dsl-id>["<label>"]`, the label in
 * mermaid's own node syntax; edges and messages name their endpoints
 * (`edge/a->b`). Without a pick, the block pointer `<ref>#mermaid-N(<type>)`.
 */
export function formatPointer(ref: string, fence: number, diagramType: string, pick?: Pick): string {
  const head = `${ref}#mermaid-${fence}`
  if (!pick) return `${head}(${diagramName(diagramType)})`
  let s = `${head}/${pick.kind}`
  if (pick.dslId) s += `/${id(pick.dslId)}`
  if (pick.from && pick.to) s += `/${id(pick.from)}->${id(pick.to)}`
  if (pick.label && pick.label !== pick.dslId) s += `[${quote(pick.label)}]`
  return s
}
