import { useEffect, useMemo, useRef, useState } from 'react'
import { DiagramView, type MermaidTheme, type Rendered } from './DiagramView'
import type { ExportModel } from './ExportMenu'
import { withoutFrontmatter, type Scalar } from './frontmatter'
import { exportFileName } from './imageExport'
import { layoutOptions, optionKey, readOption, writeOption, type LayoutOption } from './layoutOptions'
import { parseLinks } from './links'
import type { LayoutModel } from './LayoutMenu'

export type { MermaidTheme }
/** Mermaid's `themeVariables` for the `base` theme (colors, `fontFamily`, `darkMode`). */
export type MermaidThemeVariables = Record<string, string | boolean>

let mermaidModule: Promise<typeof import('mermaid').default> | null = null
let counter = 0
let queue: Promise<unknown> = Promise.resolve()

function loadMermaid() {
  mermaidModule ??= import('mermaid').then((mod) => mod.default)
  return mermaidModule
}

/**
 * One render at a time: `initialize` is global, and an export renders in the
 * other theme while the page's diagrams may be rendering in theirs.
 */
function renderMermaid(code: string, themeVariables: MermaidThemeVariables): Promise<Rendered> {
  const run = queue.then(async () => {
    const m = await loadMermaid()
    // Mermaid sizes boxes from measured label text: measure in the final font.
    await document.fonts.ready
    m.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'base',
      themeVariables,
    })
    // A fresh id per render: mermaid removes any element that already has the id,
    // which would take the diagram on screen with it.
    const { svg, diagramType } = await m.render(`mermaid-${++counter}`, code)
    return { svg, diagramType }
  })
  queue = run.catch(() => undefined)
  return run
}

const at = (config: unknown, path: string[]): unknown =>
  path.reduce<unknown>((o, k) => (o && typeof o === 'object' ? (o as Record<string, unknown>)[k] : undefined), config)

interface MermaidBlockProps {
  code: string
  theme: MermaidTheme
  /** Per theme; keep the references stable: a new object re-renders the diagram. */
  themeVariables: Record<MermaidTheme, MermaidThemeVariables>
  /** Font-size class for the status lines (error box, "Rendering…"). */
  statusClassName?: string
  /** The host doc's ref; with it and `onPick`, a click copies a pointer to the clicked element. */
  docRef?: string
  /** 1-based position among the body's mermaid fences. */
  fence?: number
  /** Receives the pointer and a short name of what was clicked, for the host's feedback. */
  onPick?: (pointer: string, name: string) => void
  /** Saves the fence's new code (a layout menu edit, rendered successfully first). Without it there's no menu. */
  onEditCode?: (code: string) => void | Promise<void>
  /** A layout edit that didn't render; nothing was saved. */
  onEditError?: (message: string) => void
  /** Feedback on an image export. Without it, an export still works but reports nothing. */
  onNotify?: (kind: 'success' | 'error', message: string) => void
  /** Whether the host can open a node link's target; keep it stable. */
  canFollow?: (target: string) => boolean
  /** Opens a node link's target, from its badge. */
  onFollow?: (target: string) => void
  /** Copies a node link's target the host can't open. */
  onCopy?: (target: string) => void
}

export function MermaidBlock({
  code,
  theme,
  themeVariables: themes,
  statusClassName = '',
  docRef,
  fence = 1,
  onPick,
  onEditCode,
  onEditError,
  onNotify,
  canFollow,
  onFollow,
  onCopy,
}: MermaidBlockProps) {
  const themeVariables = themes[theme]
  const [rendered, setRendered] = useState<Rendered | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [directiveConfig, setDirectiveConfig] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  // A layout edit's render, reused once the saved code comes back as `code`.
  const prerendered = useRef<{ code: string; themeVariables: MermaidThemeVariables; rendered: Rendered } | null>(null)

  useEffect(() => {
    let cancelled = false
    // The previous diagram stays up until the new one is ready.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setErr(null)
    const pre = prerendered.current
    prerendered.current = null
    const done =
      pre && pre.code === code && pre.themeVariables === themeVariables
        ? Promise.resolve(pre.rendered)
        : renderMermaid(code, themeVariables)
    done
      .then(({ svg, diagramType }) => {
        if (!cancelled) setRendered({ svg, diagramType })
      })
      .catch((e: unknown) => {
        if (!cancelled) setErr(e instanceof Error ? e.message : String(e))
      })
    // A legacy %%{init}%% directive wins over frontmatter; the menu shows what it sets.
    if (code.includes('%%{')) {
      loadMermaid()
        .then((m) => m.parse(withoutFrontmatter(code)))
        .then((r) => {
          if (!cancelled) setDirectiveConfig(r ? r.config : null)
        })
        .catch(() => {
          if (!cancelled) setDirectiveConfig(null)
        })
    } else {
      setDirectiveConfig(null)
    }
    return () => {
      cancelled = true
    }
  }, [code, themeVariables])

  const options = useMemo(() => (rendered ? layoutOptions(rendered.diagramType) : []), [rendered])
  const layout = useMemo<LayoutModel | undefined>(() => {
    if (!onEditCode || options.length === 0) return undefined
    const values: LayoutModel['values'] = {}
    const directive: LayoutModel['directive'] = {}
    for (const o of options) {
      values[optionKey(o)] = readOption(code, o)
      const d = o.direction ? undefined : at(directiveConfig, o.path)
      if (typeof d === 'string' || typeof d === 'number' || typeof d === 'boolean') directive[optionKey(o)] = d
    }
    const apply = async (o: LayoutOption, value: Scalar | undefined) => {
      const next = writeOption(code, o, value)
      if (next === null || next === code) return
      setBusy(true)
      try {
        let r: Rendered
        try {
          r = await renderMermaid(next, themeVariables)
        } catch (e) {
          onEditError?.(e instanceof Error ? e.message : String(e))
          return
        }
        prerendered.current = { code: next, themeVariables, rendered: r }
        await onEditCode(next)
      } finally {
        setBusy(false)
      }
    }
    return { options, values, directive, busy, apply }
  }, [code, options, directiveConfig, busy, onEditCode, onEditError, themeVariables])

  const links = useMemo(() => parseLinks(code), [code])
  const diagramType = rendered?.diagramType
  const exportImage = useMemo<ExportModel | undefined>(
    () =>
      diagramType === undefined
        ? undefined
        : {
            render: (t) => renderMermaid(code, themes[t]).then((r) => r.svg),
            background: { light: String(themes.light.background), dark: String(themes.dark.background) },
            fileName: (format) => exportFileName(docRef, fence, diagramType, format),
            notify: onNotify,
          },
    [code, themes, docRef, fence, diagramType, onNotify],
  )

  if (err) {
    return (
      <div className={`not-prose my-3 rounded border border-red-200 dark:border-red-800 bg-red-50 dark:bg-red-950/30 p-2 ${statusClassName}`}>
        <p className="font-semibold text-red-700 dark:text-red-300 mb-1">
          Mermaid render error
        </p>
        <pre className="whitespace-pre-wrap text-red-800 dark:text-red-200 font-mono">
          {err}
        </pre>
        <details className="mt-1">
          <summary className="cursor-pointer text-red-600 dark:text-red-400">
            Source
          </summary>
          <pre className="mt-1 whitespace-pre-wrap font-mono text-zinc-700 dark:text-zinc-300">
            {code}
          </pre>
        </details>
      </div>
    )
  }
  if (!rendered) {
    return (
      <div className={`not-prose my-3 italic text-zinc-500 ${statusClassName}`}>
        Rendering diagram…
      </div>
    )
  }
  return (
    <DiagramView
      rendered={rendered}
      theme={theme}
      docRef={docRef}
      fence={fence}
      onPick={onPick}
      layout={layout}
      exportImage={exportImage}
      links={links}
      canFollow={canFollow}
      onFollow={onFollow}
      onCopy={onCopy}
    />
  )
}
