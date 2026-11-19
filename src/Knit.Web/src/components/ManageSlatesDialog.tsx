import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  DndContext,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  SortableContext,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Archive, Check, GripVertical, Pencil, Plus, Trash2, X } from 'lucide-react'
import { ApiError, qk } from '../api/client'
import { doctorQk } from '../api/doctor'
import { slatesApi, slatesQk, type Slate } from '../api/slates'
import { useDialogs } from '../stores/dialogs'
import { SlateDeleteDialog } from './SlateDeleteDialog'
import { useToasts } from '../stores/toasts'

const STATUSES = ['planned', 'active', 'archived'] as const

export function ManageSlatesDialog() {
  const open = useDialogs((s) => s.manageSlatesOpen)
  const close = useDialogs((s) => s.closeManageSlates)
  if (!open) return null
  return <DialogBody onClose={close} />
}

function DialogBody({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const slatesQ = useQuery({ queryKey: slatesQk.list, queryFn: slatesApi.list })
  const registry = (slatesQ.data ?? []).filter((s) => s.inRegistry)
  const itemOnly = (slatesQ.data ?? []).filter((s) => !s.inRegistry)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: slatesQk.list })
    qc.invalidateQueries({ queryKey: doctorQk.verify })
  }
  const invalidateItems = () => {
    qc.invalidateQueries({ queryKey: qk.items })
    qc.invalidateQueries({ queryKey: qk.tree })
  }
  const fail = (prefix: string) => (e: unknown) => {
    const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : (e as Error).message
    toast('error', `${prefix}: ${msg}`)
  }

  const createM = useMutation({
    mutationFn: (name: string) => slatesApi.create({ name }),
    onSuccess: (s) => { invalidate(); toast('success', `Created slate ${s.name}`) },
    onError: fail('Create failed'),
  })
  // Reorder: PATCH {before|after} relative to the row currently occupying
  // the drop slot — the server rewrites the table row order.
  const moveM = useMutation({
    mutationFn: ({ name, before, after }: { name: string; before?: string; after?: string }) =>
      slatesApi.patch(name, { before: before ?? null, after: after ?? null }),
    onSuccess: invalidate,
    onError: fail('Reorder failed'),
  })

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  const onDragEnd = (e: DragEndEvent) => {
    const from = registry.findIndex((s) => s.name === e.active.id)
    const to = registry.findIndex((s) => s.name === e.over?.id)
    if (from < 0 || to < 0 || from === to) return
    const name = registry[from]!.name
    const anchor = registry[to]!.name
    moveM.mutate(from < to ? { name, after: anchor } : { name, before: anchor })
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="manage-slates-title"
      className="fixed inset-0 z-40 flex items-start justify-center p-6 bg-black/40 backdrop-blur-sm"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="w-full max-w-2xl mt-16 rounded-lg border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-xl">
        <header className="flex items-center justify-between px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
          <h2 id="manage-slates-title" className="text-sm font-semibold">Manage slates</h2>
          <button
            type="button"
            onClick={onClose}
            title="Close"
            aria-label="Close dialog"
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-900"
          >
            <X className="h-4 w-4" />
          </button>
        </header>
        <div className="p-4 space-y-3 max-h-[65vh] overflow-y-auto text-sm">
          {registry.length === 0 && itemOnly.length === 0 && (
            <p className="text-zinc-500 italic">
              No slates yet — add one below to start selecting work.
            </p>
          )}
          {registry.length > 0 && (
            <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
              <SortableContext
                items={registry.map((s) => s.name)}
                strategy={verticalListSortingStrategy}
              >
                <ul className="flex flex-col gap-1">
                  {registry.map((s) => (
                    <SlateRow
                      key={s.name}
                      slate={s}
                      onChanged={invalidate}
                      onItemsChanged={invalidateItems}
                    />
                  ))}
                </ul>
              </SortableContext>
            </DndContext>
          )}
          {itemOnly.length > 0 && (
            <div className="pt-1 border-t border-zinc-200 dark:border-zinc-800">
              <p className="text-xs text-zinc-500 mt-2 mb-1">
                On items but not in the registry:
              </p>
              <ul className="flex flex-col gap-1">
                {itemOnly.map((s) => (
                  <li key={s.name} className="flex items-center gap-2 px-2 py-1 rounded text-zinc-500">
                    <span className="font-mono text-xs flex-1 truncate">{s.name}</span>
                    <ItemCount count={s.itemCount} />
                    <button
                      type="button"
                      onClick={() => createM.mutate(s.name)}
                      title="Add to the registry"
                      aria-label={`Add ${s.name} to the registry`}
                      className="p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-100 dark:hover:bg-zinc-900"
                    >
                      <Plus className="h-3.5 w-3.5" />
                    </button>
                    <DeleteSlateButton
                      slate={s}
                      onChanged={invalidate}
                      onItemsChanged={invalidateItems}
                    />
                  </li>
                ))}
              </ul>
            </div>
          )}
          <AddSlate onAdd={(name) => createM.mutate(name)} pending={createM.isPending} />
        </div>
      </div>
    </div>
  )
}

function SlateRow({
  slate,
  onChanged,
  onItemsChanged,
}: {
  slate: Slate
  onChanged: () => void
  onItemsChanged: () => void
}) {
  const toast = useToasts((s) => s.push)
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: slate.name })
  const [renaming, setRenaming] = useState(false)
  const [name, setName] = useState(slate.name)
  const [notes, setNotes] = useState(slate.notes ?? '')

  const fail = (prefix: string) => (e: unknown) => {
    const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : (e as Error).message
    toast('error', `${prefix}: ${msg}`)
  }
  const patchM = useMutation({
    mutationFn: (body: Parameters<typeof slatesApi.patch>[1]) => slatesApi.patch(slate.name, body),
    onError: fail('Update failed'),
  })
  const rename = () => {
    const next = name.trim()
    setRenaming(false)
    if (!next || next === slate.name) { setName(slate.name); return }
    patchM.mutate({ rename: next }, {
      onSuccess: (r) => {
        onChanged()
        if (r.itemsUpdated > 0) onItemsChanged()
        toast('success', `Renamed to ${r.slate.name} (${r.itemsUpdated} item${r.itemsUpdated === 1 ? '' : 's'} updated)`)
      },
    })
  }
  const saveNotes = () => {
    if (notes === (slate.notes ?? '')) return
    patchM.mutate({ notes }, { onSuccess: onChanged })
  }
  const setStatus = (status: string) =>
    patchM.mutate({ status }, { onSuccess: onChanged })

  return (
    <li
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={
        'flex items-center gap-2 px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 ' +
        (isDragging ? 'opacity-60 shadow-md z-10 relative' : '') +
        (slate.status === 'archived' ? ' opacity-60' : '')
      }
    >
      <button
        type="button"
        title="Drag to reorder"
        aria-label={`Reorder ${slate.name}`}
        className="cursor-grab text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300 touch-none"
        {...attributes}
        {...listeners}
      >
        <GripVertical className="h-4 w-4" />
      </button>
      {renaming ? (
        <span className="flex items-center gap-1 flex-1 min-w-0">
          <input
            type="text"
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') { e.preventDefault(); rename() }
              if (e.key === 'Escape') { setName(slate.name); setRenaming(false) }
            }}
            aria-label={`Rename ${slate.name}`}
            className="flex-1 min-w-0 px-1.5 py-0.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 font-mono text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          <span className="text-[11px] text-zinc-500 whitespace-nowrap">
            renames {slate.itemCount} item{slate.itemCount === 1 ? '' : 's'}
          </span>
          <button
            type="button"
            onClick={rename}
            title="Apply rename"
            aria-label="Apply rename"
            className="p-1 rounded text-zinc-500 hover:text-green-600 hover:bg-zinc-100 dark:hover:bg-zinc-900"
          >
            <Check className="h-3.5 w-3.5" />
          </button>
        </span>
      ) : (
        <span className="group flex items-center gap-1 flex-1 min-w-0">
          <span className="font-mono text-xs truncate">{slate.name}</span>
          <button
            type="button"
            onClick={() => { setName(slate.name); setRenaming(true) }}
            title="Rename (rewrites slate: on assigned items)"
            aria-label={`Rename ${slate.name}`}
            className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-0.5 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-100 dark:hover:bg-zinc-900 shrink-0"
          >
            <Pencil className="h-3 w-3" />
          </button>
        </span>
      )}
      <ItemCount count={slate.itemCount} />
      <select
        value={slate.status}
        onChange={(e) => setStatus(e.target.value)}
        aria-label={`Status of ${slate.name}`}
        className="px-1.5 py-0.5 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        {STATUSES.map((st) => (
          <option key={st} value={st}>{st}</option>
        ))}
      </select>
      <input
        type="text"
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        onBlur={saveNotes}
        onKeyDown={(e) => {
          if (e.key === 'Enter') { e.preventDefault(); (e.target as HTMLInputElement).blur() }
        }}
        placeholder="notes"
        aria-label={`Notes for ${slate.name}`}
        className="w-36 px-1.5 py-0.5 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      {slate.status !== 'archived' && (
        <button
          type="button"
          onClick={() => setStatus('archived')}
          title="Archive (hidden from the assign dropdown)"
          aria-label={`Archive ${slate.name}`}
          className="p-1 rounded text-zinc-500 hover:text-amber-600 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <Archive className="h-3.5 w-3.5" />
        </button>
      )}
      <DeleteSlateButton slate={slate} onChanged={onChanged} onItemsChanged={onItemsChanged} />
    </li>
  )
}

function DeleteSlateButton({
  slate,
  onChanged,
  onItemsChanged,
}: {
  slate: Slate
  onChanged: () => void
  onItemsChanged: () => void
}) {
  const toast = useToasts((s) => s.push)
  const [confirming, setConfirming] = useState(false)
  const deleteM = useMutation({
    mutationFn: () => slatesApi.remove(slate.name),
    onSuccess: (r) => {
      setConfirming(false)
      onChanged()
      if (r.itemsCleared > 0) onItemsChanged()
      const n = (k: number) => `${k} item${k === 1 ? '' : 's'}`
      toast('success', r.itemsCleared > 0
        ? `Removed ${r.name} and cleared it from ${n(r.itemsCleared)}`
        : `Removed ${r.name}`)
    },
    onError: (e: unknown) => {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : (e as Error).message
      toast('error', `Delete failed: ${msg}`)
    },
  })
  return (
    <>
      <button
        type="button"
        onClick={() => setConfirming(true)}
        title="Remove (also clears it from its items)"
        aria-label={`Delete ${slate.name}`}
        className="p-1 rounded text-zinc-500 hover:text-red-600 hover:bg-zinc-100 dark:hover:bg-zinc-900"
      >
        <Trash2 className="h-3.5 w-3.5" />
      </button>
      {confirming && (
        <SlateDeleteDialog
          name={slate.name}
          itemCount={slate.itemCount}
          busy={deleteM.isPending}
          onConfirm={() => deleteM.mutate()}
          onCancel={() => setConfirming(false)}
        />
      )}
    </>
  )
}

function ItemCount({ count }: { count: number }) {
  return (
    <span
      className="text-[11px] text-zinc-500 whitespace-nowrap tabular-nums"
      title={`${count} item${count === 1 ? '' : 's'} assigned`}
    >
      {count}
    </span>
  )
}

function AddSlate({ onAdd, pending }: { onAdd: (name: string) => void; pending: boolean }) {
  const [name, setName] = useState('')
  const inputRef = useRef<HTMLInputElement | null>(null)
  const submit = () => {
    const v = name.trim()
    if (!v) return
    onAdd(v)
    setName('')
    inputRef.current?.focus()
  }
  return (
    <form
      className="flex items-center gap-2 pt-2 border-t border-zinc-200 dark:border-zinc-800"
      onSubmit={(e) => { e.preventDefault(); submit() }}
    >
      <input
        ref={inputRef}
        type="text"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="New slate name (slugified)…"
        aria-label="New slate name"
        className="flex-1 px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      <button
        type="submit"
        disabled={!name.trim() || pending}
        className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <Plus className="h-3.5 w-3.5" aria-hidden />
        Add slate
      </button>
    </form>
  )
}
