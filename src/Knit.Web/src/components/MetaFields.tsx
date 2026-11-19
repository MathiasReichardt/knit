import type { LucideIcon } from 'lucide-react'

// A labeled metadata grid cell shared by the item / KB frontmatter strips.
// KB's original `Field` was the superset (label + optional tooltip + icon +
// className); this unifies both so they can't drift apart.
export function MetaField({
  label,
  tooltip,
  icon: Icon,
  className,
  children,
}: {
  label: string
  tooltip?: string
  icon?: LucideIcon
  className?: string
  children: React.ReactNode
}) {
  return (
    <div className={'flex flex-col gap-0.5 min-w-0 ' + (className ?? '')}>
      <span
        className="inline-flex items-center gap-1 text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-500 w-fit"
        title={tooltip}
      >
        {Icon && <Icon className="h-3 w-3" aria-hidden />}
        {label}
      </span>
      <div className="text-zinc-800 dark:text-zinc-200 min-w-0">{children}</div>
    </div>
  )
}

// A muted, dot-separated footer for read-only provenance / lifecycle facts that
// don't warrant an editable-field grid cell. Each surface composes its own
// items (spans, links) since their content differs; this owns only the styling
// and separators. Renders nothing when every item is falsy.
export function MetaFooter({ items }: { items: React.ReactNode[] }) {
  const shown = items.filter((n) => n != null && n !== false)
  if (shown.length === 0) return null
  return (
    <div className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5 text-[11px] text-zinc-400 dark:text-zinc-500">
      {shown.map((node, i) => (
        <span key={i} className="inline-flex items-center gap-x-1.5">
          {i > 0 && <span aria-hidden className="text-zinc-300 dark:text-zinc-600">·</span>}
          {node}
        </span>
      ))}
    </div>
  )
}
