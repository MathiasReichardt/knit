import { useEffect, useRef } from 'react'
import { AlertTriangle } from 'lucide-react'

interface Props {
  shortId: string
  busy?: boolean
  onReload: () => void
  onOverwrite: () => void
  onKeepEditing: () => void
}

/**
 * Minimal 412 conflict dialog (M1c). The remote copy changed between
 * the time the user opened the editor and pressed Save. We don't show
 * a visual diff yet — that's deferred to M2 when multi-writer matters.
 *
 * This is a deliberate data-loss decision, so it's strictly modal: a
 * stray backdrop click does nothing, and the only keyboard escape is
 * Escape → Keep Editing (the safe choice). Overwrite is the sole
 * destructive action — it is never the default focus nor the Enter target.
 */
export function ConflictDialog({ shortId, busy, onReload, onOverwrite, onKeepEditing }: Props) {
  const keepRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    keepRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onKeepEditing()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onKeepEditing])

  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
    >
      <div className="w-full max-w-md rounded-md border border-amber-300 dark:border-amber-900 bg-white dark:bg-zinc-900 p-4 space-y-3">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-amber-700 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4" aria-hidden />
          Remote changed: {shortId}
        </h3>
        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          Someone else (or another tool — CLI, git pull, file edit) saved
          this item while you were editing. Pick one:
        </p>
        <ul className="text-xs text-zinc-600 dark:text-zinc-300 space-y-1.5 pl-1">
          <li>
            <span className="font-semibold">Reload</span> — discard your
            unsaved edits and load the remote version.
          </li>
          <li>
            <span className="font-semibold">Overwrite</span> — keep your
            version and replace remote (their changes are lost).
          </li>
          <li>
            <span className="font-semibold">Keep editing</span> — stay in the
            editor so you can copy out anything important before deciding.
          </li>
        </ul>
        <div className="flex flex-wrap items-center justify-end gap-2 pt-1">
          {/* Overwrite is the only data-loss action: least prominent (quiet
              danger ghost), pushed to the far left, never the default. */}
          <button
            type="button"
            disabled={busy}
            onClick={onOverwrite}
            className="mr-auto text-xs px-2 py-1 rounded border border-red-200 dark:border-red-900 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-950/40 disabled:opacity-50"
            title="Replace the remote version with your edits (their changes are lost)"
          >
            {busy ? 'Overwriting…' : 'Overwrite'}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onReload}
            className="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:bg-zinc-100 dark:hover:bg-zinc-900 disabled:opacity-50"
          >
            Reload
          </button>
          <button
            ref={keepRef}
            type="button"
            disabled={busy}
            onClick={onKeepEditing}
            className="text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            Keep editing
          </button>
        </div>
      </div>
    </div>
  )
}
