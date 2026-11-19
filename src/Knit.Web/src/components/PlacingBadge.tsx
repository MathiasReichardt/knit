import { Loader2 } from 'lucide-react'

/**
 * Shown while the server places a dropped drag. The client no longer computes
 * the rank, so the row settles only on the response — without this the wait
 * reads as a drag dropped on the floor (30).
 */
export function PlacingBadge() {
  return (
    <div
      role="status"
      className="absolute bottom-3 right-3 z-20 inline-flex items-center gap-1.5 rounded-full border border-zinc-300 dark:border-zinc-700 bg-white/95 dark:bg-zinc-900/95 px-2.5 py-1 text-xs text-zinc-700 dark:text-zinc-200 shadow"
    >
      <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />
      Placing…
    </div>
  )
}
