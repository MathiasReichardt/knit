import { Pencil } from 'lucide-react'

/**
 * The body's edit pen, top right. It sticks to the top of the scroll area, so
 * it stays in reach far down a long body. Shown on hover of the enclosing
 * `group`. Place it as the body section's first child — or `inline` inside a
 * column that sticks itself (the outline's), where it shows always, like the
 * outline beneath it.
 */
export function EditBodyButton({ onClick, inline = false }: { onClick: () => void; inline?: boolean }) {
  const button = (
    <button
      type="button"
      onClick={onClick}
      title="Edit body (double-click body)"
      aria-label="Edit body"
      className={
        (inline ? '' : 'absolute right-0 top-0 opacity-0 group-hover:opacity-100 focus:opacity-100 ') +
        'p-1 rounded text-zinc-500 ' +
        'bg-white/90 dark:bg-zinc-950/90 hover:text-blue-600 hover:bg-zinc-100 dark:hover:bg-zinc-900'
      }
    >
      <Pencil className="h-4 w-4" />
    </button>
  )
  if (inline) return button
  return (
    // Zero height: it takes no room above the body, and sticks while the body scrolls by.
    <div className="sticky top-2 z-10 h-0">{button}</div>
  )
}
