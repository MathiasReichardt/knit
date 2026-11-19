import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { X } from 'lucide-react'
import { ApiError, api, qk } from '../api/client'
import { projectsApi } from '../api/projects'
import type { ItemSummary, ItemType } from '../api/types'
import { TYPE_COLOR, TYPE_ICON, TYPE_LABEL } from '../lib/glyphs'
import { explainRefusal, parentAllowed, refusalSentence } from '../lib/hierarchy'
import { slugify } from '../lib/slug'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'

const TYPES: ItemType[] = ['project', 'epic', 'feature', 'bug', 'task', 'todo']

export function NewItemDialog() {
  const open = useDialogs((s) => s.newItemOpen)
  const prefill = useDialogs((s) => s.newItemPrefill)
  const close = useDialogs((s) => s.closeNewItem)
  if (!open) return null
  return <DialogBody prefill={prefill} onClose={close} />
}

function DialogBody({
  prefill,
  onClose,
}: {
  prefill: {
    parentId?: string | null
    type?: ItemType
    allowedTypes?: ItemType[]
    stayOnCreate?: boolean
  }
  onClose: () => void
}) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const titleRef = useRef<HTMLInputElement | null>(null)

  const types = prefill.allowedTypes ?? TYPES
  const [type, setType] = useState<ItemType>(prefill.type ?? types[0] ?? 'task')
  const [title, setTitle] = useState('')
  const [parentId, setParentId] = useState<string | null>(prefill.parentId ?? null)
  // Projects are two halves: co-create the doc folder + linked
  // item by default so the New-project flow never leaves an orphan. Unchecking
  // is the escape hatch (bare project item, folder scaffolded later).
  const [scaffoldFolder, setScaffoldFolder] = useState(true)
  const [addRoadmap, setAddRoadmap] = useState(false)

  useEffect(() => {
    titleRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })
  const byId = useMemo(() => {
    const m = new Map<string, ItemSummary>()
    for (const it of itemsQ.data ?? []) m.set(it.id, it)
    return m
  }, [itemsQ.data])
  const parent = parentId ? byId.get(parentId) ?? null : null
  const parentTypeOk = parentAllowed(type, parent?.type ?? null)

  // Co-create only for a top-level project (projects are roots — the atomic
  // path doesn't take a parent); a project as a sub-item falls back to a bare
  // tracker item.
  const coCreateProject = type === 'project' && scaffoldFolder && !parentId

  const mutation = useMutation({
    mutationFn: async () => {
      if (coCreateProject) {
        const r = await projectsApi.createProject({
          slug: slugify(title.trim()),
          title: title.trim(),
          roadmap: addRoadmap,
        })
        return { kind: 'project' as const, itemId: r.itemId, slug: r.slug }
      }
      const { item } = await api.createItem({ type, title: title.trim(), parent: parentId })
      return { kind: 'item' as const, item }
    },
    onSuccess: (res) => {
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: ['projects'] })
      onClose()
      if (res.kind === 'project') {
        toast('success', `Created project ${res.slug} (folder + linked item)`)
        navigate({ to: '/projects', search: { id: res.itemId } as never }).catch(() => {})
        return
      }
      toast('success', `Created ${res.item.shortId} — ${res.item.title}`)
      if (!prefill.stayOnCreate) {
        navigate({ to: '/items', search: () => ({ }) as never }).catch(() => {})
      }
    },
    onError: (e: unknown) => {
      const msg = e instanceof ApiError ? (e.problem?.detail ?? e.message) : (e as Error).message
      toast('error', `Create failed: ${msg}`)
    },
  })

  const canSubmit = title.trim().length > 0 && parentTypeOk && !mutation.isPending

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="new-item-title"
      className="fixed inset-0 z-40 flex items-start justify-center p-6 bg-black/40 backdrop-blur-sm"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="w-full max-w-lg mt-20 rounded-lg border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-xl">
        <header className="flex items-center justify-between px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
          <h2 id="new-item-title" className="text-sm font-semibold">New item</h2>
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
        <form
          className="p-4 space-y-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (canSubmit) mutation.mutate()
          }}
        >
          {types.length > 1 && (
          <div>
            <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
              Type
            </label>
            <div className="flex flex-wrap gap-1.5">
              {types.map((t) => {
                const Icon = TYPE_ICON[t]
                const active = type === t
                const allowed = parentAllowed(t, parent?.type ?? null)
                return (
                  <button
                    key={t}
                    type="button"
                    onClick={() => setType(t)}
                    disabled={!allowed}
                    title={
                      allowed
                        ? TYPE_LABEL[t]
                        : explainRefusal(t, parent?.type ?? null)
                    }
                    className={
                      'inline-flex items-center gap-1.5 px-3 py-1.5 rounded border text-sm ' +
                      (active
                        ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-200'
                        : allowed
                          ? 'border-zinc-200 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-900'
                          : 'border-zinc-200 dark:border-zinc-800 opacity-40 cursor-not-allowed')
                    }
                  >
                    <Icon className={`${TYPE_COLOR[t]} h-4 w-4`} />
                    {TYPE_LABEL[t]}
                  </button>
                )
              })}
            </div>
          </div>
          )}

          <div>
            <label htmlFor="new-item-title-input" className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
              Title
            </label>
            <input
              id="new-item-title-input"
              ref={titleRef}
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="What needs doing?"
              className="w-full px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>

          {type === 'project' && (
            <div className="space-y-2 rounded border border-zinc-200 dark:border-zinc-800 p-3">
              <label className="flex items-center gap-2 text-sm select-none">
                <input
                  type="checkbox"
                  checked={scaffoldFolder}
                  onChange={(e) => setScaffoldFolder(e.target.checked)}
                  className="h-3.5 w-3.5"
                />
                <span>Scaffold doc folder (<span className="font-mono">readme.md</span>) + link this item</span>
              </label>
              {scaffoldFolder && (
                <>
                  <label className="flex items-center gap-2 text-sm select-none pl-5">
                    <input
                      type="checkbox"
                      checked={addRoadmap}
                      onChange={(e) => setAddRoadmap(e.target.checked)}
                      className="h-3.5 w-3.5"
                    />
                    <span>Also add a <span className="font-mono">roadmap.md</span></span>
                  </label>
                  <p className="pl-5 text-xs text-zinc-500">
                    {parentId
                      ? 'A project with a parent is created as a bare item (co-create is for top-level projects).'
                      : <>Folder: <span className="font-mono">.knit/projects/{slugify(title.trim()) || 'slug'}/</span></>}
                  </p>
                </>
              )}
            </div>
          )}

          <ParentPicker
            value={parentId}
            onChange={setParentId}
            items={itemsQ.data ?? []}
          />

          {!parentTypeOk && parent && (
            <p className="text-xs text-red-600 dark:text-red-400">
              {refusalSentence(type, parent.type)} Pick a different type or clear the parent.
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
              type="submit"
              disabled={!canSubmit}
              className="px-3 py-1.5 rounded text-sm bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {mutation.isPending ? 'Creating…' : 'Create'}
            </button>
          </footer>
        </form>
      </div>
    </div>
  )
}

function ParentPicker({
  value,
  onChange,
  items,
}: {
  value: string | null
  onChange: (id: string | null) => void
  items: ItemSummary[]
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const current = value ? items.find((i) => i.id === value) : null

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return items.slice(0, 25)
    return items
      .filter((i) =>
        i.title.toLowerCase().includes(q) ||
        i.shortId.toLowerCase().includes(q),
      )
      .slice(0, 25)
  }, [items, query])

  return (
    <div>
      <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
        Parent (optional)
      </label>
      {current ? (
        <div className="flex items-center gap-2 px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-zinc-50 dark:bg-zinc-900">
          <span className="font-mono text-xs text-zinc-500">{current.shortId}</span>
          <span className="flex-1 truncate text-sm">{current.title}</span>
          <button
            type="button"
            onClick={() => onChange(null)}
            title="Clear parent"
            aria-label="Clear parent"
            className="text-xs text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ) : (
        <div className="relative">
          <input
            type="text"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value)
              setOpen(true)
            }}
            onFocus={() => setOpen(true)}
            onBlur={() => window.setTimeout(() => setOpen(false), 150)}
            placeholder="Search by title or short id…"
            className="w-full px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          {open && filtered.length > 0 && (
            <ul className="absolute left-0 right-0 mt-1 max-h-56 overflow-auto rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-950 shadow-lg z-10">
              {filtered.map((i) => (
                <li key={i.id}>
                  <button
                    type="button"
                    onClick={() => {
                      onChange(i.id)
                      setOpen(false)
                      setQuery('')
                    }}
                    className="flex items-center gap-2 w-full px-3 py-1.5 text-left text-sm hover:bg-zinc-100 dark:hover:bg-zinc-900"
                  >
                    <span className="font-mono text-xs text-zinc-500">{i.shortId}</span>
                    <span className="text-[10px] uppercase text-zinc-500">{i.type}</span>
                    <span className="flex-1 truncate">{i.title}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
