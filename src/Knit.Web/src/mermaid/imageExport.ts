import { unwrapLinks } from './links'
import { diagramName } from './pointer'

export type ImageFormat = 'png' | 'svg'

const PNG_SCALE = 2
// Browsers refuse to export bigger canvases; a huge diagram gets a smaller scale.
const MAX_SIDE = 16384
const MAX_AREA = 100_000_000

export interface StandaloneSvg {
  text: string
  width: number
  height: number
}

/**
 * Mermaid's SVG as a file of its own: a pixel size instead of `max-width`,
 * the background (none for transparent), well-formed XML, and no links.
 */
export function standaloneSvg(svg: string, background: string | undefined): StandaloneSvg {
  // The HTML parser: mermaid's HTML labels aren't always well-formed XML (`<br>`).
  const holder = document.createElement('div')
  holder.innerHTML = svg
  const el = holder.querySelector('svg')
  if (!el) throw new Error('No SVG in the rendered diagram')
  const [, , width = 0, height = 0] = (el.getAttribute('viewBox') ?? '').split(/[\s,]+/).map(Number)
  if (!(width > 0 && height > 0)) throw new Error('The rendered diagram has no size')
  el.setAttribute('width', String(width))
  el.setAttribute('height', String(height))
  el.style.removeProperty('max-width')
  unwrapLinks(el)
  // Parsed as HTML, a label's `xmlns` is a plain attribute: some serializers write it next to their own.
  for (const e of el.querySelectorAll('[xmlns]')) e.removeAttribute('xmlns')
  if (background) el.style.backgroundColor = background
  else el.style.removeProperty('background-color')
  return { text: new XMLSerializer().serializeToString(el), width, height }
}

export function pngScale(width: number, height: number) {
  return Math.min(PNG_SCALE, MAX_SIDE / width, MAX_SIDE / height, Math.sqrt(MAX_AREA / (width * height)))
}

export async function svgToPng({ text, width, height }: StandaloneSvg, background: string | undefined): Promise<Blob> {
  // A data: URL: Chromium taints the canvas for a blob: URL SVG with HTML labels (foreignObject).
  const img = new Image()
  img.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(text)}`
  await img.decode()
  const scale = pngScale(width, height)
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(width * scale)
  canvas.height = Math.round(height * scale)
  const g = canvas.getContext('2d')
  if (!g) throw new Error('No canvas to draw on')
  if (background) {
    g.fillStyle = background
    g.fillRect(0, 0, canvas.width, canvas.height)
  }
  g.drawImage(img, 0, 0, canvas.width, canvas.height)
  return new Promise((resolve, reject) => {
    try {
      canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('The browser made no PNG'))), 'image/png')
    } catch (e) {
      reject(e instanceof Error ? e : new Error(String(e)))
    }
  })
}

export const svgFile = (svg: StandaloneSvg) =>
  new Blob([`<?xml version="1.0" encoding="UTF-8"?>\n${svg.text}`], { type: 'image/svg+xml' })

/** Named like the block pointer (`<ref>#mermaid-N(<type>)`), minus what a file name can't hold. */
export function exportFileName(docRef: string | undefined, fence: number, diagramType: string, format: ImageFormat) {
  const ref = (docRef ?? '').replace(/[^\w.-]+/g, '-').replace(/^[-.]+|[-.]+$/g, '') || 'diagram'
  return `${ref}.mermaid-${fence}.${diagramName(diagramType)}.${format}`
}

export function download(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.append(a)
  a.click()
  a.remove()
  // The download starts from the click; keep the URL a while for slow browsers.
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
