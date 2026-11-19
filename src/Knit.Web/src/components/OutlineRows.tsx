import { Hash, Link, type LucideIcon } from 'lucide-react'
import { useCopySectionLink } from '../hooks/useCopySectionLink'

export interface OutlineEntry {
  depth: 2 | 3
  text: string
  id: string
  /** A fixed section beside the body (Links, Comments): shown with this icon, no section link. */
  icon?: LucideIcon
}

/**
 * A doc's h2/h3 outline: a click scrolls the body to the heading; the hover
 * icon copies the section link. Roving focus: only `focusId`'s row is in the
 * tab order, the owner moves it with the arrow keys.
 */
export function OutlineRows({
  items,
  docRef,
  activeHeadingId,
  focusId,
  onSelect,
  indent,
}: {
  items: OutlineEntry[]
  docRef: string
  activeHeadingId: string | null
  focusId: string | null
  /** `pointer`: a mouse click, not Enter / Space on the focused row. */
  onSelect: (id: string, pointer: boolean) => void
  indent: (depth: 2 | 3) => number | string
}) {
  const copy = useCopySectionLink(docRef)
  return (
    <div className="pb-1">
      {items.map((it) => {
        const active = it.id === activeHeadingId
        const Icon = it.icon ?? Hash
        return (
          <div key={it.id} className="group/row relative">
            <button
              type="button"
              data-outline-id={it.id}
              tabIndex={it.id === focusId ? 0 : -1}
              aria-current={active ? 'location' : undefined}
              onClick={(e) => onSelect(it.id, e.detail > 0)}
              title={it.text}
              style={{ paddingLeft: indent(it.depth) }}
              className={
                'w-full flex items-center gap-1.5 pr-8 py-1 text-left text-xs outline-none ' +
                'focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-blue-500 ' +
                (active
                  ? 'bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-300 font-medium'
                  : 'text-zinc-500 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-800/60')
              }
            >
              <Icon className="h-3 w-3 shrink-0 opacity-60" aria-hidden />
              <span className="truncate">{it.text}</span>
            </button>
            {!it.icon && (
              <button
                type="button"
                tabIndex={-1}
                onClick={() => void copy(it.id)}
                title="Copy link to section (c)"
                aria-label="Copy link to section"
                className={
                  'absolute right-2 top-1/2 -translate-y-1/2 rounded p-0.5 text-zinc-400 opacity-0 ' +
                  'group-hover/row:opacity-100 focus-visible:opacity-100 hover:text-zinc-700 hover:bg-zinc-200 ' +
                  'dark:hover:text-zinc-200 dark:hover:bg-zinc-700'
                }
              >
                <Link className="h-3 w-3" aria-hidden />
              </button>
            )}
          </div>
        )
      })}
    </div>
  )
}
