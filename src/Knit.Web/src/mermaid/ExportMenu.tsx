import { useState } from 'react'
import { Copy, Download, ImageDown } from 'lucide-react'
import type { MermaidTheme } from './DiagramView'
import { download, standaloneSvg, svgFile, svgToPng, type ImageFormat } from './imageExport'
import { CHIP, CHIP_ON } from './LayoutMenu'
import { Popover } from './Popover'

export interface ExportModel {
  /** The fence rendered in `theme`: mermaid's SVG. */
  render: (theme: MermaidTheme) => Promise<string>
  /** Each theme's page color, the image background unless transparent. */
  background: Record<MermaidTheme, string>
  fileName: (format: ImageFormat) => string
  notify?: (kind: 'success' | 'error', message: string) => void
}

const FORMATS: { value: ImageFormat; label: string }[] = [
  { value: 'png', label: 'PNG' },
  { value: 'svg', label: 'SVG' },
]
const THEMES: { value: MermaidTheme; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

const ACTION =
  'inline-flex items-center gap-1 rounded border border-zinc-200 dark:border-zinc-700 px-2 py-1 ' +
  'hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-50 disabled:hover:bg-transparent'

/** The export button and its settings popover. */
export function ExportMenu({
  model,
  theme,
  open,
  onOpenChange,
  buttonClassName,
}: {
  model: ExportModel
  /** The SPA's theme, preselected. */
  theme: MermaidTheme
  open: boolean
  onOpenChange: (open: boolean) => void
  buttonClassName: string
}) {
  return (
    <Popover
      label="Export image"
      icon={<ImageDown className="h-3.5 w-3.5" />}
      open={open}
      onOpenChange={onOpenChange}
      buttonClassName={buttonClassName}
      width="w-64"
    >
      {/* Mounted per open: the settings start from the defaults each time. */}
      <ExportForm model={model} theme={theme} onDone={() => onOpenChange(false)} />
    </Popover>
  )
}

function Choices<T extends string>({
  label,
  choices,
  value,
  onChange,
}: {
  label: string
  choices: { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
}) {
  return (
    <>
      <span>{label}</span>
      <div role="group" aria-label={label} className="flex gap-1">
        {choices.map((c) => (
          <button
            key={c.value}
            type="button"
            aria-pressed={c.value === value}
            className={`${CHIP} ${c.value === value ? CHIP_ON : ''}`}
            onClick={() => onChange(c.value)}
          >
            {c.label}
          </button>
        ))}
      </div>
    </>
  )
}

function ExportForm({ model, theme: initial, onDone }: { model: ExportModel; theme: MermaidTheme; onDone: () => void }) {
  const [format, setFormat] = useState<ImageFormat>('png')
  const [theme, setTheme] = useState(initial)
  const [transparent, setTransparent] = useState(false)
  const [busy, setBusy] = useState(false)

  const run = async (copy: boolean) => {
    setBusy(true)
    const background = transparent ? undefined : model.background[theme]
    const file = model.render(theme).then((svg) => {
      const s = standaloneSvg(svg, background)
      return format === 'svg' ? svgFile(s) : svgToPng(s, background)
    })
    // Reported through the write or the download below.
    file.catch(() => undefined)
    try {
      // The write starts in the click, with the PNG still to come: Safari needs the user gesture.
      if (copy) await navigator.clipboard.write([new ClipboardItem({ 'image/png': file })])
      else download(await file, model.fileName(format))
      if (copy) model.notify?.('success', 'Diagram copied as PNG')
      onDone()
    } catch (e) {
      model.notify?.('error', `Diagram not exported: ${e instanceof Error ? e.message : String(e)}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <fieldset disabled={busy} className="grid grid-cols-[auto_1fr] items-center gap-x-3 gap-y-1.5">
      <legend className="mb-1.5 font-semibold">Export image</legend>
      <Choices label="Format" choices={FORMATS} value={format} onChange={setFormat} />
      <Choices label="Theme" choices={THEMES} value={theme} onChange={setTheme} />
      <label className="col-start-2 inline-flex items-center gap-1.5">
        <input type="checkbox" checked={transparent} onChange={(e) => setTransparent(e.target.checked)} />
        Transparent background
      </label>
      <div className="col-span-2 mt-1 flex justify-end gap-1.5">
        {format === 'png' && (
          <button type="button" className={ACTION} onClick={() => void run(true)}>
            <Copy className="h-3 w-3" />
            Copy
          </button>
        )}
        <button type="button" className={ACTION} onClick={() => void run(false)}>
          <Download className="h-3 w-3" />
          Download
        </button>
      </div>
    </fieldset>
  )
}
