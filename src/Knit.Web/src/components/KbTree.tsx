import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Check,
  ChevronDown,
  ChevronRight,
  ChevronsDownUp,
  ChevronsUpDown,
  FileText,
  Folder,
  FolderOpen,
  GripVertical,
  Plus,
  RefreshCw,
  Tag,
  Trash2,
  X,
} from 'lucide-react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { dropOnlyWhereHovered } from '../lib/dndCollision'
import { DragGhost } from './DragGhost'
import { checkKbDrop, kbDropSlug, type KbDragPayload, type KbDropTarget } from '../lib/kbDnd'
import { DROP_OK } from '../lib/dropVerdict'
import { DropCheckContext, dropAnnouncements, useDropCheck, type DropCheck } from '../hooks/useDropVerdict'
import { allowsGrowToFolder, kbApi, kbQk, type KbBucket, type KbDeleteResult, type KbTreeNode } from '../api/kb'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'
import { useTreeOpen } from '../stores/treeOpen'
import { slugify } from '../lib/slug'
import { useArrowNav } from '../hooks/useArrowNav'
import { useAutoFocusOnData } from '../hooks/useAutoFocusOnData'
import type { NavRow } from '../lib/arrowNav'
import { KbDeleteDialog } from './KbDeleteDialog'
import { TagBadges } from './TagBadges'
import { RowLink } from './RowLink'
import { KB_BUCKET_ICON } from '../lib/glyphs'

const BUCKET_BY_FOLDER: Record<string, KbBucket> = {
  features: 'feature',
  docs: 'doc',
  adrs: 'adr',
  logs: 'log',
  ideas: 'idea',
  sources: 'source',
}

interface Props {
  selected: SelectedKb | null
  onSelect: (sel: SelectedKb) => void
}

export type SelectedKb =
  | { kind: 'folder'; subpath: string }
  | { kind: 'entry'; bucket: KbBucket; slug: string }

// An in-flight inline "+" create. `parentSlug` set => sub-doc of a folder-form
// entry; absent => top-level entry in the bucket folder. `source` is excluded —
// it needs URL/kind, so its "+" still opens the modal.
interface PendingNew {
  bucket: KbBucket
  parentSlug?: string
}

// relPaths of every node that has children — i.e. everything that can be
// expanded/collapsed (bucket folders and folder-form entries).
function collectExpandableRelPaths(nodes: KbTreeNode[], acc: Set<string>) {
  for (const n of nodes) {
    if (n.children.length > 0) {
      acc.add(n.relPath)
      collectExpandableRelPaths(n.children, acc)
    }
  }
}

// relPaths of every node, expandable or not — a folder-form entry without
// sub-docs yet is opened for an inline create. Drives stale-id pruning, which
// must know the *whole* tree, not just the nodes with children.
function collectAllRelPaths(nodes: KbTreeNode[], acc: Set<string>) {
  for (const n of nodes) {
    acc.add(n.relPath)
    collectAllRelPaths(n.children, acc)
  }
}

// Trail of ancestor relPaths that must be expanded for the entry identified by
// (bucket, slug) to be visible. Returns null when the entry isn't in the tree.
function findEntryAncestors(
  nodes: KbTreeNode[],
  bucket: KbBucket,
  slug: string,
  trail: string[] = [],
): string[] | null {
  for (const n of nodes) {
    const nSlug = n.ref ? n.ref.split(':', 2)[1] : null
    if (!n.isFolder && n.bucket === bucket && nSlug === slug) return trail
    const found = findEntryAncestors(n.children, bucket, slug, [...trail, n.relPath])
    if (found) return found
  }
  return null
}

function kbSelKey(s: SelectedKb): string {
  return s.kind === 'folder' ? `f:${s.subpath}` : `e:${s.bucket}:${s.slug}`
}

// Flatten the (already tag-filtered) tree into visible rows in render order for
// the shared arrow-nav core, honouring the current expanded set. The root
// folder is always present; a folder-form entry expands to its sub-docs.
function flattenKbRows(nodes: KbTreeNode[], expanded: Set<string>): NavRow<SelectedKb>[] {
  const rows: NavRow<SelectedKb>[] = [
    {
      key: '',
      parentKey: null,
      expandable: true,
      expanded: expanded.has(''),
      data: { kind: 'folder', subpath: '' },
    },
  ]
  const walk = (ns: KbTreeNode[], parentKey: string) => {
    for (const n of ns) {
      const isExpanded = expanded.has(n.relPath)
      if (n.isFolder) {
        rows.push({
          key: n.relPath,
          parentKey,
          expandable: n.children.length > 0,
          expanded: isExpanded,
          data: { kind: 'folder', subpath: n.relPath },
        })
        if (isExpanded) walk(n.children, n.relPath)
      } else {
        const slug = n.ref ? n.ref.split(':', 2)[1]! : null
        const isFolderForm = !!n.filePath && /[\\/]readme\.md$/i.test(n.filePath)
        const hasChildren = n.children.length > 0
        if (slug && n.bucket) {
          rows.push({
            key: n.relPath,
            parentKey,
            expandable: isFolderForm,
            expanded: isFolderForm && isExpanded,
            data: { kind: 'entry', bucket: n.bucket as KbBucket, slug },
          })
        }
        // Non-folder-form entries render their children unconditionally (912).
        if (hasChildren && (isFolderForm ? isExpanded : true)) walk(n.children, n.relPath)
      }
    }
  }
  if (expanded.has('')) walk(nodes, '')
  return rows
}

type TagMode = 'all' | 'any'

// Above this many distinct tags the inline chip row is replaced by a
// searchable popover so the filter bar stays one line.
// Level 0 of the KB tree: the bucket root. A module constant because it is a
// render dependency of the persisted-expand hook.
const KB_SEED = ['']

const INLINE_TAG_LIMIT = 12

function collectAllTags(nodes: KbTreeNode[], acc: Set<string>) {
  for (const n of nodes) {
    for (const t of n.tags) acc.add(t)
    collectAllTags(n.children, acc)
  }
}

// Count how many entries carry each tag within the given (already
// tag-filtered) subtree — used to order offered tags by frequency and to
// narrow them to those that co-occur with the current selection.
function countTags(nodes: KbTreeNode[], acc: Map<string, number>) {
  for (const n of nodes) {
    for (const t of n.tags) acc.set(t, (acc.get(t) ?? 0) + 1)
    countTags(n.children, acc)
  }
}

// Prune the tree to entries whose own tags satisfy the filter, keeping the
// ancestor folders/entries that lead to a match. Folders carry no tags, so
// they survive only via a matching descendant.
function filterTreeByTags(
  nodes: KbTreeNode[],
  selected: string[],
  mode: TagMode,
): KbTreeNode[] {
  const matches = (n: KbTreeNode) =>
    mode === 'all'
      ? selected.every((t) => n.tags.includes(t))
      : selected.some((t) => n.tags.includes(t))
  const walk = (ns: KbTreeNode[]): KbTreeNode[] => {
    const out: KbTreeNode[] = []
    for (const n of ns) {
      const keptChildren = walk(n.children)
      if (keptChildren.length > 0 || matches(n)) {
        out.push({ ...n, children: keptChildren })
      }
    }
    return out
  }
  return walk(nodes)
}

const INLINE_BUCKETS: ReadonlySet<KbBucket> = new Set<KbBucket>([
  'feature',
  'doc',
  'idea',
  'log',
  'adr',
])

export function KbTree({ selected, onSelect }: Props) {
  const tree = useQuery({
    queryKey: kbQk.tree,
    queryFn: kbApi.tree,
  })
  // Spin only for a refresh the user asked for, not for background refetches.
  const [refreshing, setRefreshing] = useState(false)
  const openNewKb = useDialogs((s) => s.openNewKb)
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  // Expand state is persisted per repo. The root ('') is level 0
  // here — what *collapse all* leaves standing — so it seeds a repo that has
  // never persisted this tree.
  const { ready, expanded, toggle, replace, expand, prune } = useTreeOpen('kb', KB_SEED)
  const [pendingNew, setPendingNew] = useState<PendingNew | null>(null)
  const [deleteRef, setDeleteRef] = useState<string | null>(null)

  const requestDelete = useCallback((ref: string) => setDeleteRef(ref), [])

  const onDeleted = useCallback(
    (result: KbDeleteResult) => {
      setDeleteRef(null)
      toast(
        'success',
        `Deleted ${result.ref}${
          result.itemsAffected.length > 0 ? ` (${result.itemsAffected.length} item(s) updated)` : ''
        }`,
      )
      void qc.invalidateQueries({ queryKey: ['kb'] })
      void qc.refetchQueries({ queryKey: kbQk.tree })
      // If the deleted entry was the selected one, fall back to the master index.
      if (selected?.kind === 'entry' && `${selected.bucket}:${selected.slug}` === result.ref)
        onSelect({ kind: 'folder', subpath: '' })
    },
    [qc, toast, selected, onSelect],
  )

  const requestCreate = useCallback(
    (bucket: KbBucket, opts?: { parentSlug?: string; expandRelPath?: string }) => {
      if (opts?.expandRelPath !== undefined) expand([opts.expandRelPath])
      setPendingNew({ bucket, parentSlug: opts?.parentSlug })
    },
    [expand],
  )

  const cancelCreate = useCallback(() => setPendingNew(null), [])

  const commitCreate = useCallback(
    async (title: string): Promise<boolean> => {
      if (!pendingNew) return false
      const { bucket, parentSlug } = pendingNew
      try {
        // Trailing-slash sentinel => server derives the leaf slug from title.
        const slug = parentSlug ? `${parentSlug}/` : undefined
        const entry = await kbApi.create({ bucket, title, slug })
        setPendingNew(null)
        await qc.refetchQueries({ queryKey: kbQk.tree })
        await qc.invalidateQueries({ queryKey: ['kb'] })
        onSelect({ kind: 'entry', bucket: entry.bucket, slug: entry.slug })
        toast('success', `Created ${entry.ref}`)
        return true
      } catch (err) {
        toast('error', `Create failed: ${String(err)}`)
        return false
      }
    },
    [pendingNew, qc, onSelect, toast],
  )

  const nodes = useMemo(() => tree.data ?? [], [tree.data])

  const expandableRelPaths = useMemo(() => {
    const s = new Set<string>()
    collectExpandableRelPaths(nodes, s)
    return s
  }, [nodes])
  const hasExpandable = expandableRelPaths.size > 0

  // The KB tree endpoint is never filtered (the tag filter is applied
  // client-side, below), so `nodes` is always a complete node set and stale
  // relPaths — a renamed entry, a split doc — can be dropped on every load.
  useEffect(() => {
    if (!ready || !tree.data) return
    const alive = new Set<string>([''])
    collectAllRelPaths(nodes, alive)
    prune(alive)
  }, [ready, tree.data, nodes, prune])

  // Root ('') stays expanded so the bucket folders remain visible.
  const expandAll = useCallback(
    () => replace(['', ...expandableRelPaths]),
    [replace, expandableRelPaths],
  )
  const collapseAll = useCallback(() => replace(['']), [replace])

  // --- tag filtering ---
  const [selectedTags, setSelectedTags] = useState<Set<string>>(new Set())
  const [tagMode, setTagMode] = useState<TagMode>('all')
  const allTags = useMemo(() => {
    const s = new Set<string>()
    collectAllTags(nodes, s)
    return [...s].sort((a, b) => a.localeCompare(b))
  }, [nodes])
  const toggleTag = useCallback((tag: string) => {
    setSelectedTags((prev) => {
      const next = new Set(prev)
      if (next.has(tag)) next.delete(tag)
      else next.add(tag)
      return next
    })
  }, [])
  const clearTags = useCallback(() => setSelectedTags(new Set()), [])

  const filterActive = selectedTags.size > 0
  const displayNodes = useMemo(
    () => (filterActive ? filterTreeByTags(nodes, [...selectedTags], tagMode) : nodes),
    [filterActive, nodes, selectedTags, tagMode],
  )

  // Offered tags: those that co-occur in the currently-filtered set (plus the
  // selected ones so they stay removable), ordered by frequency then name.
  const offeredTags = useMemo(() => {
    const counts = new Map<string, number>()
    countTags(displayNodes, counts)
    for (const t of selectedTags) if (!counts.has(t)) counts.set(t, 0)
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .map(([tag, count]) => ({ tag, count }))
  }, [displayNodes, selectedTags])
  // Inline vs popover is decided by the total tag count (stable) so the bar
  // doesn't flip modes as co-occurrence narrowing shrinks the offered list.
  const usePopover = allTags.length > INLINE_TAG_LIMIT
  // While a filter is active, force every surviving folder/entry open so the
  // matches are visible without manual expansion. Union in the user's manual
  // `expanded` set too, so a folder-form entry without sub-docs (never in
  // collectExpandableRelPaths) still opens for an inline create.
  const viewExpanded = useMemo(() => {
    if (!filterActive) return expanded
    const s = new Set<string>([''])
    collectExpandableRelPaths(displayNodes, s)
    for (const p of expanded) s.add(p)
    return s
  }, [filterActive, displayNodes, expanded])

  // Reveal the selected entry when the selection changes from outside the tree
  // (jump-to-ref, search, markdown link): expand its collapsed ancestor folders
  // and scroll its row into view. `block: 'nearest'` is a no-op when already
  // visible, so an in-tree click doesn't yank the viewport.
  const scrollRef = useRef<HTMLDivElement>(null)
  const pendingReveal = useRef<string | null>(null)

  const scrollToReveal = useCallback(() => {
    const ref = pendingReveal.current
    if (!ref) return
    const el = scrollRef.current?.querySelector<HTMLElement>(`[data-reveal-ref="${ref}"]`)
    if (el) {
      el.scrollIntoView({ block: 'nearest' })
      pendingReveal.current = null
    }
  }, [])

  useEffect(() => {
    if (selected?.kind !== 'entry') return
    const ancestors = findEntryAncestors(nodes, selected.bucket, selected.slug)
    // External sync: reacts to URL-driven selection + async tree data. `expand`
    // bails when every needed path is already open, so no cascade.
    if (ancestors) expand(['', ...ancestors])
    pendingReveal.current = `${selected.bucket}:${selected.slug}`
    scrollToReveal()
  }, [selected, nodes, scrollToReveal, expand])

  // Retry the pending reveal once the ancestor-expand has re-rendered the row.
  useEffect(() => {
    scrollToReveal()
  }, [viewExpanded, scrollToReveal])

  // Keyboard navigation, same model as the items TreeView: ↑/↓ between visible
  // rows, ←/→ collapse-or-go-to-parent / expand-or-go-to-first-child.
  const flatRows = useMemo(
    () => flattenKbRows(displayNodes, viewExpanded),
    [displayNodes, viewExpanded],
  )
  // relPath of the selected node — the hook's cursor is keyed by row.
  const selRelPath = useMemo(() => {
    const selK = selected ? kbSelKey(selected) : null
    if (!selK) return null
    const row = flatRows.find((r) => kbSelKey(r.data) === selK)
    return row ? row.key : null
  }, [flatRows, selected])

  const { onKeyDown: onTreeKeyDown } = useArrowNav<SelectedKb>({
    rows: flatRows,
    selectedKey: selRelPath,
    onActivate: (row) => onSelect(row.data),
    onToggle: toggle,
  })

  useAutoFocusOnData(scrollRef, tree.data)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  )
  const [drag, setDrag] = useState<KbDragPayload | null>(null)

  const onDragStart = useCallback((e: DragStartEvent) => {
    const data = e.active.data.current as KbDragPayload | undefined
    if (data) setDrag(data)
  }, [])

  const dropCheck = useCallback<DropCheck>(
    (target) => (drag ? checkKbDrop(drag, target as KbDropTarget) : DROP_OK),
    [drag],
  )
  const accessibility = useMemo(() => ({ announcements: dropAnnouncements(dropCheck) }), [dropCheck])

  const onDragEnd = useCallback(
    async (e: DragEndEvent) => {
      const payload = drag
      setDrag(null)
      if (!payload || !e.over) return
      const target = e.over.data.current as KbDropTarget | undefined
      if (!target) return

      // A refused or no-op drop was already shown on the ghost while hovering.
      const newSlug = kbDropSlug(payload, target)
      if (newSlug === null) return

      try {
        await kbApi.rename(payload.ref, newSlug)
        await qc.refetchQueries({ queryKey: kbQk.tree })
        await qc.invalidateQueries({ queryKey: ['kb'] })
        toast('success', `Moved to ${payload.bucket}:${newSlug}`)
      } catch (err) {
        toast('error', `Move failed: ${String(err)}`)
      }
    },
    [drag, qc, toast],
  )

  return (
    <div className="flex flex-col h-full min-h-0">
      {deleteRef && (
        <KbDeleteDialog
          entryRef={deleteRef}
          onClose={() => setDeleteRef(null)}
          onDeleted={onDeleted}
        />
      )}
      <div className="flex items-center gap-1 px-2 py-1 border-b border-zinc-200 dark:border-zinc-800 text-xs">
        <span className="text-zinc-500 font-medium">.knit/kb/</span>
        <div className="ml-auto flex items-center gap-1">
          {hasExpandable && (
            <>
              <button
                type="button"
                onClick={expandAll}
                title="Expand all"
                aria-label="Expand all"
                className="p-1 rounded text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800"
              >
                <ChevronsUpDown className="h-3.5 w-3.5" />
              </button>
              <button
                type="button"
                onClick={collapseAll}
                title="Collapse all"
                aria-label="Collapse all"
                className="p-1 rounded text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800"
              >
                <ChevronsDownUp className="h-3.5 w-3.5" />
              </button>
            </>
          )}
          <button
            type="button"
            onClick={() => openNewKb()}
            title="New KB entry"
            aria-label="New KB entry"
            className="p-1 rounded text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <Plus className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={() => {
              setRefreshing(true)
              void tree.refetch().finally(() => setRefreshing(false))
            }}
            title="Refresh tree"
            aria-label="Refresh tree"
            className="p-1 rounded text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <RefreshCw className={'h-3.5 w-3.5 ' + (refreshing ? 'animate-spin' : '')} />
          </button>
        </div>
      </div>
      {allTags.length > 0 && (
        <TagFilterBar
          tags={offeredTags}
          usePopover={usePopover}
          selected={selectedTags}
          mode={tagMode}
          onToggle={toggleTag}
          onSetMode={setTagMode}
          onClear={clearTags}
        />
      )}
      <DropCheckContext.Provider value={dropCheck}>
        <DndContext
          sensors={sensors}
          collisionDetection={dropOnlyWhereHovered}
          accessibility={accessibility}
          onDragStart={onDragStart}
          onDragEnd={onDragEnd}
          onDragCancel={() => setDrag(null)}
        >
          <div
            ref={scrollRef}
            tabIndex={0}
            onKeyDown={onTreeKeyDown}
            className="flex-1 min-h-0 overflow-y-auto py-1 text-sm focus:outline-none"
          >
            <FolderRow
              relPath=""
              name="(root)"
              depth={0}
              expanded={ready && viewExpanded.has('')}
              onToggle={() => toggle('')}
              isActive={selected?.kind === 'folder' && selected.subpath === ''}
              onSelect={() => onSelect({ kind: 'folder', subpath: '' })}
              drag={drag}
              requestCreate={requestCreate}
            />
            {ready && viewExpanded.has('') && (
              <Nodes
                nodes={displayNodes}
                depth={1}
                expanded={viewExpanded}
                toggle={toggle}
                selected={selected}
                onSelect={onSelect}
                drag={drag}
                pendingNew={pendingNew}
                requestCreate={requestCreate}
                cancelCreate={cancelCreate}
                commitCreate={commitCreate}
                requestDelete={requestDelete}
              />
            )}
            {ready && displayNodes.length === 0 && !tree.isLoading && (
              <div className="px-3 py-3 text-xs text-zinc-500">
                {filterActive ? 'no entries match the selected tags' : 'no KB entries'}
              </div>
            )}
          </div>
          <DragOverlay>{drag ? <KbDragGhost entryRef={drag.ref} /> : null}</DragOverlay>
        </DndContext>
      </DropCheckContext.Provider>
    </div>
  )
}

interface OfferedTag {
  tag: string
  count: number
}

const SELECTED_CHIP_CAP = 4

function TagFilterBar({
  tags,
  usePopover,
  selected,
  mode,
  onToggle,
  onSetMode,
  onClear,
}: {
  tags: OfferedTag[]
  usePopover: boolean
  selected: Set<string>
  mode: TagMode
  onToggle: (tag: string) => void
  onSetMode: (mode: TagMode) => void
  onClear: () => void
}) {
  const active = selected.size > 0
  return (
    <div className="flex flex-wrap items-center gap-1 px-2 py-1.5 border-b border-zinc-200 dark:border-zinc-800">
      <Tag className="h-3.5 w-3.5 text-zinc-400 shrink-0" aria-hidden />
      <button
        type="button"
        onClick={() => onSetMode(mode === 'all' ? 'any' : 'all')}
        title={
          mode === 'all'
            ? 'Match ALL selected tags — an entry must carry every selected tag. Click to switch to ANY.'
            : 'Match ANY selected tag — an entry needs at least one selected tag. Click to switch to ALL.'
        }
        className="shrink-0 inline-flex items-center leading-none h-6 text-[10px] font-semibold uppercase tracking-wide px-2 rounded border border-zinc-300 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
      >
        {mode === 'all' ? 'ALL' : 'ANY'}
      </button>
      {usePopover ? (
        <>
          <TagFilterPopover tags={tags} selected={selected} onToggle={onToggle} />
          <SelectedTagChips selected={selected} onToggle={onToggle} />
        </>
      ) : (
        tags.map(({ tag }) => {
          const on = selected.has(tag)
          return (
            <button
              key={tag}
              type="button"
              onClick={() => onToggle(tag)}
              aria-pressed={on}
              className={
                'text-[11px] rounded-full px-2 py-0.5 font-mono border transition-colors ' +
                (on
                  ? 'bg-blue-600 border-blue-600 text-white'
                  : 'bg-zinc-100 dark:bg-zinc-800 border-zinc-200 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:border-zinc-400 dark:hover:border-zinc-600')
              }
            >
              {tag}
            </button>
          )
        })
      )}
      {active && (
        <button
          type="button"
          onClick={onClear}
          title="Clear tag filter"
          aria-label="Clear tag filter"
          className="shrink-0 inline-flex items-center text-[10px] px-1 py-0.5 rounded text-zinc-500 hover:text-red-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
        >
          <X className="h-3 w-3" />
        </button>
      )}
    </div>
  )
}

// Selected tags shown as removable chips next to the popover button, capped
// so the bar can't grow unbounded; the overflow collapses into "+N".
function SelectedTagChips({
  selected,
  onToggle,
}: {
  selected: Set<string>
  onToggle: (tag: string) => void
}) {
  const arr = [...selected]
  if (arr.length === 0) return null
  const shown = arr.slice(0, SELECTED_CHIP_CAP)
  const extra = arr.length - shown.length
  return (
    <>
      {shown.map((tag) => (
        <button
          key={tag}
          type="button"
          onClick={() => onToggle(tag)}
          title={`Remove ${tag}`}
          aria-label={`Remove ${tag}`}
          className="inline-flex items-center gap-0.5 text-[11px] rounded-full px-2 py-0.5 font-mono bg-blue-600 text-white"
        >
          {tag}
          <X className="h-2.5 w-2.5" />
        </button>
      ))}
      {extra > 0 && <span className="text-[10px] text-zinc-500">+{extra}</span>}
    </>
  )
}

// Searchable, frequency-ordered checkable tag list. Used when there are too
// many distinct tags to show inline.
function TagFilterPopover({
  tags,
  selected,
  onToggle,
}: {
  tags: OfferedTag[]
  selected: Set<string>
  onToggle: (tag: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const ref = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  useEffect(() => {
    if (open) inputRef.current?.focus()
  }, [open])

  const needle = q.trim().toLowerCase()
  const filtered = needle ? tags.filter((t) => t.tag.toLowerCase().includes(needle)) : tags

  return (
    <div ref={ref} className="relative shrink-0 flex items-center">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        title="Filter by tag"
        className="inline-flex items-center gap-1 leading-none h-6 text-[11px] px-2 rounded border border-zinc-300 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
      >
        Tags
        {selected.size > 0 && (
          <span className="rounded-full bg-blue-600 text-white text-[10px] px-1.5 leading-tight">
            {selected.size}
          </span>
        )}
        <ChevronDown className="h-3 w-3" />
      </button>
      {open && (
        <div className="absolute z-20 mt-1 left-0 w-56 max-h-72 flex flex-col rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 shadow-lg">
          <div className="p-1.5 border-b border-zinc-200 dark:border-zinc-800">
            <input
              ref={inputRef}
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Filter tags…"
              aria-label="Filter tags"
              className="w-full text-[11px] px-1.5 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>
          <div className="overflow-y-auto py-1">
            {filtered.length === 0 && (
              <div className="px-2 py-2 text-[11px] text-zinc-500">no matching tags</div>
            )}
            {filtered.map(({ tag, count }) => {
              const on = selected.has(tag)
              return (
                <button
                  key={tag}
                  type="button"
                  onClick={() => onToggle(tag)}
                  aria-pressed={on}
                  className={
                    'w-full flex items-center gap-2 px-2 py-1 text-left text-[11px] ' +
                    (on
                      ? 'bg-blue-50 dark:bg-blue-950/40'
                      : 'hover:bg-zinc-100 dark:hover:bg-zinc-800')
                  }
                >
                  <span
                    className={
                      'flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded border ' +
                      (on
                        ? 'bg-blue-600 border-blue-600 text-white'
                        : 'border-zinc-400 dark:border-zinc-600')
                    }
                  >
                    {on && <Check className="h-2.5 w-2.5" />}
                  </span>
                  <span className="font-mono truncate flex-1">{tag}</span>
                  <span className="text-zinc-400 tabular-nums">{count}</span>
                </button>
              )
            })}
          </div>
        </div>
      )}
    </div>
  )
}

function Nodes({
  nodes,
  depth,
  expanded,
  toggle,
  selected,
  onSelect,
  drag,
  pendingNew,
  requestCreate,
  cancelCreate,
  commitCreate,
  requestDelete,
}: {
  nodes: KbTreeNode[]
  depth: number
  expanded: Set<string>
  toggle: (relPath: string) => void
  selected: SelectedKb | null
  onSelect: (sel: SelectedKb) => void
  drag: KbDragPayload | null
  pendingNew: PendingNew | null
  requestCreate: (bucket: KbBucket, opts?: { parentSlug?: string; expandRelPath?: string }) => void
  cancelCreate: () => void
  commitCreate: (title: string) => Promise<boolean>
  requestDelete: (ref: string) => void
}) {
  const openNewKb = useDialogs((s) => s.openNewKb)
  return (
    <>
      {nodes.map((n) => {
        if (n.isFolder) {
          const isExpanded = expanded.has(n.relPath)
          const isActive = selected?.kind === 'folder' && selected.subpath === n.relPath
          const folderBucket = depth === 1 ? BUCKET_BY_FOLDER[n.name] : undefined
          const showInlineHere =
            !!folderBucket &&
            !!pendingNew &&
            pendingNew.bucket === folderBucket &&
            pendingNew.parentSlug === undefined
          return (
            <div key={n.relPath}>
              <FolderRow
                relPath={n.relPath}
                name={n.name}
                depth={depth}
                expanded={isExpanded}
                onToggle={() => toggle(n.relPath)}
                isActive={isActive}
                onSelect={() => onSelect({ kind: 'folder', subpath: n.relPath })}
                drag={drag}
                requestCreate={requestCreate}
              />
              {isExpanded && (
                <>
                  {showInlineHere && (
                    <InlineCreateRow
                      bucket={folderBucket}
                      depth={depth + 1}
                      onCommit={commitCreate}
                      onCancel={cancelCreate}
                    />
                  )}
                  <Nodes
                    nodes={n.children}
                    depth={depth + 1}
                    expanded={expanded}
                    toggle={toggle}
                    selected={selected}
                    onSelect={onSelect}
                    drag={drag}
                    pendingNew={pendingNew}
                    requestCreate={requestCreate}
                    cancelCreate={cancelCreate}
                    commitCreate={commitCreate}
                    requestDelete={requestDelete}
                  />
                </>
              )}
            </div>
          )
        }
        const active =
          selected?.kind === 'entry' &&
          n.bucket === selected.bucket &&
          n.ref?.split(':', 2)[1] === selected.slug
        const slug = n.ref ? n.ref.split(':', 2)[1]! : null
        const navigable = !!(n.bucket && slug)
        const isFolderForm = !!n.filePath && /[\\/]readme\.md$/i.test(n.filePath)
        const isExpanded = expanded.has(n.relPath)
        const canHostSubdocs =
          isFolderForm && allowsGrowToFolder(n.bucket)
        return (
          <EntryRow
            key={n.relPath}
            node={n}
            depth={depth}
            slug={slug}
            navigable={navigable}
            active={active}
            isFolderForm={isFolderForm}
            isExpanded={isExpanded}
            canHostSubdocs={canHostSubdocs}
            onToggleExpand={() => toggle(n.relPath)}
            onSelect={() => {
              if (navigable) onSelect({ kind: 'entry', bucket: n.bucket as KbBucket, slug: slug! })
            }}
            onAddSubdoc={() => {
              if (!n.bucket || !slug) return
              const bucket = n.bucket as KbBucket
              // A source sub-doc needs URL/kind up front, like a top-level
              // one — inline title-only create can't supply them.
              if (INLINE_BUCKETS.has(bucket))
                requestCreate(bucket, { parentSlug: slug, expandRelPath: n.relPath })
              else openNewKb({ bucket, parentRef: { bucket, slug } })
            }}
            onDelete={n.ref ? () => requestDelete(n.ref!) : undefined}
            drag={drag}
          >
            {pendingNew &&
              pendingNew.bucket === n.bucket &&
              pendingNew.parentSlug === slug &&
              isExpanded && (
                <InlineCreateRow
                  bucket={n.bucket as KbBucket}
                  depth={depth + 1}
                  parentSlug={slug ?? undefined}
                  onCommit={commitCreate}
                  onCancel={cancelCreate}
                />
              )}
            {n.children.length > 0 && (isFolderForm ? isExpanded : true) && (
              <Nodes
                nodes={n.children}
                depth={depth + 1}
                expanded={expanded}
                toggle={toggle}
                selected={selected}
                onSelect={onSelect}
                drag={drag}
                pendingNew={pendingNew}
                requestCreate={requestCreate}
                cancelCreate={cancelCreate}
                commitCreate={commitCreate}
                requestDelete={requestDelete}
              />
            )}
          </EntryRow>
        )
      })}
    </>
  )
}

function EntryRow({
  node,
  depth,
  slug,
  navigable,
  active,
  isFolderForm,
  isExpanded,
  canHostSubdocs,
  onToggleExpand,
  onSelect,
  onAddSubdoc,
  onDelete,
  drag,
  children,
}: {
  node: KbTreeNode
  depth: number
  slug: string | null
  navigable: boolean
  active: boolean
  isFolderForm: boolean
  isExpanded: boolean
  canHostSubdocs: boolean
  onToggleExpand: () => void
  onSelect: () => void
  onAddSubdoc: () => void
  onDelete?: () => void
  drag: KbDragPayload | null
  children: React.ReactNode
}) {
  const ref = node.ref
  const bucket = node.bucket as KbBucket | null
  const isDraggable = !!(ref && bucket && slug)
  const payload: KbDragPayload | undefined = isDraggable
    ? { ref: ref!, bucket: bucket!, slug: slug!, title: node.title ?? node.name }
    : undefined

  const {
    attributes,
    listeners,
    setNodeRef: setDragRef,
    isDragging,
  } = useDraggable({
    id: `kb-entry:${node.relPath}`,
    data: payload,
    disabled: !isDraggable,
  })

  // Folder-form entries take sub-docs; flat ones are drop zones too, only so
  // the ghost can say why they refuse and how to fix it.
  const dropData: KbDropTarget | undefined =
    bucket && slug
      ? isFolderForm
        ? { kind: 'entry', bucket, parentSlug: slug }
        : { kind: 'flat', bucket, slug }
      : undefined
  const { setNodeRef: setDropRef, isOver } = useDroppable({
    id: `kb-drop:${node.relPath}`,
    data: dropData,
    disabled: !dropData,
  })
  const check = useDropCheck()
  const validDropHover = !!(isOver && drag && dropData) && (check?.(dropData) ?? DROP_OK).kind === 'ok'

  const beingDragged = isDragging

  return (
    <div className="group/row">
      <div
        ref={setDropRef}
        data-reveal-ref={ref ?? undefined}
        className={
          'w-full flex items-center pr-1 py-1 transition-colors ' +
          (active
            ? 'bg-blue-100 dark:bg-blue-900/40 text-blue-900 dark:text-blue-100'
            : 'hover:bg-zinc-100 dark:hover:bg-zinc-800/60 text-zinc-700 dark:text-zinc-300') +
          (validDropHover ? ' ring-1 ring-inset ring-blue-500 bg-blue-50 dark:bg-blue-950/40' : '') +
          (beingDragged ? ' opacity-40' : '')
        }
        style={{ paddingLeft: depth * 14 + 4 }}
      >
        {isFolderForm ? (
          <button
            type="button"
            onClick={onToggleExpand}
            aria-label={isExpanded ? 'Collapse' : 'Expand'}
            className="p-0.5 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
          >
            {isExpanded ? (
              <ChevronDown className="h-3.5 w-3.5" />
            ) : (
              <ChevronRight className="h-3.5 w-3.5" />
            )}
          </button>
        ) : (
          <span className="w-[18px] shrink-0" aria-hidden />
        )}
        {isDraggable && (
          <button
            ref={setDragRef}
            {...attributes}
            {...listeners}
            type="button"
            aria-label="Drag handle"
            title="Drag to move"
            className="opacity-0 group-hover/row:opacity-100 focus:opacity-100 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-grab active:cursor-grabbing shrink-0"
          >
            <GripVertical className="h-3.5 w-3.5" />
          </button>
        )}
        <RowLink
          to="/kb"
          search={{ bucket, slug }}
          onSelect={onSelect}
          onDoubleClick={isFolderForm ? () => onToggleExpand() : undefined}
          disabled={!navigable}
          className="flex-1 text-left flex items-center gap-1.5 min-w-0 select-none"
          title={node.title ?? node.name}
        >
          {isFolderForm ? (
            <FolderOpen
              className="h-3.5 w-3.5 shrink-0 text-blue-600 dark:text-blue-400"
              aria-hidden
            />
          ) : (
            <FileText className="h-3.5 w-3.5 shrink-0 text-zinc-500" aria-hidden />
          )}
          <span className="truncate">{node.title ?? node.name}</span>
          {/* An entry's own tags only. Folders carry none (the server sends an
              empty list, and the tag filter already treats them as tagless),
              so a union of the children's would invent metadata the KB does
              not have. Inside the select button, as in the item tree, so a
              click on a pill still selects the row. */}
          <TagBadges tags={node.tags} />
        </RowLink>
        <div className="flex items-center gap-1.5 shrink-0">
          {canHostSubdocs && slug && bucket && (
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                onAddSubdoc()
              }}
              title={`New sub-doc of ${bucket}:${slug}`}
              aria-label={`New sub-doc of ${bucket}:${slug}`}
              className="opacity-0 group-hover/row:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
            >
              <Plus className="h-3 w-3" />
            </button>
          )}
          {onDelete && ref && (
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                onDelete()
              }}
              title={`Delete ${ref}`}
              aria-label={`Delete ${ref}`}
              className="opacity-0 group-hover/row:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-red-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
            >
              <Trash2 className="h-3 w-3" />
            </button>
          )}
        </div>
      </div>
      {children}
    </div>
  )
}

// File-explorer-style inline create row: a single text input rendered at the
// right depth. Enter commits (server derives the slug from the title), Esc or
// blur discards the row — clicking elsewhere in the tree abandons an
// uncommitted entry. A failed commit keeps the row mounted with a red ring
// (the commit guards against blur firing mid-request via `busy`).
function InlineCreateRow({
  bucket,
  depth,
  parentSlug,
  onCommit,
  onCancel,
}: {
  bucket: KbBucket
  depth: number
  parentSlug?: string
  onCommit: (title: string) => Promise<boolean>
  onCancel: () => void
}) {
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  const title = value.trim()
  const preview = title ? slugify(title) : ''
  const slugHint = preview
    ? `Will be stored as ${parentSlug ? `${parentSlug}/` : ''}${preview}`
    : undefined

  const submit = async () => {
    if (!title || busy) return
    setBusy(true)
    setError(false)
    const ok = await onCommit(title)
    if (!ok) {
      setBusy(false)
      setError(true)
      inputRef.current?.focus()
    }
    // On success the parent clears pendingNew and this row unmounts.
  }

  return (
    <div
      className="w-full flex items-center pr-1 py-1"
      style={{ paddingLeft: depth * 14 + 4 }}
    >
      <span className="w-[1.125rem]" aria-hidden />
      <FileText className="h-3.5 w-3.5 shrink-0 text-zinc-500" aria-hidden />
      <input
        ref={inputRef}
        value={value}
        readOnly={busy}
        onChange={(e) => {
          setValue(e.target.value)
          setError(false)
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            void submit()
          } else if (e.key === 'Escape') {
            e.preventDefault()
            onCancel()
          }
        }}
        onBlur={() => {
          // Clicking elsewhere abandons an uncommitted entry. Guard against the
          // blur that fires while an Enter-triggered commit is still in flight.
          if (!busy) onCancel()
        }}
        placeholder={`New ${bucket}…`}
        title={slugHint}
        aria-label={`New ${bucket} title`}
        className={
          'ml-1.5 flex-1 min-w-0 bg-white dark:bg-zinc-900 rounded px-1 py-0.5 text-sm ' +
          'ring-1 ring-inset outline-none focus:ring-2 ' +
          (error
            ? 'ring-red-500 focus:ring-red-500'
            : 'ring-blue-500 focus:ring-blue-500')
        }
      />
    </div>
  )
}

function BucketAwareFolderIcon({
  name,
  depth,
  expanded,
}: {
  name: string
  depth: number
  expanded: boolean
}) {
  const folderBucket = depth === 1 ? BUCKET_BY_FOLDER[name] : undefined
  const bucketIcon = folderBucket ? KB_BUCKET_ICON[folderBucket] : undefined
  if (bucketIcon) {
    const Icon = bucketIcon
    return (
      <Icon className="h-3.5 w-3.5 shrink-0 text-blue-600 dark:text-blue-400" aria-hidden />
    )
  }
  return expanded ? (
    <FolderOpen className="h-3.5 w-3.5 shrink-0 text-amber-600 dark:text-amber-400" aria-hidden />
  ) : (
    <Folder className="h-3.5 w-3.5 shrink-0 text-amber-600 dark:text-amber-400" aria-hidden />
  )
}

function FolderRow({
  relPath: _relPath,
  name,
  depth,
  expanded,
  onToggle,
  isActive,
  onSelect,
  drag,
  requestCreate,
}: {
  relPath: string
  name: string
  depth: number
  expanded: boolean
  onToggle: () => void
  isActive: boolean
  onSelect: () => void
  drag: KbDragPayload | null
  requestCreate: (bucket: KbBucket, opts?: { parentSlug?: string; expandRelPath?: string }) => void
}) {
  const openNewKb = useDialogs((s) => s.openNewKb)
  const bucket = depth === 1 ? BUCKET_BY_FOLDER[name] : undefined

  const dropData: KbDropTarget | undefined = bucket
    ? { kind: 'bucket', bucket }
    : undefined
  const { setNodeRef: setDropRef, isOver } = useDroppable({
    id: `kb-folder-drop:${_relPath || '__root'}`,
    data: dropData,
    disabled: !dropData,
  })

  const check = useDropCheck()
  const validDropHover = !!(isOver && drag && dropData) && (check?.(dropData) ?? DROP_OK).kind === 'ok'

  return (
    <div
      ref={setDropRef}
      className={
        'group/row w-full flex items-center gap-1 py-1 transition-colors ' +
        (isActive
          ? 'bg-blue-100 dark:bg-blue-900/40 text-blue-900 dark:text-blue-100'
          : 'hover:bg-zinc-100 dark:hover:bg-zinc-800/60 text-zinc-700 dark:text-zinc-300') +
        (validDropHover ? ' ring-1 ring-inset ring-blue-500 bg-blue-50 dark:bg-blue-950/40' : '')
      }
      style={{ paddingLeft: depth * 14 + 4 }}
    >
      <button
        type="button"
        onClick={onToggle}
        aria-label={expanded ? 'Collapse' : 'Expand'}
        className="p-0.5 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
      >
        {expanded ? (
          <ChevronDown className="h-3.5 w-3.5" />
        ) : (
          <ChevronRight className="h-3.5 w-3.5" />
        )}
      </button>
      <RowLink
        to="/kb"
        search={{ folder: _relPath }}
        onSelect={onSelect}
        onDoubleClick={onToggle}
        className="flex-1 text-left flex items-center gap-1.5 min-w-0 select-none"
        title={name}
      >
        <BucketAwareFolderIcon name={name} depth={depth} expanded={expanded} />
        <span className="truncate font-medium">{name}</span>
      </RowLink>
      {bucket && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            // source needs URL/kind up front, so it keeps the full modal.
            if (INLINE_BUCKETS.has(bucket)) requestCreate(bucket, { expandRelPath: _relPath })
            else openNewKb({ bucket })
          }}
          title={`New ${bucket} entry`}
          aria-label={`New ${bucket} entry`}
          className="opacity-0 group-hover/row:opacity-100 focus:opacity-100 mr-1 p-0.5 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
        >
          <Plus className="h-3 w-3" />
        </button>
      )}
    </div>
  )
}

function KbDragGhost({ entryRef }: { entryRef: string }) {
  return (
    <DragGhost label={entryRef} labelClassName="font-mono">
      <FolderOpen className="h-4 w-4 shrink-0 text-blue-600 dark:text-blue-400" aria-hidden />
    </DragGhost>
  )
}
