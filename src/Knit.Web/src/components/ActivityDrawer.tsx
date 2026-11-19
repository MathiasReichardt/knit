import { useEffect } from 'react'
import { useNavigate } from '@tanstack/react-router'
import {
  ChevronDown,
  ChevronUp,
  MessageSquare,
  Notebook,
  Pencil,
  Plus,
  Trash2,
} from 'lucide-react'
import type { ActivityCategory, ActivityEntry } from '../api/types'
import { api } from '../api/client'
import type { IconCmp } from '../lib/glyphs'
import {
  activityNavTarget,
  activityVerb,
  relativeTime,
} from '../lib/activityFormat'
import { ACTIVITY_CHIPS, isVisible, useActivity } from '../stores/activity'

const CAT_ICON: Record<ActivityCategory, IconCmp> = {
  created: Plus,
  state: ChevronUp,
  comment: MessageSquare,
  kb: Notebook,
  other: Pencil,
}

function rowIcon(e: ActivityEntry): IconCmp {
  if (e.op === 'deleted') return Trash2
  return CAT_ICON[e.category]
}

export function ActivityDrawer() {
  const open = useActivity((s) => s.open)
  const entries = useActivity((s) => s.entries)
  const filters = useActivity((s) => s.filters)
  const toggleOpen = useActivity((s) => s.toggleOpen)
  const toggleFilter = useActivity((s) => s.toggleFilter)
  const hydrate = useActivity((s) => s.hydrate)
  const navigate = useNavigate()

  // Hydrate from the server buffer only while the drawer is open. Live
  // entries arrive on the SSE stream regardless; this fills history (and
  // gaps after a reconnect) without an HTTP request when the feed is hidden.
  useEffect(() => {
    if (!open) return
    let cancelled = false
    api.activity().then(
      (e) => {
        if (!cancelled) hydrate(e)
      },
      () => {},
    )
    return () => {
      cancelled = true
    }
  }, [open, hydrate])

  const visible = entries.filter((e) => isVisible(filters, e.category))

  const go = (e: ActivityEntry) => {
    const t = activityNavTarget(e)
    void navigate({ to: t.to, search: t.search as never })
  }

  if (!open) {
    return (
      <div className="shrink-0 border-t border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/40">
        <button
          type="button"
          onClick={toggleOpen}
          title="Show activity feed"
          aria-label="Show activity feed"
          className="flex items-center gap-2 px-4 h-8 w-full text-xs text-zinc-600 dark:text-zinc-300 hover:bg-zinc-200/60 dark:hover:bg-zinc-800"
        >
          <ChevronUp className="h-4 w-4" aria-hidden />
          <span className="font-medium">Activity</span>
          {visible.length > 0 && (
            <span className="text-zinc-400 dark:text-zinc-500">{visible.length}</span>
          )}
        </button>
      </div>
    )
  }

  return (
    <section
      aria-label="Activity feed"
      className="shrink-0 h-[25vh] min-h-32 flex flex-col border-t border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/40"
    >
      <div
        onClick={toggleOpen}
        title="Collapse activity feed"
        className="shrink-0 flex items-center gap-2 px-3 py-1.5 border-b border-zinc-200 dark:border-zinc-800 cursor-pointer select-none"
      >
        <span className="text-xs font-medium text-zinc-600 dark:text-zinc-300">Activity</span>
        <div className="flex items-center gap-1">
          {ACTIVITY_CHIPS.map(({ cat, label }) => {
            const on = filters[cat]
            return (
              <button
                key={cat}
                type="button"
                onClick={(ev) => {
                  ev.stopPropagation()
                  toggleFilter(cat)
                }}
                aria-pressed={on}
                className={
                  'rounded-full px-2 py-0.5 text-[11px] border transition-colors ' +
                  (on
                    ? 'bg-zinc-900 text-white border-zinc-900 dark:bg-zinc-100 dark:text-zinc-900 dark:border-zinc-100'
                    : 'text-zinc-500 dark:text-zinc-400 border-zinc-300 dark:border-zinc-700 hover:bg-zinc-200/60 dark:hover:bg-zinc-800')
                }
              >
                {label}
              </button>
            )
          })}
        </div>
        <div className="flex-1" />
        <button
          type="button"
          onClick={(ev) => {
            ev.stopPropagation()
            toggleOpen()
          }}
          title="Collapse activity feed"
          aria-label="Collapse activity feed"
          className="inline-flex items-center justify-center h-6 w-6 rounded text-zinc-500 dark:text-zinc-400 hover:bg-zinc-200/60 dark:hover:bg-zinc-800"
        >
          <ChevronDown className="h-4 w-4" aria-hidden />
        </button>
      </div>

      <ul className="flex-1 min-h-0 overflow-auto text-sm">
        {visible.length === 0 ? (
          <li className="px-4 py-3 text-xs text-zinc-400 dark:text-zinc-500">
            No activity yet — changes show up here as you work.
          </li>
        ) : (
          visible.map((e) => {
            const Icon = rowIcon(e)
            return (
              <li key={e.seq}>
                <button
                  type="button"
                  onClick={() => go(e)}
                  className="flex items-center gap-2 w-full px-3 py-1 text-left hover:bg-zinc-200/60 dark:hover:bg-zinc-800"
                >
                  <Icon className="h-3.5 w-3.5 shrink-0 text-zinc-400 dark:text-zinc-500" aria-hidden />
                  <span className="shrink-0 w-12 text-[11px] tabular-nums text-zinc-400 dark:text-zinc-500">
                    {relativeTime(e.ts)}
                  </span>
                  {e.actor && (
                    <span className="shrink-0 text-xs font-medium text-zinc-600 dark:text-zinc-300">
                      {e.actor}
                    </span>
                  )}
                  <span className="shrink-0 text-xs text-zinc-500 dark:text-zinc-400">
                    {activityVerb(e)}
                  </span>
                  <span className="truncate text-xs text-zinc-800 dark:text-zinc-100">
                    {e.title}
                  </span>
                </button>
              </li>
            )
          })
        )}
      </ul>
    </section>
  )
}
