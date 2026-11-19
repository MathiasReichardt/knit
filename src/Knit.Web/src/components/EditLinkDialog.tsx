import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { X } from 'lucide-react'
import { ApiError, api, qk, type LinkRel } from '../api/client'
import type { Item, LinkRow } from '../api/types'
import { useToasts } from '../stores/toasts'

interface Props {
  item: Item
  link: LinkRow
  onClose: () => void
}

// The target is fixed — a different target is a different link. The relation
// only moves between these three; the others carry their own rules.
const RETARGETABLE: { rel: LinkRow['rel']; wire: LinkRel }[] = [
  { rel: 'Blocks', wire: 'blocks' },
  { rel: 'Blocked by', wire: 'blocked-by' },
  { rel: 'Related', wire: 'related' },
]

export function EditLinkDialog({ item, link, onClose }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const canChangeRel = RETARGETABLE.some((r) => r.rel === link.rel)
  const [rel, setRel] = useState<LinkRow['rel']>(link.rel)
  const [notes, setNotes] = useState(link.notes ?? '')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const unchanged = rel === link.rel && notes.trim() === (link.notes ?? '')

  const submit = async () => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.editLink(
        item.id,
        {
          rel: link.rel,
          targetPath: link.targetPath,
          targetText: link.targetText,
          notes: notes.trim() || null,
          newRel: rel === link.rel ? null : RETARGETABLE.find((r) => r.rel === rel)!.wire,
        },
        fetched.etag,
      )
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: qk.context(item.id) })
      toast('success', 'Link updated')
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Edit link failed'
      toast('error', `Edit link failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-40 flex items-start justify-center p-6 bg-black/40 backdrop-blur-sm"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="w-full max-w-lg mt-20 rounded-lg border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-xl text-zinc-900 dark:text-zinc-100">
        <header className="flex items-center justify-between px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
          <h2 className="text-sm font-semibold">Edit link</h2>
          <button
            type="button"
            onClick={onClose}
            title="Close"
            aria-label="Close"
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-900"
          >
            <X className="h-4 w-4" />
          </button>
        </header>
        <div className="p-4 space-y-4">
          <div>
            <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
              Relation
            </label>
            {canChangeRel ? (
              <select
                value={rel}
                onChange={(e) => setRel(e.target.value as LinkRow['rel'])}
                className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
              >
                {RETARGETABLE.map((r) => (
                  <option key={r.rel} value={r.rel}>
                    {r.rel}
                  </option>
                ))}
              </select>
            ) : (
              <div className="text-sm">{link.rel}</div>
            )}
          </div>
          <div>
            <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
              Target
            </label>
            <div className="text-sm break-words" title={link.targetPath}>
              {link.targetText || link.targetPath}
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
              Notes
            </label>
            <input
              type="text"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !unchanged && !busy) void submit()
              }}
              autoFocus
              className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
            />
          </div>
          <footer className="flex items-center justify-end gap-2 pt-2 border-t border-zinc-200 dark:border-zinc-800">
            <button
              type="button"
              onClick={onClose}
              className="px-3 py-1.5 rounded text-sm text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900"
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={busy || unchanged}
              onClick={() => void submit()}
              className="px-3 py-1.5 rounded text-sm bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {busy ? 'Saving…' : 'Save'}
            </button>
          </footer>
        </div>
      </div>
    </div>
  )
}
