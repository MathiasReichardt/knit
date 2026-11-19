import { Lock } from 'lucide-react'

/** Marks a view that can't be edited here, with why on hover. */
export function ReadOnlyBadge({ reason }: { reason: string }) {
  return (
    <span
      className="inline-flex shrink-0 items-center gap-1 rounded bg-zinc-100 px-1.5 py-0.5 font-sans text-[11px] font-medium text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400"
      title={reason}
    >
      <Lock className="h-3 w-3" aria-hidden />
      Read-only
    </span>
  )
}
