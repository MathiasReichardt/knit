import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useSectionOpen } from '../stores/sectionOpen'

type LucideIcon = React.ComponentType<{ className?: string; 'aria-hidden'?: boolean }>

interface Props {
  icon: LucideIcon
  label: string
  actions?: React.ReactNode
  // Stable anchor id on the section wrapper, for scroll-to-region.
  id?: string
  defaultOpen?: boolean
  // When set, open/closed state is persisted in localStorage under this key,
  // shared across all items.
  persistKey?: string
  children: React.ReactNode
}

/**
 * Sticky-header collapsible section used by the details pane (Body-adjacent
 * groupings: Links, Comments, Context). The grey header strip is the toggle;
 * actions render only when open so clicks on Add (+) never compete with the
 * collapse hit-target.
 */
export function CollapsibleSection({
  icon: Icon,
  label,
  actions,
  id,
  defaultOpen = true,
  persistKey,
  children,
}: Props) {
  const persisted = useSectionOpen((s) =>
    persistKey ? s.byKey[persistKey] : undefined,
  )
  const setPersisted = useSectionOpen((s) => s.setOpen)
  const [localOpen, setLocalOpen] = useState(defaultOpen)
  const open = persistKey ? (persisted ?? defaultOpen) : localOpen
  const setOpen = (next: boolean | ((prev: boolean) => boolean)) => {
    const value = typeof next === 'function' ? next(open) : next
    if (persistKey) setPersisted(persistKey, value)
    else setLocalOpen(value)
  }
  const toggle = () => setOpen((v) => !v)
  return (
    <section id={id} className="pt-6">
      <div
        role="button"
        tabIndex={0}
        aria-expanded={open}
        onClick={toggle}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault()
            toggle()
          }
        }}
        className="group sticky top-0 z-10 flex items-center justify-between gap-2 px-5 py-1.5 border-b border-zinc-200 dark:border-zinc-800 bg-zinc-100 dark:bg-zinc-900/70 backdrop-blur-sm cursor-pointer select-none focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400 group-hover:text-zinc-700 dark:group-hover:text-zinc-200">
          <Icon className="h-3.5 w-3.5 text-blue-500 dark:text-blue-400" aria-hidden />
          <span>{label}</span>
          {open ? (
            <ChevronDown className="h-3.5 w-3.5" aria-hidden />
          ) : (
            <ChevronRight className="h-3.5 w-3.5" aria-hidden />
          )}
        </span>
        {open && actions && (
          <span onClick={(e) => e.stopPropagation()}>{actions}</span>
        )}
      </div>
      {open && <div className="px-5 pt-3">{children}</div>}
    </section>
  )
}
