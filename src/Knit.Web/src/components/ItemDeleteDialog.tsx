import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, api, qk } from '../api/client'
import { useToasts } from '../stores/toasts'

// Shared hard-delete confirm for a single item. Owns the etag round-trip,
// the cascade choice, and the query-cache cleanup; the caller decides what
// happens after (e.g. clear the selected id) via onDeleted.
export function ItemDeleteDialog({
  item,
  onClose,
  onDeleted,
}: {
  item: { id: string; shortId: string; title: string }
  onClose: () => void
  onDeleted?: (id: string) => void
}) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [cascade, setCascade] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async () => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.deleteItem(item.id, cascade, fetched.etag)
      qc.removeQueries({ queryKey: qk.item(item.id) })
      for (const cid of res.cascaded) qc.removeQueries({ queryKey: qk.item(cid) })
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      const cascadedMsg = res.cascaded.length > 0 ? ` (+${res.cascaded.length} descendant)` : ''
      toast('success', `Deleted: ${item.shortId}${cascadedMsg}. Run \`knit verify\` to surface broken back-references.`)
      onClose()
      onDeleted?.(item.id)
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Delete failed'
      toast('error', `Delete failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-red-300 dark:border-red-900 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold text-red-700 dark:text-red-400">
          Delete {item.shortId} — {item.title}?
        </h3>
        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          This permanently removes the item file from disk. Use{' '}
          <span className="font-semibold">Dismiss</span> instead for a
          reversible soft-delete. Back-references in other items are not
          auto-cleaned — run <code className="font-mono">knit verify</code>{' '}
          afterwards to surface broken links.
        </p>
        <label className="flex items-center gap-2 text-xs">
          <input
            type="checkbox"
            checked={cascade}
            onChange={(e) => setCascade(e.target.checked)}
          />
          Also delete every descendant
        </label>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void submit()}
            className="text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            Delete
          </button>
        </div>
      </div>
    </div>
  )
}
