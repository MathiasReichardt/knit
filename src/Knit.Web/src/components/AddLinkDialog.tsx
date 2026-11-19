import { useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { X, FileText, Flag } from 'lucide-react'
import { ApiError, api, qk, type LinkRel } from '../api/client'
import type { Item, ItemSummary } from '../api/types'
import { useToasts } from '../stores/toasts'
import { kbApi, kbQk, type KbSummary } from '../api/kb'
import { projectsApi, projectsQk, type Milestone } from '../api/projects'
import { ACTIVE_PILL } from '../lib/activeStyle'

// Pressing an option would blur the input and close the list before the click lands.
const keepInputFocus = (e: React.MouseEvent) => e.preventDefault()

interface Props {
  item: Item
  onClose: () => void
}

const REL_OPTIONS: { value: LinkRel; label: string }[] = [
  { value: 'parent', label: 'Parent' },
  { value: 'blocks', label: 'Blocks' },
  { value: 'blocked-by', label: 'Blocked by' },
  { value: 'related', label: 'Related' },
]

type Tab = 'item' | 'kb' | 'milestone' | 'external'

// Branch / Repo / PR / Commit — a free-text target, validated server-side
// against the declared mounts, the filesystem or URL shape depending on the rel.
const EXTERNAL_OPTIONS: { value: LinkRel; label: string; placeholder: string }[] = [
  { value: 'branch', label: 'Branch', placeholder: 'feature/checkout-rewrite' },
  { value: 'repo', label: 'Repo', placeholder: 'a mount name, a path, or git@host:org/repo.git' },
  { value: 'pr', label: 'PR', placeholder: 'https://github.com/org/repo/pull/42' },
  { value: 'commit', label: 'Commit', placeholder: 'a SHA, or https://github.com/org/repo/commit/abc1234…' },
]

export function AddLinkDialog({ item, onClose }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [tab, setTab] = useState<Tab>('item')
  const [rel, setRel] = useState<LinkRel>('related')
  const [targetId, setTargetId] = useState<string | null>(null)
  const [kbRef, setKbRef] = useState<string | null>(null)
  // Selected milestone as a qualified ref `<project>/<slug>` (unambiguous).
  const [msRef, setMsRef] = useState<string | null>(null)
  const [extRel, setExtRel] = useState<LinkRel>('branch')
  const [extTarget, setExtTarget] = useState('')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const itemsQ = useQuery({ queryKey: qk.items, queryFn: () => api.items() })
  const kbQ = useQuery({ queryKey: kbQk.list(), queryFn: () => kbApi.list() })
  const projectsQ = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const milestones = useMemo(
    () => (projectsQ.data ?? []).flatMap((p) => p.milestones),
    [projectsQ.data],
  )

  const submit = async () => {
    if (tab === 'item' && !targetId) return
    if (tab === 'kb' && !kbRef) return
    if (tab === 'milestone' && !msRef) return
    if (tab === 'external' && !extTarget.trim()) return
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const effectiveRel: LinkRel =
        tab === 'kb' ? 'kb-ref' : tab === 'milestone' ? 'milestone' : tab === 'external' ? extRel : rel
      const target =
        tab === 'kb'
          ? kbRef!
          : tab === 'milestone'
            ? msRef!
            : tab === 'external'
              ? extTarget.trim()
              : targetId!
      const res = await api.addLink(
        item.id,
        { rel: effectiveRel, target, notes: notes.trim() || null },
        fetched.etag,
      )
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      qc.invalidateQueries({ queryKey: qk.context(item.id) })
      // milestone linkedCount + backlinks change on a Milestone link.
      if (effectiveRel === 'milestone') qc.invalidateQueries({ queryKey: ['projects'] })
      toast('success', `Added ${effectiveRel} link`)
      onClose()
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Add link failed'
      toast('error', `Add link failed: ${msg}`)
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
      <div className="w-full max-w-lg mt-20 rounded-lg border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-xl">
        <header className="flex items-center justify-between px-4 py-3 border-b border-zinc-200 dark:border-zinc-800">
          <h2 className="text-sm font-semibold">Add link</h2>
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
          <div className="flex border-b border-zinc-200 dark:border-zinc-800">
            <TabButton active={tab === 'item'} onClick={() => setTab('item')}>
              Item
            </TabButton>
            <TabButton active={tab === 'kb'} onClick={() => setTab('kb')}>
              KB
            </TabButton>
            <TabButton
              active={tab === 'milestone'}
              onClick={() => setTab('milestone')}
              disabled={milestones.length === 0}
              title={milestones.length === 0 ? 'No milestones yet' : undefined}
            >
              Milestone
            </TabButton>
            <TabButton active={tab === 'external'} onClick={() => setTab('external')}>
              External
            </TabButton>
          </div>

          {tab === 'item' && (
            <>
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Relation
                </label>
                <select
                  value={rel}
                  onChange={(e) => setRel(e.target.value as LinkRel)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                >
                  {REL_OPTIONS.map((r) => (
                    <option key={r.value} value={r.value}>
                      {r.label}
                    </option>
                  ))}
                </select>
              </div>

              <ItemPicker
                items={(itemsQ.data ?? []).filter((i) => i.id !== item.id)}
                value={targetId}
                onChange={setTargetId}
              />

              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Notes (optional)
                </label>
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                />
              </div>
            </>
          )}

          {tab === 'kb' && (
            <>
              <KbPicker
                entries={kbQ.data ?? []}
                value={kbRef}
                onChange={setKbRef}
              />
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Notes (optional)
                </label>
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                />
              </div>
            </>
          )}

          {tab === 'milestone' && (
            <>
              <MilestonePicker milestones={milestones} value={msRef} onChange={setMsRef} />
              <p className="text-[11px] text-zinc-500">
                One milestone per item — adding replaces any existing milestone link.
              </p>
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Notes (optional)
                </label>
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                />
              </div>
            </>
          )}

          {tab === 'external' && (
            <>
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Relation
                </label>
                <select
                  value={extRel}
                  onChange={(e) => setExtRel(e.target.value as LinkRel)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                >
                  {EXTERNAL_OPTIONS.map((r) => (
                    <option key={r.value} value={r.value}>
                      {r.label}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Target
                </label>
                <input
                  type="text"
                  value={extTarget}
                  onChange={(e) => setExtTarget(e.target.value)}
                  placeholder={EXTERNAL_OPTIONS.find((r) => r.value === extRel)?.placeholder}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  Notes (optional)
                </label>
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full px-2 py-1.5 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
                />
              </div>
            </>
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
                busy ||
                (tab === 'item'
                  ? !targetId
                  : tab === 'kb'
                    ? !kbRef
                    : tab === 'milestone'
                      ? !msRef
                      : !extTarget.trim())
              }
              onClick={() => void submit()}
              className="px-3 py-1.5 rounded text-sm bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {busy ? 'Adding…' : 'Add'}
            </button>
          </footer>
        </div>
      </div>
    </div>
  )
}

function TabButton({
  active,
  onClick,
  children,
  disabled,
  title,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
  disabled?: boolean
  title?: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={
        'px-3 py-1.5 text-sm border-b-2 -mb-px ' +
        (active
          ? 'border-blue-500 text-blue-700 dark:text-blue-300'
          : disabled
            ? 'border-transparent text-zinc-400 cursor-not-allowed'
            : 'border-transparent text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-100')
      }
    >
      {children}
    </button>
  )
}

function KbPicker({
  entries,
  value,
  onChange,
}: {
  entries: KbSummary[]
  value: string | null
  onChange: (ref: string | null) => void
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const current = value ? entries.find((e) => e.ref === value) : null

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const base = q
      ? entries.filter(
          (e) =>
            e.title.toLowerCase().includes(q) ||
            e.ref.toLowerCase().includes(q) ||
            (e.summary?.toLowerCase().includes(q) ?? false),
        )
      : entries
    return base.slice(0, 25)
  }, [entries, query])

  return (
    <div>
      <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
        KB target
      </label>
      {current ? (
        <div className={`flex items-center gap-2 px-3 py-2 rounded border ${ACTIVE_PILL}`}>
          <FileText className="h-3.5 w-3.5 text-zinc-500" aria-hidden />
          <span className="font-mono text-xs text-zinc-500">{current.ref}</span>
          <span className="flex-1 truncate text-sm">{current.title}</span>
          <button
            type="button"
            onClick={() => onChange(null)}
            title="Clear"
            aria-label="Clear"
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
            onBlur={() => setOpen(false)}
            placeholder="Search KB by title or ref…"
            className="w-full px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
          />
          {open && filtered.length > 0 && (
            <ul
              onMouseDown={keepInputFocus}
              className="absolute left-0 right-0 mt-1 max-h-56 overflow-auto rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 shadow-xl ring-1 ring-black/5 dark:ring-white/10 py-1 z-10"
            >
              {filtered.map((e) => (
                <li key={e.ref}>
                  <button
                    type="button"
                    onClick={() => {
                      onChange(e.ref)
                      setOpen(false)
                      setQuery('')
                    }}
                    className="flex items-center gap-2 w-full px-3 py-1.5 text-left text-sm cursor-pointer hover:bg-blue-50 hover:text-blue-800 dark:hover:bg-blue-500/15 dark:hover:text-blue-200"
                  >
                    <FileText className="h-3.5 w-3.5 text-zinc-500 shrink-0" aria-hidden />
                    <span className="font-mono text-xs text-zinc-500 truncate">{e.ref}</span>
                    <span className="flex-1 truncate">{e.title}</span>
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

export function ItemPicker({
  items,
  value,
  onChange,
  label = 'Item',
  filter,
}: {
  items: ItemSummary[]
  value: string | null
  onChange: (id: string | null) => void
  label?: string
  filter?: (i: ItemSummary) => boolean
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const current = value ? items.find((i) => i.id === value) : null
  const pool = filter ? items.filter(filter) : items

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const base = q
      ? pool.filter(
          (i) =>
            i.title.toLowerCase().includes(q) ||
            i.shortId.toLowerCase().includes(q) ||
            i.id.toLowerCase().includes(q),
        )
      : pool
    return base.slice(0, 25)
  }, [pool, query])

  return (
    <div>
      <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
        {label}
      </label>
      {current ? (
        <div className={`flex items-center gap-2 px-3 py-2 rounded border ${ACTIVE_PILL}`}>
          <span className="font-mono text-xs text-zinc-500">{current.shortId}</span>
          <span className="text-[10px] uppercase text-zinc-500">{current.type}</span>
          <span className="flex-1 truncate text-sm">{current.title}</span>
          <button
            type="button"
            onClick={() => onChange(null)}
            title="Clear"
            aria-label="Clear"
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
            onBlur={() => setOpen(false)}
            placeholder="Search by title or short id…"
            className="w-full px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
          />
          {open && filtered.length > 0 && (
            <ul
              onMouseDown={keepInputFocus}
              className="absolute left-0 right-0 mt-1 max-h-56 overflow-auto rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 shadow-xl ring-1 ring-black/5 dark:ring-white/10 py-1 z-10"
            >
              {filtered.map((i) => (
                <li key={i.id}>
                  <button
                    type="button"
                    onClick={() => {
                      onChange(i.id)
                      setOpen(false)
                      setQuery('')
                    }}
                    className="flex items-center gap-2 w-full px-3 py-1.5 text-left text-sm cursor-pointer hover:bg-blue-50 hover:text-blue-800 dark:hover:bg-blue-500/15 dark:hover:text-blue-200"
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

// Picker over every project's milestone sub-docs. The value is the qualified
// ref `<project>/<slug>` — unambiguous across projects, and exactly what the
// link endpoint + `--milestone` filter resolve.
function MilestonePicker({
  milestones,
  value,
  onChange,
}: {
  milestones: Milestone[]
  value: string | null
  onChange: (ref: string | null) => void
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const refOf = (m: Milestone) => `${m.project}/${m.slug}`
  const current = value ? milestones.find((m) => refOf(m) === value) ?? null : null

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const base = q
      ? milestones.filter(
          (m) =>
            m.title.toLowerCase().includes(q) ||
            refOf(m).toLowerCase().includes(q),
        )
      : milestones
    return base.slice(0, 25)
  }, [milestones, query])

  return (
    <div>
      <label className="block text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
        Milestone
      </label>
      {current ? (
        <div className={`flex items-center gap-2 px-3 py-2 rounded border ${ACTIVE_PILL}`}>
          <Flag className="h-3.5 w-3.5 text-cyan-600 dark:text-cyan-400" aria-hidden />
          <span className="font-mono text-xs text-zinc-500">{refOf(current)}</span>
          <span className="flex-1 truncate text-sm">{current.title}</span>
          <button
            type="button"
            onClick={() => onChange(null)}
            title="Clear"
            aria-label="Clear"
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
            onBlur={() => setOpen(false)}
            placeholder="Search milestones by title or project/slug…"
            className="w-full px-3 py-2 rounded border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-sm"
          />
          {open && filtered.length > 0 && (
            <ul
              onMouseDown={keepInputFocus}
              className="absolute left-0 right-0 mt-1 max-h-56 overflow-auto rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 shadow-xl ring-1 ring-black/5 dark:ring-white/10 py-1 z-10"
            >
              {filtered.map((m) => (
                <li key={refOf(m)}>
                  <button
                    type="button"
                    onClick={() => {
                      onChange(refOf(m))
                      setOpen(false)
                      setQuery('')
                    }}
                    className="flex items-center gap-2 w-full px-3 py-1.5 text-left text-sm cursor-pointer hover:bg-blue-50 hover:text-blue-800 dark:hover:bg-blue-500/15 dark:hover:text-blue-200"
                  >
                    <Flag className="h-3.5 w-3.5 text-cyan-600 dark:text-cyan-400 shrink-0" aria-hidden />
                    <span className="font-mono text-xs text-zinc-500 truncate">{refOf(m)}</span>
                    <span className="flex-1 truncate">{m.title}</span>
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
