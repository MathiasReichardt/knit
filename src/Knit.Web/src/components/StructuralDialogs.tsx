import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { X } from 'lucide-react'
import { ApiError, api, qk } from '../api/client'
import type { Item, ItemSummary, ItemType } from '../api/types'
import { TYPE_COLOR, TYPE_ICON, TYPE_LABEL } from '../lib/glyphs'
import { parentAllowed, refusalSentence } from '../lib/hierarchy'
import { patchItemSafe } from '../lib/patchItem'
import { useToasts } from '../stores/toasts'
import { ItemPicker } from './AddLinkDialog'

const TYPES: ItemType[] = ['project', 'epic', 'feature', 'bug', 'task', 'todo']

function DialogShell({
  title,
  children,
  onClose,
}: {
  title: string
  children: React.ReactNode
  onClose: () => void
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-40 flex items-start justify-center p-6 bg-black/40 backdrop-blur-sm"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="w-full max-w-lg mt-20 rounded-lg border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-xl">
        <header className="flex items-center justify-between px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
          <h2 className="text-sm font-semibold">{title}</h2>
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
        <div className="p-4 space-y-4">{children}</div>
      </div>
    </div>
  )
}

export function MoveParentDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  // Tri-state: 'pick' = the user must select a new parent; 'detach' =
  // move to root. We do NOT pre-fill the picker with the current parent
  // — that turned out to confuse users into submitting a no-op move.
  const [mode, setMode] = useState<'pick' | 'detach'>('pick')
  const [parentId, setParentId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })

  const all = itemsQ.data ?? []
  const target = parentId ? all.find((i) => i.id === parentId) : null
  const allowed = parentAllowed(item.type, mode === 'detach' ? null : target?.type ?? null)
  const currentParent = item.parent ? all.find((i) => i.id === item.parent) ?? null : null
  const wouldChange =
    mode === 'detach'
      ? !!item.parent
      : !!parentId && parentId !== item.parent

  // Filter out self + descendants from the picker to avoid cycles.
  const descendantIds = new Set<string>()
  ;(() => {
    const visit = (id: string) => {
      descendantIds.add(id)
      for (const i of all) if (i.parent === id) visit(i.id)
    }
    visit(item.id)
  })()
  const candidates = all.filter(
    (i: ItemSummary) => !descendantIds.has(i.id) && parentAllowed(item.type, i.type),
  )

  const submit = async () => {
    setBusy(true)
    // Empty string = detach to root on the PATCH contract.
    const wire = mode === 'detach' ? '' : parentId!
    const res = await patchItemSafe(qc, item.id, { parent: wire })
    setBusy(false)
    if (!res.ok) {
      toast('error', `Move failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
      return
    }
    toast('success', `Moved ${item.shortId}`)
    onClose()
  }

  return (
    <DialogShell title={`Move ${item.shortId}`} onClose={onClose}>
      <p className="text-xs text-zinc-600 dark:text-zinc-400">
        {currentParent ? (
          <>
            Currently under <span className="font-mono">{currentParent.shortId}</span>{' '}
            {currentParent.title}.
          </>
        ) : (
          <>Currently a root item.</>
        )}
      </p>
      <div className="flex gap-1.5">
        <button
          type="button"
          onClick={() => setMode('pick')}
          className={
            'px-2 py-1 rounded border text-xs ' +
            (mode === 'pick'
              ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200'
              : 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-900')
          }
        >
          Move under…
        </button>
        <button
          type="button"
          onClick={() => setMode('detach')}
          className={
            'px-2 py-1 rounded border text-xs ' +
            (mode === 'detach'
              ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200'
              : 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-900')
          }
        >
          Detach (root)
        </button>
      </div>
      {mode === 'pick' && (
        <ItemPicker
          items={candidates}
          value={parentId}
          onChange={setParentId}
          label="New parent"
        />
      )}
      {!allowed && mode === 'pick' && parentId && (
        <p className="text-xs text-red-600 dark:text-red-400">
          {refusalSentence(item.type, target?.type ?? null)}
        </p>
      )}
      {mode === 'pick' && parentId && parentId === item.parent && (
        <p className="text-xs text-amber-600 dark:text-amber-400">
          That's already this item's parent — pick a different target.
        </p>
      )}
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
          disabled={
            busy || !allowed || !wouldChange || (mode === 'pick' && !parentId)
          }
          onClick={() => void submit()}
          className="px-3 py-1.5 rounded text-sm bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Moving…' : mode === 'detach' ? 'Detach' : 'Move'}
        </button>
      </footer>
    </DialogShell>
  )
}

export function RetypeDialog({ item, onClose }: { item: Item; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [type, setType] = useState<ItemType>(item.type)
  // null = keep current parent; "" = detach; string = new parent id.
  const [parentMode, setParentMode] = useState<'keep' | 'detach' | 'pick'>('keep')
  const [parentId, setParentId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })

  const all = itemsQ.data ?? []
  const currentParent = item.parent ? all.find((i) => i.id === item.parent) ?? null : null
  const target = parentId ? all.find((i) => i.id === parentId) : null

  const effectiveParentType =
    parentMode === 'detach'
      ? null
      : parentMode === 'keep'
        ? currentParent?.type ?? null
        : target?.type ?? null

  const allowed = parentAllowed(type, effectiveParentType)

  const descendantIds = new Set<string>()
  ;(() => {
    const visit = (id: string) => {
      descendantIds.add(id)
      for (const i of all) if (i.parent === id) visit(i.id)
    }
    visit(item.id)
  })()
  const candidates = all.filter(
    (i: ItemSummary) => !descendantIds.has(i.id) && parentAllowed(type, i.type),
  )

  const submit = async () => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const parentArg =
        parentMode === 'keep'
          ? null
          : parentMode === 'detach'
            ? ''
            : parentId ?? null
      const res = await api.retypeItem(
        item.id,
        { to: type, parent: parentArg },
        fetched.etag,
      )
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: qk.context(item.id) })
      toast('success', `Retyped ${item.shortId} → ${type}`)
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Retype failed'
      toast('error', `Retype failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <DialogShell title={`Change type · ${item.shortId}`} onClose={onClose}>
      <div>
        <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
          New type
        </label>
        <div className="flex gap-1.5 flex-wrap">
          {TYPES.map((t) => {
            const Icon = TYPE_ICON[t]
            const active = type === t
            return (
              <button
                key={t}
                type="button"
                onClick={() => setType(t)}
                className={
                  'inline-flex items-center gap-1.5 px-3 py-1.5 rounded border text-sm ' +
                  (active
                    ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200'
                    : 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-900')
                }
              >
                <Icon className={`${TYPE_COLOR[t]} h-4 w-4`} />
                {TYPE_LABEL[t]}
              </button>
            )
          })}
        </div>
      </div>

      <div>
        <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
          Parent
        </label>
        <div className="flex gap-1.5 mb-2">
          <ParentModeButton
            active={parentMode === 'keep'}
            onClick={() => setParentMode('keep')}
          >
            Keep current
          </ParentModeButton>
          <ParentModeButton
            active={parentMode === 'detach'}
            onClick={() => setParentMode('detach')}
          >
            Detach (root)
          </ParentModeButton>
          <ParentModeButton
            active={parentMode === 'pick'}
            onClick={() => setParentMode('pick')}
          >
            Move under…
          </ParentModeButton>
        </div>
        {parentMode === 'pick' && (
          <ItemPicker
            items={candidates}
            value={parentId}
            onChange={setParentId}
            label="New parent"
          />
        )}
        {parentMode === 'keep' && currentParent && (
          <p className="text-xs text-zinc-500">
            Current parent: <span className="font-mono">{currentParent.shortId}</span>{' '}
            {currentParent.title}
          </p>
        )}
      </div>

      {!allowed && (
        <p className="text-xs text-red-600 dark:text-red-400">
          {refusalSentence(type, effectiveParentType)}
        </p>
      )}

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
          disabled={busy || !allowed || (parentMode === 'pick' && !parentId)}
          onClick={() => void submit()}
          className="px-3 py-1.5 rounded text-sm bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Retyping…' : 'Retype'}
        </button>
      </footer>
    </DialogShell>
  )
}

function ParentModeButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'px-2 py-1 rounded border text-xs ' +
        (active
          ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200'
          : 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-900')
      }
    >
      {children}
    </button>
  )
}
