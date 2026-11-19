import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fileApi, fileQk } from '../api/file'
import { DiagramView, type MermaidTheme, type Pointers } from '../mermaid/DiagramView'
import type { ExportModel } from '../mermaid/ExportMenu'
import { applySvgTheme, formatSvgPointer, svgExportName, svgFileHit } from '../mermaid/svgFile'
import { sanitizeSvg } from '../lib/sanitizeSvg'

const BACKGROUND: Record<MermaidTheme, string> = { light: '#ffffff', dark: '#09090b' }

function themed(svg: SVGSVGElement, theme: MermaidTheme): string {
  const copy = svg.cloneNode(true) as SVGSVGElement
  applySvgTheme(copy, theme)
  return copy.outerHTML
}

/**
 * An SVG file next to the doc, embedded with an image link on a line of its
 * own: sanitized, inlined and colored for the theme, with the diagram view's
 * pan, zoom, maximize and export. A click copies `<ref>#<file>.svg/<kind>/<id>`.
 */
export function SvgFileBlock({
  path,
  file,
  alt,
  theme,
  docRef,
  statusClassName = '',
  onPick,
  onNotify,
}: {
  /** Repo-relative path of the file. */
  path: string
  /** Its name, as the pointer names it. */
  file: string
  alt: string
  theme: MermaidTheme
  docRef?: string
  statusClassName?: string
  onPick?: (pointer: string, name: string) => void
  onNotify?: (kind: 'success' | 'error', message: string) => void
}) {
  const view = useQuery({ queryKey: fileQk.view(path), queryFn: () => fileApi.view(path), retry: false })
  const svg = useMemo(() => (view.data ? sanitizeSvg(view.data.content) : null), [view.data])
  const rendered = useMemo(() => (svg ? { svg: themed(svg, theme), diagramType: 'svg' } : null), [svg, theme])
  const pointers = useMemo<Pointers | undefined>(
    () =>
      docRef
        ? { resolve: svgFileHit, format: (pick) => formatSvgPointer(docRef, file, pick), name: alt || file }
        : undefined,
    [docRef, file, alt],
  )
  const exportImage = useMemo<ExportModel | undefined>(
    () =>
      svg
        ? {
            render: (t) => Promise.resolve(themed(svg, t)),
            background: BACKGROUND,
            fileName: (format) => svgExportName(docRef, file, format),
            notify: onNotify,
          }
        : undefined,
    [svg, docRef, file, onNotify],
  )

  const problem = view.error
    ? `Can't load ${file}: ${view.error instanceof Error ? view.error.message : String(view.error)}`
    : view.data?.truncated
      ? `${file} is too large to show`
      : view.data && !svg
        ? `${file} holds no SVG`
        : null
  if (problem) {
    return (
      <div className={`not-prose my-3 rounded border border-red-200 dark:border-red-800 bg-red-50 dark:bg-red-950/30 p-2 text-red-700 dark:text-red-300 ${statusClassName}`}>
        {problem}
      </div>
    )
  }
  if (!rendered) {
    return <div className={`not-prose my-3 italic text-zinc-500 ${statusClassName}`}>Loading {file}…</div>
  }
  return (
    <DiagramView
      rendered={rendered}
      theme={theme}
      docRef={docRef}
      fence={0}
      onPick={onPick}
      exportImage={exportImage}
      pointers={pointers}
    />
  )
}
