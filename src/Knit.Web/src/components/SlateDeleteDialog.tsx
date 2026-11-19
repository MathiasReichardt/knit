import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { Trash2 } from 'lucide-react'

interface Props {
  name: string
  itemCount: number
  busy: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Confirm-before-remove for a registry slate. Removing it also clears it from
 * the items that carry it, so the count is stated up front.
 */
export function SlateDeleteDialog({ name, itemCount, busy, onConfirm, onCancel }: Props) {
  // Portalled and capture-phase so Escape closes only this, not Manage slates.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopImmediatePropagation()
      if (!busy) onCancel()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [busy, onCancel])

  const items = `${itemCount} item${itemCount === 1 ? '' : 's'}`
  return createPortal(
    <div
      className="fixed inset-0 z-50 bg-black/30 flex items-center justify-center p-4"
      onClick={busy ? undefined : onCancel}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="slate-delete-title"
        className="w-full max-w-md rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 id="slate-delete-title" className="flex items-center gap-2 text-sm font-semibold text-zinc-800 dark:text-zinc-100">
          <Trash2 className="h-4 w-4" aria-hidden />
          Remove slate <span className="font-mono">{name}</span>
        </h3>

        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          {itemCount > 0
            ? <>{items} carry this slate. Removing it clears it from them too.</>
            : <>No items carry this slate.</>}
        </p>

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
            className="text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            {itemCount > 0 ? `Remove and clear ${items}` : 'Remove slate'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
