import { RefreshCw } from 'lucide-react'

interface Props {
  onReload: () => void
  onKeepMine: () => void
}

/**
 * Non-blocking "the doc moved under you" notice. Shown only when the
 * open editor is **dirty** and a newer revision arrived — a clean editor
 * re-seeds silently instead. Deliberately inline (not a modal): mid-edit it
 * must not hijack the screen the way the save-time {@link ConflictDialog} does.
 * "Keep mine" leaves the stale base in place, so a later Save still routes
 * through the conflict dialog.
 */
export function StaleBanner({ onReload, onKeepMine }: Props) {
  return (
    <div className="mb-2 flex items-center gap-2 rounded border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/40 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
      <RefreshCw className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span className="flex-1">
        This doc changed on disk (another tab, the CLI, or git) while you were
        editing. Your unsaved edits are kept.
      </span>
      <button
        type="button"
        onClick={onKeepMine}
        className="px-2 py-0.5 rounded border border-amber-300 dark:border-amber-800 hover:bg-amber-100 dark:hover:bg-amber-900/50"
      >
        Keep mine
      </button>
      <button
        type="button"
        onClick={onReload}
        className="px-2 py-0.5 rounded bg-amber-600 hover:bg-amber-700 text-white"
        title="Discard your edits and load the new version"
      >
        Reload
      </button>
    </div>
  )
}
