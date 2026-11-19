import { Ban } from 'lucide-react'
import type { HeldBy } from '../api/types'
import { blockMark } from '../lib/blockMark'

// Row marker: the item's own block, or a dimmer one when a blocked parent holds it.
export function BlockMarkIcon({
  item,
}: {
  item: { blocked: boolean; blockedReason: string | null; heldBy?: HeldBy | undefined }
}) {
  const mark = blockMark(item)
  if (!mark) return null
  return (
    <span
      title={mark.label}
      aria-label={mark.label}
      className={'text-red-600 dark:text-red-400 shrink-0' + (mark.held ? ' opacity-50' : '')}
    >
      <Ban className="h-3.5 w-3.5" />
    </span>
  )
}
