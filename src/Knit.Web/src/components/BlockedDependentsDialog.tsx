import { useEffect, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, api, qk } from '../api/client'
import type { BlockedDependent } from '../api/types'
import { dependentAction } from '../lib/blockedDependents'
import { useBlockedDependents } from '../stores/blockedDependents'
import { useToasts } from '../stores/toasts'

// After a close: closing a blocker resolves nothing, so offer to resolve each
// item it still blocks. Later leaves them; verify and the doctor catch them.
export function BlockedDependentsDialog() {
  const groups = useBlockedDependents((s) => s.groups)
  const close = useBlockedDependents((s) => s.close)
  const done = useBlockedDependents((s) => s.resolved)
  const markResolved = useBlockedDependents((s) => s.markResolved)

  useEffect(() => {
    if (groups.length === 0) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [groups, close])

  if (groups.length === 0) return null
  const count = groups.reduce((n, g) => n + g.dependents.length, 0)

  return (
    <div className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4" onClick={close}>
      <div
        className="w-full max-w-lg rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3 text-zinc-900 dark:text-zinc-100"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold">
          {count === 1 ? '1 item is' : `${count} items are`} still blocked
        </h3>
        <div className="text-xs text-zinc-600 dark:text-zinc-300">
          Closing a blocker doesn't unblock anything. Resolve each block, or leave it if it still stands.
        </div>
        {groups.map((g) => (
          <div key={g.id} className="space-y-1">
            <div className="text-xs text-zinc-500">Blocked by {g.shortId}</div>
            {g.dependents.map((d) => (
              <DependentRow
                key={d.id}
                blockerId={g.id}
                dependent={d}
                result={done[`${g.id}/${d.id}`]}
                onResolved={(text) => markResolved(`${g.id}/${d.id}`, text)}
                onOpen={close}
              />
            ))}
          </div>
        ))}
        <div className="flex justify-end pt-1">
          <button
            type="button"
            onClick={close}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800"
          >
            {Object.keys(done).length === count ? 'Close' : 'Later'}
          </button>
        </div>
      </div>
    </div>
  )
}

function DependentRow({
  blockerId,
  dependent: d,
  result,
  onResolved,
  onOpen,
}: {
  blockerId: string
  dependent: BlockedDependent
  result: string | undefined
  onResolved: (text: string) => void
  onOpen: () => void
}) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const toast = useToasts((s) => s.push)
  const [busy, setBusy] = useState(false)
  const action = dependentAction(d)

  const resolve = async () => {
    setBusy(true)
    try {
      const { etag } = await api.fetchItemWithEtag(d.id)
      const res =
        action === 'unblock'
          ? await api.unblockItem(d.id, etag)
          : await api.deleteLink(d.id, 'blocked-by', blockerId, etag)
      qc.setQueryData(qk.item(d.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      onResolved(action === 'unblock' ? 'Unblocked' : 'Link removed — still blocked')
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Request failed'
      toast('error', `Resolve failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex items-center gap-2 text-xs rounded border border-zinc-200 dark:border-zinc-800 px-2 py-1.5">
      <div className="min-w-0 flex-1">
        <button
          type="button"
          onClick={() => {
            onOpen()
            void navigate({ to: '/items', search: { id: d.id } as never })
          }}
          className="text-left hover:underline break-words"
        >
          <span className="text-zinc-500">{d.shortId}</span> {d.title}
        </button>
        {d.remaining && (
          <div className="text-zinc-500 break-words">also {d.remaining}</div>
        )}
      </div>
      {result ? (
        <span className="shrink-0 text-zinc-500">{result}</span>
      ) : (
        <button
          type="button"
          disabled={busy}
          onClick={() => void resolve()}
          className="shrink-0 text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
        >
          {action === 'unblock' ? 'Unblock' : 'Remove link'}
        </button>
      )}
    </div>
  )
}
