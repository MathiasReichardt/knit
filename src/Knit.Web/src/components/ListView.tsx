import { useEffect, useMemo, useCallback, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, CalendarClock, Filter, GripVertical, TriangleAlert } from 'lucide-react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  KeyboardSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { dropOnlyWhereHovered } from '../lib/dndCollision'
import { DragGhost } from './DragGhost'
import { api, qk, type SortKey } from '../api/client'
import type { ItemSummary } from '../api/types'
import { STATE_COLOR, STATE_ICON, TYPE_COLOR, TYPE_ICON } from '../lib/glyphs'
import { PRIORITY_ICON, PRIORITY_LABEL, hasPriorityIcon, priorityOf, priorityRowTint } from '../lib/priority'
import { dueShown, dueStatus, dueStatusColor, formatDueDate, todayIso } from '../lib/dueStatus'
import { useShallow } from 'zustand/react/shallow'
import { buildItemQuery, useNavigator } from '../stores/navigator'
import { useViewPrefs } from '../stores/viewPrefs'
import { useSelection } from '../stores/selection'
import { useToasts } from '../stores/toasts'
import { useItemReveal } from '../stores/itemReveal'
import { flashIn } from '../lib/headingFlash'
import { CopyIdIconButton } from './CopyButton'
import { CopyIdTextButton } from './CopyIdTextButton'
import { TagBadges } from './TagBadges'
import { RowLink } from './RowLink'
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

interface Props {
  selectedId: string | null
  onSelect: (id: string) => void
}

export function ListView({ selectedId, onSelect }: Props) {
  const navQuery = useNavigator(useShallow(buildItemQuery))
  const sort = useNavigator((s) => s.sort)
  const setSort = useNavigator((s) => s.setSort)
  const q = useQuery({ queryKey: qk.itemsFiltered(navQuery), queryFn: () => api.items(navQuery) })

  const setOrder = useSelection((s) => s.setOrder)
  const items = useMemo<ItemSummary[]>(() => q.data ?? [], [q.data])
  useEffect(() => setOrder(items.map((i) => i.id)), [items, setOrder])

  const scrollRef = useRef<HTMLDivElement>(null)
  useAutoFocusOnData(scrollRef, q.data)

  // A jump (jump-to-ref, search, body ref) scrolls to and flashes its row.
  // Taken as soon as it arrives; kept pending until the row is rendered, since
  // the list query may still be loading after the route change.
  const pendingJump = useRef<string | null>(null)
  const revealTarget = useItemReveal((s) => s.target)
  useEffect(() => {
    const t = revealTarget ? useItemReveal.getState().take() : null
    if (t) pendingJump.current = t.id
    const id = pendingJump.current
    const root = scrollRef.current
    if (!id || !root) return
    const selector = `[data-reveal-id="${id}"]`
    const el = root.querySelector<HTMLElement>(selector)
    if (!el) return
    pendingJump.current = null
    el.scrollIntoView({ block: 'nearest' })
    flashIn(root, selector)
  }, [revealTarget, items])

  // Flat rows for the shared arrow-nav core. The list has no collapse, so rows
  // are never `expandable`; ←/→ degenerate to parent / first-child hops among
  // whatever rows are visible (a child present elsewhere in the sorted list).
  const idSet = useMemo(() => new Set(items.map((i) => i.id)), [items])
  const navRows = useMemo<NavRow<ItemSummary>[]>(
    () =>
      items.map((i) => ({
        key: i.id,
        parentKey: i.parent && idSet.has(i.parent) ? i.parent : null,
        expandable: false,
        expanded: false,
        data: i,
      })),
    [items, idSet],
  )
  const { onKeyDown } = useArrowNav<ItemSummary>({
    rows: navRows,
    selectedKey: selectedId,
    onActivate: (row) => {
      useSelection.getState().click(row.key, { shift: false, meta: false })
      onSelect(row.key)
    },
    onToggle: () => {},
  })

  // DnD only makes sense when the list is sorted by rank ASC — gap drops
  // wouldn't be meaningful in any other order. Mixed parents are fine:
  // each gap inherits the parent of the row below it, so dropping into a
  // gap means "insert before that row in its parent".
  const showIds = useViewPrefs((s) => s.showIds)
  const columnCount = gapRowSpan(showIds)
  const dndEnabled = useMemo(() => {
    if (sort.key !== 'rank' || sort.dir !== 'asc') return false
    return items.length >= 2
  }, [items, sort])

  const itemsById = useMemo(() => {
    const map = new Map<string, ItemSummary>()
    for (const i of items) map.set(i.id, i)
    return map
  }, [items])

  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const selected = useSelection((s) => s.selected)
  const [drag, setDrag] = useState<DragPayload | null>(null)
  // The server places a drop now, so the rows settle only on the response —
  // keep them dimmed and show a spinner until then (30).
  const [placing, setPlacing] = useState<string[] | null>(null)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )

  const onDragStart = useCallback(
    (e: DragStartEvent) => {
      const id = String(e.active.id)
      let ids: string[]
      if (selected.has(id) && selected.size > 1) {
        ids = items.filter((i) => selected.has(i.id)).map((i) => i.id)
      } else {
        ids = [id]
      }
      setDrag({
        source: 'list',
        ids,
        primaryId: id,
        totalCount: countWithDescendants(ids, itemsById),
      })
    },
    [selected, items, itemsById],
  )

  const dropCheck = useCallback<DropCheck>(
    (target) => (drag ? checkItemDrop(drag, target as DropKind, { itemsById }) : DROP_OK),
    [drag, itemsById],
  )
  const accessibility = useMemo(() => ({ announcements: dropAnnouncements(dropCheck) }), [dropCheck])

  const onDragEnd = useCallback(
    async (e: DragEndEvent) => {
      const payload = drag
      setDrag(null)
      if (!payload || !e.over) return
      const target = e.over.data.current as DropKind | undefined
      if (!target) return
      const built = buildDndPlacement(payload, target, { itemsById })
      // A refused drop was already explained on the ghost while hovering.
      if ('error' in built) return
      if (!built.placement) return
      setPlacing(built.placement.ids)
      try {
        const res = await applyDndPlacement(qc, built.placement)
        if (!res.ok) toast('error', res.message ?? 'Move failed')
      } finally {
        setPlacing(null)
      }
    },
    [drag, itemsById, qc, toast],
  )

  if (q.isLoading) return <div className="p-3 text-sm text-zinc-500">Loading…</div>
  if (q.error) return <div className="p-3 text-sm text-red-600">Failed to load items</div>

  if (items.length === 0) {
    return (
      <div className="p-4 text-sm text-zinc-500 dark:text-zinc-400">
        <p className="font-semibold mb-1">No items match</p>
        <p>Adjust filters or create one from the CLI.</p>
      </div>
    )
  }

  const click = useSelection.getState().click

  return (
    <DropCheckContext.Provider value={dropCheck}>
      <DndContext
        sensors={sensors}
        collisionDetection={dropOnlyWhereHovered}
        accessibility={accessibility}
        onDragStart={onDragStart}
        onDragEnd={onDragEnd}
        onDragCancel={() => setDrag(null)}
      >
        <div className="relative h-full">
          <div
            ref={scrollRef}
            className="overflow-auto h-full focus:outline-none"
            tabIndex={0}
            onKeyDown={onKeyDown}
          >
            {!dndEnabled && items.length >= 2 && (
              <div className="px-3 py-1 text-[11px] text-zinc-500 dark:text-zinc-400 italic border-b border-zinc-200 dark:border-zinc-800 flex items-center gap-2">
                <span>Drag-reorder needs Rank↑ sort.</span>
                <button
                  type="button"
                  onClick={() => setSort({ key: 'rank', dir: 'asc' })}
                  className="not-italic px-1.5 py-0.5 rounded border border-zinc-300 dark:border-zinc-700 hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-700 dark:text-zinc-200"
                >
                  Sort by Rank ↑
                </button>
              </div>
            )}
            <table className="w-full text-sm">
              <thead className="sticky top-0 bg-white dark:bg-zinc-950 border-b border-zinc-200 dark:border-zinc-800 text-left text-[11px] uppercase tracking-wide text-zinc-500">
                <tr>
                  {dndEnabled && <th scope="col" className="w-5" aria-label="drag" />}
                  {[
                    { key: 'type' as SortKey, label: 'Type' },
                    { key: 'state' as SortKey, label: 'State' },
                    { key: 'priority' as SortKey, label: 'Priority' },
                    ...(showIds ? [{ key: 'tree' as SortKey, label: 'Id' }] : []),
                    { key: 'tree' as SortKey, label: 'Title' },
                    { key: 'assignee' as SortKey, label: 'Assignee' },
                    { key: 'rank' as SortKey, label: 'Rank' },
                    { key: 'due' as SortKey, label: 'Due' },
                  ].map((col, idx) => {
                    const active = sort.key === col.key
                    return (
                      <th
                        key={idx}
                        scope="col"
                        className="px-2 py-1.5 font-medium cursor-pointer select-none hover:bg-zinc-50 dark:hover:bg-zinc-900"
                        onClick={() => {
                          if (active) setSort({ key: col.key, dir: sort.dir === 'asc' ? 'desc' : 'asc' })
                          else setSort({ key: col.key, dir: 'asc' })
                        }}
                      >
                        <span className="inline-flex items-center gap-1">
                          {col.label}
                          {active && (sort.dir === 'asc'
                            ? <ArrowUp className="h-3 w-3" />
                            : <ArrowDown className="h-3 w-3" />)}
                        </span>
                      </th>
                    )
                  })}
                  <th scope="col" className="px-1 py-1.5 w-14" aria-label="actions" />
                </tr>
              </thead>
              <tbody>
                {items.map((i, idx) => {
                  const prevId = idx > 0 ? items[idx - 1]!.id : null
                  return (
                    <ListRow
                      key={i.id}
                      item={i}
                      selected={i.id === selectedId}
                      prevSiblingId={prevId}
                      dndEnabled={dndEnabled}
                      draggedIds={drag?.ids ?? placing}
                      onClick={(e) => {
                        const multi = e.shiftKey || e.ctrlKey || e.metaKey
                        if (multi && selectedId && useSelection.getState().selected.size === 0) {
                          useSelection.getState().click(selectedId, { shift: false, meta: false })
                        }
                        click(i.id, { shift: e.shiftKey, meta: e.ctrlKey || e.metaKey })
                        if (!multi) onSelect(i.id)
                      }}
                    />
                  )
                })}
                {dndEnabled && items.length > 0 && (
                  <tr>
                    <td colSpan={columnCount} className="p-0">
                      <ListGapDrop
                        parentId={items[items.length - 1]!.parent ?? null}
                        prevId={items[items.length - 1]!.id}
                        nextId={null}
                      />
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        {placing && <PlacingBadge />}
        </div>
        <DragOverlay>
          {drag ? <ItemDragGhost count={drag.totalCount} item={itemsById.get(drag.primaryId) ?? null} /> : null}
        </DragOverlay>
      </DndContext>
    </DropCheckContext.Provider>
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

function ListGapDrop({
  parentId,
  prevId,
  nextId,
}: {
  parentId: string | null
  prevId: string | null
  nextId: string | null
}) {
  const id = `list-gap:${parentId ?? '__root'}:${prevId ?? ''}:${nextId ?? ''}`
  const data: DropKind = { kind: 'list-gap', parentId, prevId, nextId }
  const { isOver, setNodeRef } = useDroppable({ id, data })
  const check = useDropCheck()
  const lands = isOver && (check?.(data) ?? DROP_OK).kind === 'ok'
  // Taller hit zone than the old 6px sliver so first/last positions are easy
  // to hit; the drop shows a centered insertion line rather than a fat fill.
  return (
    <div ref={setNodeRef} aria-hidden className="relative h-2.5">
      {lands && <span className="absolute inset-x-0 top-1/2 -translate-y-1/2 h-0.5 bg-blue-500" />}
    </div>
  )
}

// Gap rows only render with drag on: drag handle + 7 data columns + optional
// id + actions.
function gapRowSpan(showIds: boolean): number {
  return showIds ? 10 : 9
}

function ListRow({
  item,
  selected,
  prevSiblingId,
  dndEnabled,
  draggedIds,
  onClick,
}: {
  item: ItemSummary
  selected: boolean
  prevSiblingId: string | null
  dndEnabled: boolean
  draggedIds: string[] | null
  onClick: (e: React.MouseEvent) => void
}) {
  const multiSelected = useSelection((s) => s.selected.has(item.id))
  const TypeIcon = TYPE_ICON[item.type]
  const StateIcon = STATE_ICON[item.state]
  const priority = priorityOf(item.priority)
  const PriorityIcon = PRIORITY_ICON[priority]
  const colorByPriority = useViewPrefs((s) => s.colorByPriority)
  const rowTint = colorByPriority ? priorityRowTint(item.priority) : null
  const showIds = useViewPrefs((s) => s.showIds)
  const showTags = useViewPrefs((s) => s.showTags)
  const columnCount = gapRowSpan(showIds)
  const setUnder = useNavigator((s) => s.setUnder)
  const scopeUnder = useCallback(
    (e: React.MouseEvent | React.SyntheticEvent) => {
      e.preventDefault()
      e.stopPropagation()
      setUnder(item.id)
    },
    [setUnder, item.id],
  )
  const beingDragged = draggedIds?.includes(item.id) ?? false
  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({
    id: item.id,
    disabled: !dndEnabled,
  })
  return (
    <>
      {dndEnabled && (
        <tr aria-hidden>
          <td colSpan={columnCount} className="p-0">
            <ListGapDrop
              parentId={item.parent ?? null}
              prevId={prevSiblingId}
              nextId={item.id}
            />
          </td>
        </tr>
      )}
      <tr
        data-reveal-id={item.id}
        onClick={onClick}
        onContextMenu={scopeUnder}
        className={
          'group cursor-pointer ' +
          (selected || multiSelected
            ? 'bg-blue-100 dark:bg-blue-900/40'
            : (rowTint ?? 'hover:bg-zinc-50 dark:hover:bg-zinc-900/60')) +
          (beingDragged || isDragging ? ' opacity-40' : '')
        }
      >
        {dndEnabled && (
          <td className="px-1 py-1.5 w-5 align-middle">
            <button
              ref={setDragRef}
              {...attributes}
              {...listeners}
              type="button"
              title="Drag to reorder"
              aria-label="Drag handle"
              onClick={(e) => e.stopPropagation()}
              className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-grab active:cursor-grabbing"
            >
              <GripVertical className="h-3.5 w-3.5" />
            </button>
          </td>
        )}
        <td className="px-2 py-1.5">
          <span className="inline-flex items-center gap-1.5">
            <TypeIcon aria-label={item.type} className={`${TYPE_COLOR[item.type]} h-4 w-4`} />
            <span className="capitalize text-xs">{item.type}</span>
          </span>
        </td>
        <td className="px-2 py-1.5">
          <span className="inline-flex items-center gap-1.5">
            <StateIcon
              aria-label={item.state}
              className={STATE_COLOR[item.state] + ' h-4 w-4'}
            />
            <span className="text-xs">{item.state}</span>
          </span>
        </td>
        <td className="px-2 py-1.5">
          <span className="inline-flex items-center gap-1.5 text-zinc-500 dark:text-zinc-400">
            {hasPriorityIcon(priority) && (
              <PriorityIcon aria-label={priority} className="h-4 w-4" />
            )}
            <span className="text-xs">{PRIORITY_LABEL[priority]}</span>
          </span>
        </td>
        {showIds && (
          <td className="px-2 py-1.5 text-zinc-500 dark:text-zinc-400">
            <CopyIdTextButton id={item.id} label={item.shortId} className="text-[11px]" />
          </td>
        )}
        <td className="px-2 py-1.5 truncate">
          <span className="inline-flex items-center gap-1.5">
            <BlockMarkIcon item={item} />
            <RowLink
              to="/items"
              search={{ id: item.id }}
              claimModifiers
              keepSearch
              className="truncate cursor-pointer"
            >
              {item.title}
            </RowLink>
            {showTags && <TagBadges tags={item.tags} />}
          </span>
        </td>
        <td className="px-2 py-1.5 text-xs">{item.assignee ?? ''}</td>
        <td className="px-2 py-1.5 font-mono text-[11px] text-zinc-500 dark:text-zinc-400">
          {item.rank ?? ''}
        </td>
        <td className="px-2 py-1.5 text-xs whitespace-nowrap">
          <DueCell item={item} />
        </td>
        <td className="px-1 py-1 w-14 text-right whitespace-nowrap">
          {!showIds && (
            <CopyIdIconButton
              value={item.id}
              title="Copy id"
              className="mr-0.5 opacity-0 group-hover:opacity-100 focus:opacity-100"
            />
          )}
          <button
            type="button"
            onClick={scopeUnder}
            title="Filter under this item"
            aria-label="Filter under this item"
            className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-1 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
          >
            <Filter className="h-3 w-3" />
          </button>
        </td>
      </tr>
    </>
  )
}

function DueCell({ item }: { item: ItemSummary }) {
  const health = useQuery({ queryKey: qk.health, queryFn: api.health })
  const dueDisplay = useViewPrefs((s) => s.due)
  if (!item.due) return <span className="text-zinc-400">—</span>
  const closed = item.state === 'done' || item.state === 'dismissed'
  const status = dueStatus(item.due, todayIso(), health.data?.dueSoonDays ?? 3, closed)
  if (!dueShown(status, dueDisplay)) return null
  const StatusIcon = status === 'overdue' ? TriangleAlert : CalendarClock
  return (
    <span className={'inline-flex items-center gap-1 ' + dueStatusColor(status)}>
      {(status === 'overdue' || status === 'soon') && (
        <StatusIcon aria-hidden className="h-3.5 w-3.5 shrink-0" />
      )}
      <span title={item.due}>{formatDueDate(item.due)}</span>
    </span>
  )
}
