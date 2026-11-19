import { Link, useRouterState } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { CalendarRange, FolderKanban, ListTree, Notebook, PenLine, Settings, Stethoscope } from 'lucide-react'
import { api, qk } from '../api/client'
import type { IconCmp } from '../lib/glyphs'

interface NavItem {
  to: string
  label: string
  Icon: IconCmp
}

const ITEMS: NavItem[] = [
  { to: '/items', label: 'Items', Icon: ListTree },
  { to: '/designs', label: 'Designs', Icon: PenLine },
  { to: '/kb', label: 'Knowledge base', Icon: Notebook },
  // Projects is the PM lens (roadmap / milestones / todos + work items) to
  // Items' dev lens; Timeline closes the row as the deadline view.
  { to: '/projects', label: 'Projects', Icon: FolderKanban },
  { to: '/timeline', label: 'Timeline', Icon: CalendarRange },
]

// Doctor and Settings sit at the bottom of the rail — health/meta surfaces,
// not peers of the content views; settings directly below doctor.
const BOTTOM_ITEMS: NavItem[] = [
  { to: '/doctor', label: 'Doctor', Icon: Stethoscope },
  { to: '/settings', label: 'Settings', Icon: Settings },
]

export function NavRail() {
  const location = useRouterState({ select: (s) => s.location.pathname })
  return (
    <nav
      aria-label="Primary"
      className="flex flex-col items-center gap-1 w-12 shrink-0 border-r border-zinc-200 dark:border-zinc-800 py-2 bg-zinc-50 dark:bg-zinc-900/40"
    >
      {ITEMS.map((it) => (
        <NavEntry key={it.to} item={it} active={location.startsWith(it.to)} />
      ))}
      <div className="mt-auto" />
      {BOTTOM_ITEMS.map((it) => (
        <NavEntry key={it.to} item={it} active={location.startsWith(it.to)} />
      ))}
    </nav>
  )
}

function NavEntry({ item, active }: { item: NavItem; active: boolean }) {
  const Icon = item.Icon
  const dot = item.to === '/timeline' ? <TimelineAttentionDot /> : null
  return (
    <Link
      to={item.to}
      title={item.label}
      aria-label={item.label}
      className={
        'relative inline-flex items-center justify-center h-9 w-9 rounded-md ' +
        (active
          ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
          : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-200/70 dark:hover:bg-zinc-800')
      }
    >
      <Icon className="h-5 w-5" aria-hidden />
      {dot}
    </Link>
  )
}

// A small status dot when open items need attention: red if any are overdue,
// yellow if any are due soon (red wins). Computed repo-wide from /api/health,
// independent of the current filter — a reliable global "something's due"
// signal (same pattern as DoctorChip: absence means all clear).
function TimelineAttentionDot() {
  const health = useQuery({
    queryKey: qk.health,
    queryFn: api.health,
    staleTime: 60_000,
    retry: false,
  })
  const overdue = health.data?.overdueCount ?? 0
  const dueSoon = health.data?.dueSoonCount ?? 0
  if (overdue === 0 && dueSoon === 0) return null
  const color = overdue > 0 ? 'bg-red-500' : 'bg-amber-400'
  const label = overdue > 0
    ? `${overdue} open item${overdue === 1 ? '' : 's'} overdue`
    : `${dueSoon} open item${dueSoon === 1 ? '' : 's'} due soon`
  return (
    <span
      role="status"
      aria-label={label}
      title={label}
      className={`absolute top-1 right-1 h-2 w-2 rounded-full ${color}`}
    />
  )
}
