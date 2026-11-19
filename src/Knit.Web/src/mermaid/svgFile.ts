// A diagram SVG file next to the doc that embeds it (`![x](flow.svg)`). Its
// meaning lives in attributes: `data-kind` (node / edge / group / label) with an
// `id`, a `<title>` per element, `data-from` / `data-to` on edges. Colors come
// from `k-*` class names, so the file reads in both themes; its own neutral
// presentation attributes are the fallback outside Knit.

import type { MermaidTheme } from './DiagramView'
import type { Hit, Pick } from './pointer'

type Role = 'node' | 'edge' | 'arrow' | 'group' | 'label'
const ROLES: Role[] = ['node', 'edge', 'arrow', 'group', 'label']

interface Palette {
  fill: string
  stroke: string
  text: string
  groupFill: string
  groupStroke: string
  edge: string
  muted: string
  accent: string
  accentFill: string
}

const PALETTE: Record<MermaidTheme, Palette> = {
  light: {
    fill: '#f4f4f5',
    stroke: '#71717a',
    text: '#18181b',
    groupFill: '#fafafa',
    groupStroke: '#a1a1aa',
    edge: '#52525b',
    muted: '#a1a1aa',
    accent: '#2563eb',
    accentFill: '#dbeafe',
  },
  dark: {
    fill: '#27272a',
    stroke: '#a1a1aa',
    text: '#f4f4f5',
    groupFill: '#18181b',
    groupStroke: '#52525b',
    edge: '#a1a1aa',
    muted: '#71717a',
    accent: '#60a5fa',
    accentFill: '#1e3a8a',
  },
}

const SHAPES = new Set(['rect', 'circle', 'ellipse', 'polygon', 'path', 'line', 'polyline'])
const LINES = new Set(['path', 'line', 'polyline'])

function classes(el: Element): string[] {
  return (el.getAttribute('class') ?? '').split(/\s+/)
}

/** The class names a theme colors; the skill lists the same set. */
export const THEME_CLASSES = [...ROLES.map((r) => `k-${r}`), 'k-muted', 'k-accent']

/**
 * Colors every element under a `k-*` class for `theme`, as inline style: it
 * beats the file's presentation attributes, and an export carries it along.
 * An element takes the nearest role and modifier above it.
 */
export function applySvgTheme(svg: SVGSVGElement, theme: MermaidTheme): void {
  const p = PALETTE[theme]
  for (const el of svg.querySelectorAll('*')) {
    let role: Role | undefined
    let muted = false
    let accent = false
    for (let e: Element | null = el; e && e !== svg; e = e.parentElement) {
      const c = classes(e)
      muted ||= c.includes('k-muted')
      accent ||= c.includes('k-accent')
      role ??= ROLES.find((r) => c.includes(`k-${r}`))
    }
    if (!role && !muted && !accent) continue
    const tag = el.tagName.toLowerCase()
    const line = accent ? p.accent : muted ? p.muted : undefined
    const style = (el as SVGElement).style
    if (tag === 'text' || tag === 'tspan') {
      // A node's or group's accent marks its frame; its text stays readable.
      style.fill = role === 'label' ? (line ?? p.text) : muted ? p.muted : role === 'group' ? p.muted : p.text
      continue
    }
    if (!SHAPES.has(tag)) continue
    switch (role) {
      case 'edge':
        if (LINES.has(tag)) {
          style.stroke = line ?? p.edge
          style.fill = 'none'
        } else style.fill = line ?? p.edge
        break
      case 'arrow':
        style.fill = line ?? p.edge
        style.stroke = 'none'
        break
      case 'group':
        style.fill = accent ? p.accentFill : p.groupFill
        style.stroke = line ?? p.groupStroke
        break
      case 'label':
        style.fill = 'none'
        style.stroke = 'none'
        break
      default:
        style.fill = accent ? p.accentFill : p.fill
        style.stroke = line ?? p.stroke
    }
  }
}

/** The nearest `data-kind` element with an id: what a click names. Undefined: the background. */
export function svgFileHit(target: Element, svg: Element): Hit | undefined {
  for (let el: Element | null = target; el && el !== svg; el = el.parentElement) {
    const kind = el.getAttribute('data-kind')
    const id = el.getAttribute('id')
    if (!kind || !id) continue
    const title = Array.from(el.children).find((c) => c.tagName.toLowerCase() === 'title')?.textContent?.trim()
    const pick: Pick = { kind, dslId: id }
    if (title) pick.label = title
    return { pick, el }
  }
  return undefined
}

/** `<ref>#<file>.svg/<kind>/<id>`, or the whole-diagram pointer `<ref>#<file>.svg`. */
export function formatSvgPointer(ref: string, file: string, pick?: Pick): string {
  const head = `${ref}#${file}`
  return pick?.dslId ? `${head}/${pick.kind}/${pick.dslId}` : head
}

/** `<ref>.<file>.<format>`, minus what a file name can't hold. */
export function svgExportName(docRef: string | undefined, file: string, format: string): string {
  const ref = (docRef ?? '').replace(/[^\w.-]+/g, '-').replace(/^[-.]+|[-.]+$/g, '') || 'diagram'
  return `${ref}.${file.replace(/\.svg$/i, '')}.${format}`
}
