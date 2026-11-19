import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api, qk } from '../api/client'
import { applySortSummary } from '../lib/applySort'
import { useToasts } from '../stores/toasts'

// Confirm for baking the state sort into rank — under one item, or the whole
// tree when `item` is absent. The count comes from the server's dry run, the
// same plan the write applies.
export function ApplySortDialog({
  item,
  onClose,
}: {
  item?: { id: string; shortId: string; title: string }
  onClose: () => void
}) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [shallow, setShallow] = useState(false)
  const [busy, setBusy] = useState(false)

  const dryQ = useQuery({
    queryKey: ['apply-sort', item?.id ?? null, shallow],
    queryFn: () => api.applySort({ id: item?.id, shallow, dryRun: true }),
    staleTime: 0,
    gcTime: 0,
  })
  const plan = dryQ.data

  const submit = async () => {
    setBusy(true)
    try {
      const res = await api.applySort({ id: item?.id, shallow, dryRun: false })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: qk.items })
      for (const id of res.movedIds) qc.invalidateQueries({ queryKey: qk.item(id) })
      toast('success', `Ranks rewritten on ${res.moved} item${res.moved === 1 ? '' : 's'}`)
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? (e.problem?.detail ?? e.message) : 'Reorder failed'
      toast('error', `Reorder failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">
          Apply state order to ranks{item ? ` under ${item.shortId} — ${item.title}` : ' — whole tree'}
        </h3>
        <p className="text-xs text-zinc-600 dark:text-zinc-300">
          Makes the state order the planned order: each sibling group gets ranks that list it done → review →
          in-progress → todo → draft → dismissed. Items already in that order keep their rank. A snapshot — the next
          state change drifts from it again.
        </p>
        {item && (
          <label className="flex items-center gap-2 text-xs">
            <input type="checkbox" checked={shallow} onChange={(e) => setShallow(e.target.checked)} />
            Direct children only
          </label>
        )}
        <p className="text-xs font-medium" aria-live="polite">
          {dryQ.isPending
            ? 'Counting…'
            : dryQ.isError
              ? `Could not plan the reorder: ${dryQ.error instanceof ApiError ? (dryQ.error.problem?.detail ?? dryQ.error.message) : 'unknown error'}`
              : applySortSummary(plan!)}
        </p>
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
            disabled={busy || !plan || plan.moved === 0}
            onClick={() => void submit()}
            className="text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            Rewrite ranks
          </button>
        </div>
      </div>
    </div>
  )
}
