import { Archive } from 'lucide-react'

interface Props {
  slug: string
  busy: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Confirm-before-archive for an active design. Archiving moves the file into
 * `completed/<slug>/` and flips its `completed` flag — a single, reversible
 * filesystem move, so the dialog just states the target and confirms.
 */
export function DesignArchiveDialog({ slug, busy, onConfirm, onCancel }: Props) {
  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={busy ? undefined : onCancel}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="flex items-center gap-2 text-sm font-semibold text-zinc-800 dark:text-zinc-100">
          <Archive className="h-4 w-4" aria-hidden />
          Archive {slug}
        </h3>

        <div className="space-y-2 text-xs text-zinc-600 dark:text-zinc-300">
          <p>
            Archive this design? It moves into{' '}
            <span className="font-mono">completed/{slug}/</span> and is flagged{' '}
            <span className="font-medium">completed</span>.
          </p>
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            disabled={busy}
            onClick={onCancel}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            title="Move into completed/<slug>/ and flag completed"
            className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            <Archive className="h-3.5 w-3.5" aria-hidden />
            {busy ? 'Archiving…' : 'Archive'}
          </button>
        </div>
      </div>
    </div>
  )
}
