import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { FileText, Plus } from 'lucide-react'
import { designsApi, designsQk } from '../api/designs'
import { useArrowNav } from '../hooks/useArrowNav'
import { useAutoFocusOnData } from '../hooks/useAutoFocusOnData'
import type { NavRow } from '../lib/arrowNav'
import { RowLink } from './RowLink'

export type DesignsTab = 'active' | 'completed'

interface Props {
  tab: DesignsTab
  selectedSlug: string | null
  onSelectTab: (tab: DesignsTab) => void
  onSelectDoc: (slug: string) => void
  onNewDesign: () => void
}

const noToggle = () => {}

export function DesignsTree({
  tab,
  selectedSlug,
  onSelectTab,
  onSelectDoc,
  onNewDesign,
}: Props) {
  const completed = tab === 'completed'
  const list = useQuery({
    queryKey: designsQk.list(completed),
    queryFn: () => designsApi.list(completed),
  })
  const docs = useMemo(() => list.data ?? [], [list.data])

  // Reveal the selected row when selection is driven from outside the tree
  // (jump-to-ref, a markdown `design:` link). The list is flat, so there's
  // no ancestor chain to expand — just scroll the row into view. Runs again
  // when the list resolves, since a jump can land before data arrives.
  const scrollRef = useRef<HTMLDivElement>(null)
  const revealSlug = useCallback((slug: string) => {
    const el = scrollRef.current?.querySelector<HTMLElement>(`[data-reveal-slug="${slug}"]`)
    if (el) el.scrollIntoView({ block: 'nearest' })
  }, [])
  useEffect(() => {
    if (selectedSlug) revealSlug(selectedSlug)
  }, [selectedSlug, list.data, revealSlug])

  const navRows = useMemo<NavRow<string>[]>(
    () => docs.map((d) => ({ key: d.slug, parentKey: null, expandable: false, expanded: false, data: d.slug })),
    [docs],
  )
  const { onKeyDown } = useArrowNav<string>({
    rows: navRows,
    selectedKey: selectedSlug,
    onActivate: (row) => onSelectDoc(row.data),
    onToggle: noToggle,
  })

  useAutoFocusOnData(scrollRef, list.data)

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="flex items-center gap-1 px-2 py-2 border-b border-zinc-200 dark:border-zinc-800">
        <div className="flex rounded-md bg-zinc-100 dark:bg-zinc-800 p-0.5 text-xs font-medium">
          <TabButton active={!completed} onClick={() => onSelectTab('active')}>Active</TabButton>
          <TabButton active={completed} onClick={() => onSelectTab('completed')}>Completed</TabButton>
        </div>
        <div className="flex-1" />
        {!completed && (
          <button
            type="button"
            onClick={onNewDesign}
            title="New design"
            aria-label="New design"
            className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
          >
            <Plus className="h-4 w-4" />
          </button>
        )}
      </div>

      <div
        ref={scrollRef}
        tabIndex={0}
        onKeyDown={onKeyDown}
        className="flex-1 min-h-0 overflow-y-auto py-1 focus:outline-none"
      >
        {list.isLoading && (
          <div className="px-3 py-2 text-sm text-zinc-500">Loading…</div>
        )}
        {list.error && (
          <div className="px-3 py-2 text-sm text-red-600">Failed: {String(list.error)}</div>
        )}
        {list.data?.length === 0 && (
          <div className="px-3 py-2 text-sm italic text-zinc-500">
            {completed ? 'no archived designs' : 'no active designs'}
          </div>
        )}
        {list.data?.map((d) => (
          <div key={d.slug} data-reveal-slug={d.slug}>
            <DocRow
              title={d.title}
              slug={d.slug}
              tab={tab}
              active={d.slug === selectedSlug}
              onClick={() => onSelectDoc(d.slug)}
            />
          </div>
        ))}
      </div>
    </div>
  )
}

function TabButton({
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
      aria-pressed={active}
      className={
        'px-2.5 py-1 rounded ' +
        (active
          ? 'bg-white dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 shadow-sm'
          : 'text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200')
      }
    >
      {children}
    </button>
  )
}

function DocRow({
  title,
  slug,
  tab,
  active,
  onClick,
}: {
  title: string
  slug: string
  tab: DesignsTab
  active: boolean
  onClick: () => void
}) {
  return (
    <div
      className={
        'w-full flex items-center px-3 py-1.5 text-sm ' +
        (active
          ? 'bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-300 font-medium'
          : 'text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800/60')
      }
    >
      <RowLink
        to="/designs"
        search={{ tab, slug }}
        onSelect={onClick}
        className="flex-1 min-w-0 flex items-center gap-2 text-left select-none"
        title={title}
      >
        <FileText className="h-4 w-4 shrink-0 opacity-70" aria-hidden />
        <span className="truncate">{title}</span>
      </RowLink>
    </div>
  )
}
