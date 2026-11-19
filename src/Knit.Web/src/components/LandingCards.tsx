import { Link } from '@tanstack/react-router'
import { BookOpen, FileText, Ban, Clock, ListTodo } from 'lucide-react'
import type { ReactNode } from 'react'
import type { DesignSummary } from '../api/designs'
import type { KbSummary } from '../api/kb'
import type { ProjectLanding } from '../api/projects'
import type { ItemHealth, ItemSummary } from '../api/types'
import {
  TYPE_ICON, TYPE_COLOR, STATE_ICON, STATE_COLOR, STATE_LABEL,
  HEALTH_ICON, HEALTH_COLOR, HEALTH_LABEL, type IconCmp,
} from '../lib/glyphs'
import { relativeAgo, exactTime } from '../lib/relativeTime'
import { countdown, formatDueDate, todayIso } from '../lib/dueStatus'
import { barFilled } from '../lib/status'
import { blockMark } from '../lib/blockMark'

// Shared card chrome: rounded border, hover lift, roving-focus ring. Both the
// CardShell family and the project card use it so they can't drift apart.
const CARD_CLASS =
  'group flex h-full w-full min-w-0 flex-col gap-2 rounded-lg border border-zinc-200 bg-white p-3 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-zinc-300 hover:shadow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500 dark:border-zinc-800 dark:bg-zinc-900 dark:hover:border-zinc-700'

interface ShellProps {
  to: string
  search?: Record<string, unknown>
  icon: IconCmp
  iconClass?: string
  /** Small state glyph overlaid on the icon (items only). */
  overlay?: ReactNode
  title: string
  subtitle?: ReactNode
  modified?: string | null
  badge?: ReactNode
}

// All landing cards are <Link>s styled as big buttons: rounded border, hover
// lift, title clamped to two lines. `data-landing-card` marks them for the
// roving focus ring.
function CardShell({ to, search, icon: Icon, iconClass, overlay, title, subtitle, modified, badge }: ShellProps) {
  const rel = modified ? relativeAgo(modified) : ''
  return (
    <Link
      to={to as never}
      search={search as never}
      data-landing-card=""
      className={CARD_CLASS}
    >
      <div className="flex items-start gap-2">
        <span className="relative shrink-0">
          <Icon className={'h-5 w-5 ' + (iconClass ?? 'text-zinc-500 dark:text-zinc-400')} aria-hidden />
          {overlay}
        </span>
        {badge}
      </div>
      <div className="line-clamp-2 text-sm font-medium text-zinc-900 dark:text-zinc-100">{title}</div>
      {subtitle && <div className="truncate text-xs text-zinc-500 dark:text-zinc-400">{subtitle}</div>}
      {rel && (
        <div className="mt-auto text-xs text-zinc-400 dark:text-zinc-500" title={exactTime(modified!)}>
          edited {rel}
        </div>
      )}
    </Link>
  )
}

const BAR_CELLS = 14

/**
 * The landing projects row's card: one project's headline — progress,
 * health, next milestone, attention counts. Deliberately not a dashboard; the
 * Projects view owns detail.
 *
 * A null `slug` is the server-synthesized "All items" stand-in for a repo with
 * no projects. It carries no health and no milestone line by construction and
 * links to /items, so it can't be mistaken for a project someone should go
 * looking for.
 */
export function ProjectCard({ project: p }: { project: ProjectLanding }) {
  const standIn = p.slug === null
  const target = standIn
    ? { to: '/items', search: {} }
    : p.itemId
      ? { to: '/projects', search: { id: p.itemId } }
      : { to: '/projects', search: { doc: `${p.slug}/readme.md` } }

  return (
    <Link
      to={target.to as never}
      search={target.search as never}
      data-landing-card=""
      className={CARD_CLASS}
    >
      <div className="flex items-start gap-2">
        {standIn ? (
          <ListTodo className="h-5 w-5 shrink-0 text-zinc-500 dark:text-zinc-400" aria-hidden />
        ) : (
          <ProjectIcon className={'h-5 w-5 shrink-0 ' + TYPE_COLOR.project} aria-hidden />
        )}
        <span className="line-clamp-2 min-w-0 text-sm font-medium text-zinc-900 dark:text-zinc-100">
          {p.title}
        </span>
        <HealthChip health={p.health} />
      </div>

      <div className="flex items-center gap-1.5">
        <Bar percent={p.percent} />
        <span className="text-xs font-medium tabular-nums text-zinc-600 dark:text-zinc-300">
          {p.percent}%
        </span>
      </div>

      {p.nextMilestoneTitle && (
        <div
          className="truncate text-xs text-zinc-500 dark:text-zinc-400"
          title={p.nextMilestoneTarget ? formatDueDate(p.nextMilestoneTarget) : undefined}
        >
          next: {p.nextMilestoneTitle}
          {p.nextMilestoneTarget && ` · ${countdown(p.nextMilestoneTarget, todayIso())}`}
        </div>
      )}

      <div className="mt-auto flex items-center gap-3 text-xs text-zinc-500 dark:text-zinc-400">
        {p.blocked > 0 && (
          <span className="inline-flex items-center gap-1 text-red-600 dark:text-red-400">
            <Ban className="h-3 w-3" aria-hidden />
            {p.blocked} blocked
          </span>
        )}
        {p.overdue > 0 && (
          <span className="inline-flex items-center gap-1 text-amber-600 dark:text-amber-400">
            <Clock className="h-3 w-3" aria-hidden />
            {p.overdue} overdue
          </span>
        )}
        {p.blocked === 0 && p.overdue === 0 && <span className="tabular-nums">{p.total} items</span>}
      </div>
    </Link>
  )
}

const ProjectIcon = TYPE_ICON.project

// Compact twin of the SummaryStrip bar — same barFilled math, fewer cells.
function Bar({ percent }: { percent: number }) {
  const filled = barFilled(percent, BAR_CELLS)
  return (
    <span role="img" aria-label={`${percent} percent done`} className="inline-flex items-center gap-px">
      {Array.from({ length: BAR_CELLS }, (_, i) => (
        <span
          key={i}
          aria-hidden
          className={
            'h-2 w-1 rounded-[1px] ' +
            (i < filled ? 'bg-emerald-500 dark:bg-emerald-400' : 'bg-zinc-200 dark:bg-zinc-800')
          }
        />
      ))}
    </span>
  )
}

// Icon-only at this card width; the label rides in the tooltip. Follows the
// SummaryStrip rule — health shows whenever set, including on-track.
function HealthChip({ health }: { health: ItemHealth | null }) {
  if (!health) return null
  const Icon = HEALTH_ICON[health]
  return (
    <span className={'ml-auto shrink-0 ' + HEALTH_COLOR[health]} title={HEALTH_LABEL[health]}>
      <Icon className="h-4 w-4" aria-label={HEALTH_LABEL[health]} />
    </span>
  )
}

export function DesignCard({ design }: { design: DesignSummary }) {
  return (
    <CardShell
      to="/designs"
      search={{ slug: design.slug }}
      icon={FileText}
      iconClass="text-violet-600 dark:text-violet-400"
      title={design.title || design.slug}
      modified={design.modified}
    />
  )
}

export function KbCard({ entry }: { entry: KbSummary }) {
  return (
    <CardShell
      to="/kb"
      search={{ bucket: entry.bucket, slug: entry.slug }}
      icon={BookOpen}
      iconClass="text-amber-600 dark:text-amber-400"
      title={entry.title || entry.slug}
      subtitle={entry.bucket}
      modified={entry.modified}
    />
  )
}

export function ItemCard({ item }: { item: ItemSummary }) {
  const TypeIcon = TYPE_ICON[item.type]
  const StateIcon = STATE_ICON[item.state]
  return (
    <CardShell
      to="/items"
      search={{ id: item.id }}
      icon={TypeIcon}
      iconClass={TYPE_COLOR[item.type]}
      overlay={
        <span
          className="absolute -bottom-1 -right-1 rounded-full bg-white dark:bg-zinc-900"
          title={STATE_LABEL[item.state]}
        >
          <StateIcon className={'h-3 w-3 ' + STATE_COLOR[item.state]} aria-hidden />
        </span>
      }
      title={item.title}
      modified={item.modified}
      badge={
        item.blocked || item.heldBy ? (
          <span
            className="ml-auto inline-flex items-center gap-1 rounded bg-red-50 px-1.5 py-0.5 text-xs text-red-600 dark:bg-red-950/40 dark:text-red-400"
            title={blockMark(item)?.label}
          >
            <Ban className="h-3 w-3" aria-hidden />
            blocked
          </span>
        ) : undefined
      }
    />
  )
}
