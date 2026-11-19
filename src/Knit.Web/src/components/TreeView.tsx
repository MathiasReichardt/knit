import { createContext, useContext, useEffect, useMemo, useRef, useState, useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowDownUp, ArrowRight, ArrowUp, CalendarClock, ChevronDown, ChevronRight, ChevronsUpDown, ChevronsDownUp, Filter, GripVertical, Plus, TriangleAlert } from 'lucide-react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  KeyboardSensor,
  MeasuringStrategy,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { dropOnlyWhereHovered } from '../lib/dndCollision'
import { DragGhost } from './DragGhost'
import { api, qk } from '../api/client'
import type { ItemSummary, TreeNode } from '../api/types'
import { STATE_COLOR, STATE_ICON, TYPE_COLOR, TYPE_ICON } from '../lib/glyphs'
import { PRIORITY_ICON, PRIORITY_LABEL, priorityOf, priorityRowTint } from '../lib/priority'
import { dueShown, dueStatus, dueStatusColor, formatDueDate, todayIso } from '../lib/dueStatus'
import { useShallow } from 'zustand/react/shallow'
import { buildItemQuery, hasAnyActiveFilter, useNavigator, useTreeSort } from '../stores/navigator'
import { useTreeOpen } from '../stores/treeOpen'
import { useItemReveal } from '../stores/itemReveal'
import { flashIn } from '../lib/headingFlash'
import { useViewPrefs } from '../stores/viewPrefs'
import { useSelection } from '../stores/selection'
import { useDialogs } from '../stores/dialogs'
import { useToasts } from '../stores/toasts'
import { CopyIdIconButton } from './CopyButton'
import { CopyIdTextButton } from './CopyIdTextButton'
import { TagBadges } from './TagBadges'
import { RowLink } from './RowLink'
import { ApplySortDialog } from './ApplySortDialog'
import { PlacingBadge } from './PlacingBadge'
import { applyDndPlacement } from '../lib/applyDnd'
import {
  buildDndPlacement,
  checkItemDrop,
  countWithDescendants,
  type DragPayload,
  type DropKind,
} from '../lib/dndPayload'
import { DROP_OK } from '../lib/dropVerdict'
import { DropCheckContext, dropAnnouncements, useDropCheck, type DropCheck } from '../hooks/useDropVerdict'
import { useArrowNav } from '../hooks/useArrowNav'
import { useAutoFocusOnData } from '../hooks/useAutoFocusOnData'
import type { NavRow } from '../lib/arrowNav'
import { BlockMarkIcon } from './BlockMarkIcon'

// Trailing badge columns are shared by every visible row so ids and tags line
// up. A column only takes space when some visible row fills it. The mono id
// is sized in `ch`; the proportional due date is measured in the row's font.
interface Columns {
  priority: boolean
  duePx: number
  dueIcon: boolean
  idCh: number
}

const NO_COLUMNS: Columns = { priority: false, duePx: 0, dueIcon: false, idCh: 0 }
const DUE_FONT_PX = 11

let measureCtx: CanvasRenderingContext2D | null | undefined
function textWidthPx(text: string): number {
  if (measureCtx === undefined) measureCtx = document.createElement('canvas').getContext('2d')
  if (!measureCtx) return text.length * DUE_FONT_PX * 0.6
  measureCtx.font = `${DUE_FONT_PX}px ${getComputedStyle(document.body).fontFamily}`
  return Math.ceil(measureCtx.measureText(text).width)
}
const ColumnsContext = createContext<Columns>(NO_COLUMNS)

interface DueBadgeInfo {
  label: string
  status: ReturnType<typeof dueStatus>
  emphasized: boolean
}

function dueBadgeInfo(
  item: ItemSummary,
  today: string,
  dueSoonDays: number,
  display: 'all' | 'overdue' | 'none',
): DueBadgeInfo | null {
  if (!item.due) return null
  const closed = item.state === 'done' || item.state === 'dismissed'
  const status = dueStatus(item.due, today, dueSoonDays, closed)
  if (!dueShown(status, display)) return null
  return { label: formatDueDate(item.due), status, emphasized: status === 'overdue' || status === 'soon' }
}

function useDueBadgeInfo(): (item: ItemSummary) => DueBadgeInfo | null {
  const health = useQuery({ queryKey: qk.health, queryFn: api.health })
  const display = useViewPrefs((s) => s.due)
  const soonDays = health.data?.dueSoonDays ?? 3
  return useCallback((item) => dueBadgeInfo(item, todayIso(), soonDays, display), [soonDays, display])
}

function useColumns(items: (ItemSummary | undefined)[]): Columns {
  const dueOf = useDueBadgeInfo()
  return useMemo(() => {
    const c = { ...NO_COLUMNS }
    for (const item of items) {
      if (!item) continue
      if (priorityOf(item.priority) !== 'normal') c.priority = true
      const due = dueOf(item)
      if (due) {
        c.duePx = Math.max(c.duePx, textWidthPx(due.label))
        if (due.emphasized) c.dueIcon = true
      }
      c.idCh = Math.max(c.idCh, item.shortId.length)
    }
    return c
  }, [items, dueOf])
}

interface Props {
  selectedId: string | null
  onSelect: (id: string) => void
}

// Spring-loaded expand: how long a drag has to rest on a collapsed row before
// it opens. Long enough that crossing a stack of rows on the way somewhere
// else leaves them all shut.
const HOVER_EXPAND_MS = 700

// Newly spring-loaded children mount mid-drag, so their drop bands have to be
// measured then and there — the default only measures the set present at drag
// start, and the new rows would be undroppable until the next drag.
const MEASURING = { droppable: { strategy: MeasuringStrategy.Always } }

function collectExpandableIds(nodes: TreeNode[], acc: Set<string>) {
  for (const n of nodes) {
    if (n.children.length > 0) {
      acc.add(n.item.id)
      collectExpandableIds(n.children, acc)
    }
  }
}

function flattenIds(nodes: TreeNode[], expanded: ReadonlySet<string>, acc: string[]) {
  for (const n of nodes) {
    acc.push(n.item.id)
    if (n.children.length > 0 && expanded.has(n.item.id)) {
      flattenIds(n.children, expanded, acc)
    }
  }
}

function collectAllItems(nodes: TreeNode[], acc: Map<string, ItemSummary>) {
  for (const n of nodes) {
    acc.set(n.item.id, n.item)
    collectAllItems(n.children, acc)
  }
}

export function TreeView({ selectedId, onSelect }: Props) {
  const navQuery = useNavigator(useShallow(buildItemQuery))
  const treeSort = useTreeSort()
  const treeQ = useQuery({ queryKey: qk.treeFiltered(navQuery), queryFn: () => api.tree(navQuery) })
  const unparentedQuery = { ...navQuery, unparented: true }
  const unparentedQ = useQuery({
    queryKey: qk.itemsFiltered(unparentedQuery),
    queryFn: () => api.items(unparentedQuery),
    enabled: !navQuery.unparented,
  })

  const expandableIds = useMemo(() => {
    const s = new Set<string>()
    if (treeQ.data) collectExpandableIds(treeQ.data, s)
    return s
  }, [treeQ.data])

  // Expand state is persisted per repo. Tracking `expanded` rather
  // than the old `collapsed` set is what lets one store serve every tree: an
  // empty store means level 0 only, the same thing *collapse all* renders.
  const { ready, expanded, toggle, replace, expand, prune } = useTreeOpen('items')

  const collapseAll = useCallback(() => replace([]), [replace])
  const expandAll = useCallback(() => replace([...expandableIds]), [replace, expandableIds])

  const unparented = useMemo(() => {
    if (navQuery.unparented) return []
    if (!treeQ.data || !unparentedQ.data) return []
    const inTree = new Set<string>()
    const walk = (nodes: TreeNode[]) => {
      for (const n of nodes) {
        inTree.add(n.item.id)
        walk(n.children)
      }
    }
    walk(treeQ.data)
    return unparentedQ.data.filter((i) => !inTree.has(i.id))
  }, [unparentedQ.data, treeQ.data, navQuery.unparented])

  const setOrder = useSelection((s) => s.setOrder)
  const visibleOrder = useMemo(() => {
    const ids: string[] = []
    if (treeQ.data) flattenIds(treeQ.data, expanded, ids)
    for (const i of unparented) ids.push(i.id)
    return ids
  }, [treeQ.data, unparented, expanded])
  useEffect(() => setOrder(visibleOrder), [visibleOrder, setOrder])

  // Flat lookup of every item the tree knows about — drives DnD validation.
  const itemsById = useMemo(() => {
    const map = new Map<string, ItemSummary>()
    if (treeQ.data) collectAllItems(treeQ.data, map)
    for (const i of unparented) map.set(i.id, i)
    return map
  }, [treeQ.data, unparented])

  // Drop expand state for ids that no longer exist — but only against a
  // complete node set. The tree query is usually narrowed (filters, search,
  // slate) and the lens hides whole item types, so pruning against a filtered
  // result would discard state for merely-hidden items: unknown is not deleted.
  const treeIsComplete = useNavigator((s) => !hasAnyActiveFilter(s) && s.lens === 'all')
  // Reuses the `itemsById` walk above rather than re-flattening the tree, so
  // an SSE-driven refetch doesn't pay for a second pass.
  useEffect(() => {
    if (!ready || !treeIsComplete || !treeQ.data) return
    prune(new Set(itemsById.keys()))
  }, [ready, treeIsComplete, treeQ.data, itemsById, prune])

  const nav = useMemo(() => {
    const parentOf = new Map<string, string>()
    const firstChildOf = new Map<string, string>()
    const hasChildren = new Set<string>()
    const walk = (nodes: TreeNode[], parentId: string | null) => {
      for (const n of nodes) {
        if (parentId !== null) parentOf.set(n.item.id, parentId)
        if (n.children.length > 0) {
          hasChildren.add(n.item.id)
          firstChildOf.set(n.item.id, n.children[0]!.item.id)
          walk(n.children, n.item.id)
        }
      }
    }
    if (treeQ.data) walk(treeQ.data, null)
    return { parentOf, firstChildOf, hasChildren }
  }, [treeQ.data])

  // Reveal the selected row when the selection changes from outside the tree
  // (jump-to-ref, search, markdown link): expand its collapsed ancestors and
  // scroll it into view. `block: 'nearest'` is a no-op when already visible,
  // so an in-tree click doesn't yank the viewport.
  const scrollRef = useRef<HTMLDivElement>(null)
  const pendingReveal = useRef<string | null>(null)
  const armedFor = useRef<string | null>(null)
  // An explicit jump also opens the target itself and flashes its row.
  const jumpTo = useRef<string | null>(null)

  const scrollToReveal = useCallback(() => {
    const id = pendingReveal.current
    const root = scrollRef.current
    if (!id || !root) return
    const selector = `[data-reveal-id="${id}"]`
    const el = root.querySelector<HTMLElement>(selector)
    if (el) {
      el.scrollIntoView({ block: 'nearest' })
      pendingReveal.current = null
      if (jumpTo.current === id) {
        jumpTo.current = null
        flashIn(root, selector)
      }
    }
  }, [])

  // Arm a reveal only when the selection itself changes. The tree data is
  // rebuilt on every write, so arming off it would yank the viewport back to
  // the selected row after an unrelated edit — a drop far down a scrolled tree
  // would jump back up to wherever the selection happens to sit.
  useEffect(() => {
    if (!selectedId || armedFor.current === selectedId) return
    armedFor.current = selectedId
    pendingReveal.current = selectedId
  }, [selectedId])

  // A jump arms a reveal even when the target is already selected — the
  // selection didn't change, but its row may sit under a collapsed parent.
  const revealTarget = useItemReveal((s) => s.target)
  // Requested before the navigation lands, so it may precede the selection;
  // arming `armedFor` keeps the selection effect from re-arming it after.
  useEffect(() => {
    if (!revealTarget) return
    const t = useItemReveal.getState().take()
    if (!t) return
    armedFor.current = t.id
    pendingReveal.current = t.id
    jumpTo.current = t.id
  }, [revealTarget])

  // Resolve an armed reveal: open the collapsed ancestors (and a jump target
  // itself), then scroll. Reruns on data / expand changes because the row may
  // not exist yet (URL-driven selection lands before the tree query resolves).
  // `expand` bails when every id is already open, so no cascade.
  useEffect(() => {
    const id = pendingReveal.current
    if (!id) return
    const open: string[] = []
    if (jumpTo.current === id && nav.hasChildren.has(id)) open.push(id)
    let p = nav.parentOf.get(id)
    while (p) {
      open.push(p)
      p = nav.parentOf.get(p)
    }
    if (open.length > 0) expand(open)
    scrollToReveal()
  }, [selectedId, revealTarget, nav, expanded, scrollToReveal, expand])

  useAutoFocusOnData(scrollRef, treeQ.data)

  // Flatten the visible rows for the shared arrow-nav core. The order already
  // matches `visibleOrder`, including the appended unparented section.
  const columns = useColumns(useMemo(() => visibleOrder.map((id) => itemsById.get(id)), [visibleOrder, itemsById]))

  const navRows = useMemo<NavRow<ItemSummary>[]>(
    () =>
      visibleOrder.map((id) => {
        const hasKids = nav.hasChildren.has(id)
        return {
          key: id,
          parentKey: nav.parentOf.get(id) ?? null,
          expandable: hasKids,
          expanded: hasKids && expanded.has(id),
          data: itemsById.get(id)!,
        }
      }),
    [visibleOrder, nav, expanded, itemsById],
  )
  const { onKeyDown } = useArrowNav<ItemSummary>({
    rows: navRows,
    selectedKey: selectedId,
    onActivate: (row) => {
      useSelection.getState().click(row.key, { shift: false, meta: false })
      onSelect(row.key)
    },
    onToggle: toggle,
  })

  // DnD wiring -------------------------------------------------------------
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const selected = useSelection((s) => s.selected)
  const [drag, setDrag] = useState<DragPayload | null>(null)
  // The server places a drop now, so the row only settles on the response.
  // Ids stay dimmed and a spinner shows until then — otherwise the wait
  // looks like a drag that was dropped on the floor (30).
  const [placing, setPlacing] = useState<string[] | null>(null)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )

  const onDragStart = useCallback(
    (e: DragStartEvent) => {
      const id = String(e.active.id)
      // If the dragged item is part of the multi-selection, drag the whole
      // selection in visible order. Otherwise drag just this row.
      let ids: string[]
      if (selected.has(id) && selected.size > 1) {
        ids = visibleOrder.filter((x) => selected.has(x))
      } else {
        ids = [id]
      }
      setDrag({
        source: 'tree',
        ids,
        primaryId: id,
        totalCount: countWithDescendants(ids, itemsById),
      })
    },
    [selected, visibleOrder, itemsById],
  )

  // Spring-loaded expand. Armed per hovered row and re-armed from scratch when
  // the drag moves to another one, so only a row the pointer actually rests on
  // opens. Only the `inside` band arms it: that is the "drop under this row"
  // gesture, and it is the only one a closed subtree can be hiding a target
  // from.
  const hoverExpand = useRef<{ id: string; timer: number } | null>(null)
  const cancelHoverExpand = useCallback(() => {
    if (!hoverExpand.current) return
    window.clearTimeout(hoverExpand.current.timer)
    hoverExpand.current = null
  }, [])
  useEffect(() => cancelHoverExpand, [cancelHoverExpand])

  const onDragOver = useCallback(
    (e: DragOverEvent) => {
      const target = e.over?.data.current as DropKind | undefined
      const id = target?.kind === 'tree-node' ? target.nodeId : null
      if (hoverExpand.current?.id === id) return
      cancelHoverExpand()
      if (!id || !nav.hasChildren.has(id) || expanded.has(id)) return
      if (drag?.ids.includes(id)) return
      const timer = window.setTimeout(() => {
        hoverExpand.current = null
        expand([id])
      }, HOVER_EXPAND_MS)
      hoverExpand.current = { id, timer }
    },
    [cancelHoverExpand, nav.hasChildren, expanded, drag, expand],
  )

  const dropCheck = useCallback<DropCheck>(
    (target) => (drag ? checkItemDrop(drag, target as DropKind, { itemsById, treeSort }) : DROP_OK),
    [drag, itemsById, treeSort],
  )
  const accessibility = useMemo(() => ({ announcements: dropAnnouncements(dropCheck) }), [dropCheck])

  const onDragEnd = useCallback(
    async (e: DragEndEvent) => {
      const payload = drag
      setDrag(null)
      cancelHoverExpand()
      if (!payload || !e.over) return
      const target = e.over.data.current as DropKind | undefined
      if (!target) return
      const altHeld =
        e.activatorEvent instanceof MouseEvent
          ? (e.activatorEvent as MouseEvent).altKey
          : false
      const built = buildDndPlacement(payload, target, { itemsById, treeSort }, {
        keepRelativeOrder: altHeld,
      })
      // A refused drop was already explained on the ghost while hovering.
      if ('error' in built) return
      if (!built.placement) return
      setPlacing(built.placement.ids)
      try {
        const res = await applyDndPlacement(qc, built.placement)
        if (!res.ok) toast('error', res.message ?? 'Move failed')
      } finally {
        setPlacing(null)
        // The dragged row re-mounts under its new parent, taking the focused
        // grip button with it — without this, focus falls to <body> and arrow
        // navigation goes dead after every drop.
        if (!document.activeElement || document.activeElement === document.body) {
          scrollRef.current?.focus({ preventScroll: true })
        }
      }
    },
    [drag, itemsById, treeSort, qc, toast, cancelHoverExpand],
  )

  if (treeQ.isLoading || !ready) {
    return <div className="p-3 text-sm text-zinc-500">Loading tree…</div>
  }
  if (treeQ.error) {
    return <div className="p-3 text-sm text-red-600">Failed to load tree</div>
  }

  const tree = treeQ.data ?? []
  if (tree.length === 0 && unparented.length === 0) {
    return (
      <div className="p-4 text-sm text-zinc-500 dark:text-zinc-400">
        <p className="font-semibold mb-1">No items match</p>
        <p>Clear filters or create one from the CLI:</p>
        <pre className="font-mono text-xs bg-zinc-100 dark:bg-zinc-900 rounded px-2 py-1.5 mt-1">
          knit add task &quot;My first task&quot;
        </pre>
      </div>
    )
  }

  const hasExpandable = expandableIds.size > 0

  return (
    <div className="relative flex flex-col h-full">
      {hasExpandable && (
        <div className="flex items-center gap-1 px-2 py-1.5 border-b border-zinc-200 dark:border-zinc-800">
          <button
            type="button"
            onClick={expandAll}
            aria-label="Expand all"
            title="Expand all"
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300"
          >
            <ChevronsUpDown className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={collapseAll}
            aria-label="Collapse all"
            title="Collapse all"
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300"
          >
            <ChevronsDownUp className="h-4 w-4" />
          </button>
        </div>
      )}
      <DropCheckContext.Provider value={dropCheck}>
        <DndContext
          sensors={sensors}
          collisionDetection={dropOnlyWhereHovered}
          accessibility={accessibility}
          measuring={MEASURING}
          onDragStart={onDragStart}
          onDragOver={onDragOver}
          onDragEnd={onDragEnd}
          onDragCancel={() => {
            setDrag(null)
            cancelHoverExpand()
          }}
        >
          <ColumnsContext.Provider value={columns}>
            <div
              ref={scrollRef}
              className="overflow-y-auto flex-1 focus:outline-none"
              tabIndex={0}
              onKeyDown={onKeyDown}
            >
              <ul className="py-1 text-sm">
                {tree.map((n, i) => {
                  const prevId = i > 0 ? tree[i - 1]!.item.id : null
                  const nextId = i < tree.length - 1 ? tree[i + 1]!.item.id : null
                  return (
                    <Node
                      key={n.item.id}
                      node={n}
                      depth={0}
                      parentId={null}
                      prevSiblingId={prevId}
                      nextSiblingId={nextId}
                      selectedId={selectedId}
                      onSelect={onSelect}
                      expanded={expanded}
                      onToggle={toggle}
                      draggedIds={drag?.ids ?? placing}
                    />
                  )
                })}
              </ul>
              {unparented.length > 0 && (
                <div className="mt-2 border-t border-zinc-200 dark:border-zinc-800">
                  <div className="px-3 py-1.5 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
                    Unparented
                  </div>
                  <ul className="text-sm">
                    {unparented.map((i, idx) => {
                      const prevId = idx > 0 ? unparented[idx - 1]!.id : null
                      const nextId = idx < unparented.length - 1 ? unparented[idx + 1]!.id : null
                      return (
                        <li key={i.id}>
                          <Row
                            item={i}
                            depth={0}
                            parentId={null}
                            prevSiblingId={prevId}
                            nextSiblingId={nextId}
                            selectedId={selectedId}
                            onSelect={onSelect}
                            hasChildren={false}
                            isCollapsed={false}
                            onToggle={undefined}
                            match
                            draggedIds={drag?.ids ?? placing}
                          />
                        </li>
                      )
                    })}
                  </ul>
                </div>
              )}
            </div>
          </ColumnsContext.Provider>
          {placing && <PlacingBadge />}
          <DragOverlay>
            {drag ? <ItemDragGhost count={drag.totalCount} item={itemsById.get(drag.primaryId) ?? null} /> : null}
          </DragOverlay>
        </DndContext>
      </DropCheckContext.Provider>
    </div>
  )
}

function ItemDragGhost({ count, item }: { count: number; item: ItemSummary | null }) {
  const Icon = item ? TYPE_ICON[item.type] : null
  const color = item ? TYPE_COLOR[item.type] : 'text-zinc-500'
  return (
    <DragGhost count={count} label={item?.title ?? 'item'}>
      {Icon ? <Icon className={`${color} h-4 w-4 shrink-0`} aria-hidden /> : null}
    </DragGhost>
  )
}

function Node({
  node,
  depth,
  parentId,
  prevSiblingId,
  nextSiblingId,
  selectedId,
  onSelect,
  expanded,
  onToggle,
  draggedIds,
}: {
  node: TreeNode
  depth: number
  parentId: string | null
  prevSiblingId: string | null
  nextSiblingId: string | null
  selectedId: string | null
  onSelect: (id: string) => void
  expanded: ReadonlySet<string>
  onToggle: (id: string) => void
  draggedIds: string[] | null
}) {
  const hasChildren = node.children.length > 0
  const isCollapsed = !expanded.has(node.item.id)
  return (
    <li>
      <Row
        item={node.item}
        depth={depth}
        parentId={parentId}
        prevSiblingId={prevSiblingId}
        nextSiblingId={nextSiblingId}
        selectedId={selectedId}
        onSelect={onSelect}
        hasChildren={hasChildren}
        isCollapsed={isCollapsed}
        onToggle={hasChildren ? () => onToggle(node.item.id) : undefined}
        match={node.match}
        draggedIds={draggedIds}
      />
      {hasChildren && !isCollapsed && (
        <ul>
          {node.children.map((c, i) => {
            const prev = i > 0 ? node.children[i - 1]!.item.id : null
            const next = i < node.children.length - 1 ? node.children[i + 1]!.item.id : null
            return (
              <Node
                key={c.item.id}
                node={c}
                depth={depth + 1}
                parentId={node.item.id}
                prevSiblingId={prev}
                nextSiblingId={next}
                selectedId={selectedId}
                onSelect={onSelect}
                expanded={expanded}
                onToggle={onToggle}
                draggedIds={draggedIds}
              />
            )
          })}
        </ul>
      )}
    </li>
  )
}

// One of a row's three stacked drop zones. `before`/`after` reorder the item
// as a sibling (drop-line + up/down arrow); `inside` re-parents it as a child
// (ring + right arrow). Bands are `pointer-events-none` — dnd-kit resolves the
// target from droppable rects (nearest-center → equal thirds), so normal row
// clicks pass straight through. Icons hint the outcome before releasing.
function DropBand({
  pos,
  id,
  data,
  active,
  indent,
}: {
  pos: 'before' | 'inside' | 'after'
  id: string
  data: DropKind
  active: boolean
  indent: number
}) {
  const { isOver, setNodeRef } = useDroppable({ id, data })
  const check = useDropCheck()
  // Only a drop that lands lights up — a refused or no-op zone stays plain, so
  // dragging across a tree doesn't flicker; the ghost says "no" and why.
  const show = active && isOver && (check?.(data) ?? DROP_OK).kind === 'ok'
  const band = pos === 'before' ? 'top-0 h-1/3' : pos === 'after' ? 'bottom-0 h-1/3' : 'top-1/3 h-1/3'
  const Icon = pos === 'before' ? ArrowUp : pos === 'after' ? ArrowDown : ArrowRight
  return (
    <div ref={setNodeRef} aria-hidden className={`pointer-events-none absolute inset-x-0 ${band}`}>
      {show && (
        <>
          {pos === 'inside' ? (
            // middle band is exactly 1/3 of the row, so -inset-y-full grows the
            // ring to the full row height → "this row becomes the parent".
            <span className="absolute -inset-y-full inset-x-0 rounded-sm ring-1 ring-inset ring-blue-500 bg-blue-500/10" />
          ) : (
            <span className={`absolute inset-x-0 ${pos === 'before' ? 'top-0' : 'bottom-0'} h-0.5 bg-blue-500`} />
          )}
          <span
            style={{ left: indent }}
            className={
              `absolute inline-flex h-4 w-4 items-center justify-center rounded-full bg-blue-500 text-white shadow ` +
              (pos === 'before'
                ? 'top-0 -translate-y-1/2'
                : pos === 'after'
                  ? 'bottom-0 translate-y-1/2'
                  : 'top-1/2 -translate-y-1/2')
            }
          >
            <Icon className="h-3 w-3" />
          </span>
        </>
      )}
    </div>
  )
}

function Row({
  item,
  depth,
  parentId,
  prevSiblingId,
  nextSiblingId,
  selectedId,
  onSelect,
  hasChildren,
  isCollapsed,
  onToggle,
  match,
  draggedIds,
}: {
  item: ItemSummary
  depth: number
  parentId: string | null
  prevSiblingId: string | null
  nextSiblingId: string | null
  selectedId: string | null
  onSelect: (id: string) => void
  hasChildren: boolean
  isCollapsed: boolean
  onToggle: (() => void) | undefined
  match: boolean
  draggedIds: string[] | null
}) {
  const selected = item.id === selectedId
  const multiSelected = useSelection((s) => s.selected.has(item.id))
  const click = useSelection((s) => s.click)
  const setUnder = useNavigator((s) => s.setUnder)
  const byState = useTreeSort() === 'state'
  const [applySortOpen, setApplySortOpen] = useState(false)
  const scopeUnder = useCallback(
    (e: React.MouseEvent | React.SyntheticEvent) => {
      e.preventDefault()
      e.stopPropagation()
      setUnder(item.id)
    },
    [setUnder, item.id],
  )

  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({
    id: item.id,
  })

  const beingDragged = draggedIds?.includes(item.id) ?? false
  // Show drop hints on every row except the one(s) being dragged.
  const showDrops = draggedIds !== null && !beingDragged
  const showIds = useViewPrefs((s) => s.showIds)
  const showTags = useViewPrefs((s) => s.showTags)
  const indent = 8 + depth * 14

  const columns = useContext(ColumnsContext)
  const colorByPriority = useViewPrefs((s) => s.colorByPriority)
  const rowTint = colorByPriority ? priorityRowTint(item.priority) : null

  return (
    <div
      data-reveal-id={item.id}
      style={{ paddingLeft: indent }}
      onContextMenu={scopeUnder}
      className={
        'group relative flex items-center gap-1 w-full pr-2 py-1 text-left ' +
        (selected || multiSelected
          ? 'bg-blue-100 dark:bg-blue-900/40 text-zinc-900 dark:text-zinc-50'
          : (rowTint ?? 'hover:bg-zinc-100 dark:hover:bg-zinc-900')) +
        (match ? '' : ' opacity-50') +
        (beingDragged || isDragging ? ' opacity-40' : '')
      }
    >
      <DropBand
        pos="before"
        id={`tree-before:${item.id}`}
        data={{ kind: 'tree-gap', parentId, prevId: prevSiblingId, nextId: item.id }}
        active={showDrops}
        indent={indent}
      />
      <DropBand
        pos="inside"
        id={`tree-inside:${item.id}`}
        data={{ kind: 'tree-node', nodeId: item.id }}
        active={showDrops}
        indent={indent}
      />
      <DropBand
        pos="after"
        id={`tree-after:${item.id}`}
        data={{ kind: 'tree-gap', parentId, prevId: item.id, nextId: nextSiblingId }}
        active={showDrops}
        indent={indent}
      />
      <button
        ref={setDragRef}
        {...attributes}
        {...listeners}
        type="button"
        title="Drag to reorder or re-parent"
        aria-label="Drag handle"
        className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-grab active:cursor-grabbing shrink-0"
      >
        <GripVertical className="h-3.5 w-3.5" />
      </button>
      {hasChildren ? (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            onToggle?.()
          }}
          aria-label={isCollapsed ? 'Expand' : 'Collapse'}
          className="w-4 h-4 flex items-center justify-center text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-100 shrink-0"
        >
          {isCollapsed ? <ChevronRight className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
        </button>
      ) : (
        <span aria-hidden className="w-4 h-4 shrink-0" />
      )}
      <RowLink
        to="/items"
        search={{ id: item.id }}
        claimModifiers
        keepSearch
        onSelect={(e) => {
          const multi = e.shiftKey || e.ctrlKey || e.metaKey
          // Seed the multi-select store with the URL-focused row so the
          // user's perceived "first selection" is actually part of the
          // set when they start adding rows via Ctrl/Shift+click.
          if (multi && selectedId && useSelection.getState().selected.size === 0) {
            useSelection.getState().click(selectedId, { shift: false, meta: false })
          }
          click(item.id, { shift: e.shiftKey, meta: e.ctrlKey || e.metaKey })
          // Multi-select must not change the URL `id` — the items route
          // effect treats a new selectedId as a hard reset of the selection.
          if (!multi) onSelect(item.id)
        }}
        onDoubleClick={(e) => {
          e.stopPropagation()
          onToggle?.()
        }}
        className="flex items-center gap-2 flex-1 min-w-0 text-left select-none"
      >
        {(() => {
          const TypeIcon = TYPE_ICON[item.type]
          const StateIcon = STATE_ICON[item.state]
          const typeColor = match ? TYPE_COLOR[item.type] : 'text-zinc-400 dark:text-zinc-600'
          return (
            <>
              <TypeIcon
                aria-label={item.type}
                className={typeColor + ' h-4 w-4 shrink-0'}
              />
              <StateIcon
                aria-label={item.state}
                className={STATE_COLOR[item.state] + ' h-4 w-4 shrink-0'}
              />
            </>
          )
        })()}
        <span className="truncate flex-1">{item.title}</span>
        <BlockMarkIcon item={item} />
        <span className="inline-flex items-center gap-1 shrink-0 empty:hidden">
          {showTags && <TagBadges tags={item.tags} />}
          {columns.duePx > 0 && <DueBadge item={item} columns={columns} />}
          {columns.priority && <PriorityBadge item={item} />}
        </span>
      </RowLink>
      {showIds ? (
        <CopyIdTextButton
          id={item.id}
          label={item.shortId}
          className="px-0.5 text-xs text-zinc-400 dark:text-zinc-500"
          style={columns.idCh ? { width: `calc(${columns.idCh}ch + 0.25rem)` } : undefined}
        />
      ) : (
        <CopyIdIconButton
          value={item.id}
          title="Copy id"
          className="opacity-0 group-hover:opacity-100 focus:opacity-100"
        />
      )}
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation()
          useDialogs.getState().openNewItem({ parentId: item.id })
        }}
        title="Add child item"
        aria-label="Add child item"
        className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
      >
        <Plus className="h-3 w-3" />
      </button>
      <button
        type="button"
        onClick={scopeUnder}
        title="Filter under this item (or right-click row)"
        aria-label="Filter under this item"
        className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
      >
        <Filter className="h-3 w-3" />
      </button>
      {byState && hasChildren && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            setApplySortOpen(true)
          }}
          title="Apply the state order to ranks below this item"
          aria-label="Apply state order to ranks"
          className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
        >
          <ArrowDownUp className="h-3 w-3" />
        </button>
      )}
      {applySortOpen && (
        // The dialog renders inside the row: keep its clicks from selecting
        // or scoping the row.
        <div onClick={(e) => e.stopPropagation()} onContextMenu={(e) => e.stopPropagation()} className="contents">
          <ApplySortDialog item={item} onClose={() => setApplySortOpen(false)} />
        </div>
      )}
    </div>
  )
}

function PriorityBadge({ item }: { item: ItemSummary }) {
  // Normal is the default for most items — badge only the signal levels so
  // the tree doesn't carry a glyph on every row. Neutral color (priority
  // coloring is the color-by-priority toggle). Normal keeps the column's empty slot.
  const priority = priorityOf(item.priority)
  if (priority === 'normal') return <span aria-hidden className="w-3.5 h-3.5 shrink-0" />
  const Icon = PRIORITY_ICON[priority]
  return (
    <span
      title={`Priority: ${PRIORITY_LABEL[priority]}`}
      aria-label={`Priority ${PRIORITY_LABEL[priority]}`}
      className="shrink-0 text-zinc-400 dark:text-zinc-500"
    >
      <Icon className="h-3.5 w-3.5" />
    </span>
  )
}

function DueBadge({ item, columns }: { item: ItemSummary; columns: Columns }) {
  const due = useDueBadgeInfo()(item)
  // Icon (0.875rem) + gap (0.25rem) when any visible date is emphasized.
  const width = { minWidth: `calc(${columns.duePx}px${columns.dueIcon ? ' + 1.125rem' : ''})` }
  if (!due) return <span aria-hidden className="shrink-0 text-[11px]" style={width} />
  const StatusIcon = due.status === 'overdue' ? TriangleAlert : CalendarClock
  const color = due.emphasized ? dueStatusColor(due.status) : 'text-zinc-400 dark:text-zinc-500'
  return (
    <span
      title={`Due ${item.due}`}
      className={'inline-flex items-center justify-end gap-1 shrink-0 text-[11px] ' + color}
      style={width}
    >
      {due.emphasized && <StatusIcon aria-hidden className="h-3.5 w-3.5" />}
      {due.label}
    </span>
  )
}
