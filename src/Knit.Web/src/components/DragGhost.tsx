import { useEffect, useState, type ReactNode } from 'react'
import { useDndContext, type UniqueIdentifier } from '@dnd-kit/core'
import { Ban } from 'lucide-react'
import { useDropVerdict } from '../hooks/useDropVerdict'

/**
 * The floating ghost under the cursor while a row is being dragged. One shell
 * for every tree so the drag reads the same everywhere: an icon plus the name
 * of what is in hand, in a frame that says whether releasing here will land it.
 * A drop that would not land goes dashed and dimmed with a `Ban` badge; one
 * the tree's `DropCheckContext` refuses also says why and how to fix it, once
 * the pointer has rested on that zone for {@link EXPLAIN_DELAY_MS} — sweeping
 * across rows stays quiet.
 *
 * Offset down-right of the drag origin: dnd-kit parks the overlay on the grip
 * handle the drag started from, which puts it straight under the cursor, and
 * the mouse pointer then hides the icon that says what is being dragged.
 *
 * Render it inside a `DragOverlay`: it reads the enclosing `DndContext`.
 */
const EXPLAIN_DELAY_MS = 1000

export function DragGhost({
  count = 1,
  label,
  labelClassName = '',
  children,
}: {
  /** Dragged row count; the badge appears from 2 up. */
  count?: number
  /** What is in hand — title, name or ref. Also the ghost's tooltip. */
  label: string
  labelClassName?: string
  /** The type icon, sized `h-4 w-4`. */
  children: ReactNode
}) {
  const verdict = useDropVerdict()
  const rejected = verdict !== null && verdict.kind !== 'ok'
  const overId = useDndContext().over?.id ?? null
  const refused = verdict?.kind === 'refused'
  const [explainedFor, setExplainedFor] = useState<UniqueIdentifier | null>(null)
  useEffect(() => {
    if (!refused) return
    const timer = window.setTimeout(() => setExplainedFor(overId), EXPLAIN_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [refused, overId])
  const explain = refused && explainedFor === overId
  // `w-max`: the overlay is only as wide as the grip the drag started from,
  // which would wrap the explanation to a word per line.
  return (
    <div className="pointer-events-none flex w-max translate-x-4 translate-y-4 flex-col items-start gap-1">
      <div
        title={label}
        className={
          'relative inline-flex items-center gap-1.5 rounded border bg-white px-2 py-1 shadow-md dark:bg-zinc-950 ' +
          (rejected
            ? 'border-dashed border-zinc-400 opacity-60 dark:border-zinc-600'
            : 'border-blue-500')
        }
      >
        {children}
        <span className={`max-w-[16rem] truncate text-sm ${labelClassName}`}>{label}</span>
        {count > 1 && (
          <span
            className={
              'absolute -top-1.5 -right-1.5 inline-flex h-4 min-w-[1rem] items-center justify-center rounded-full px-1 text-[10px] leading-none text-white ' +
              (rejected ? 'bg-zinc-500' : 'bg-blue-600')
            }
          >
            {count}
          </span>
        )}
        {rejected && (
          <Ban
            aria-hidden
            className="absolute -bottom-1.5 -left-1.5 h-4 w-4 rounded-full bg-white text-zinc-500 dark:bg-zinc-950"
          />
        )}
      </div>
      {explain && verdict.kind === 'refused' && (
        <div className="max-w-[28rem] rounded border border-zinc-200 bg-white px-2 py-1 text-xs shadow-md dark:border-zinc-700 dark:bg-zinc-950">
          <div className="text-zinc-800 dark:text-zinc-200">{verdict.reason}</div>
          {verdict.hint && <div className="text-zinc-600 dark:text-zinc-400">{verdict.hint}</div>}
        </div>
      )}
    </div>
  )
}
