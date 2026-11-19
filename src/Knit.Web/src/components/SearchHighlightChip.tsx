import { useEffect, useSyncExternalStore } from 'react'
import { Highlighter, X } from 'lucide-react'
import {
  clearSearchHighlight,
  highlightQuery,
  isHighlightActive,
  subscribeHighlight,
} from '../lib/searchHighlight'

/**
 * Inline chip shown next to the search box while a search-jump word highlight
 * is painted. The highlight otherwise lingers until the pane is
 * left, with no way to dismiss it; this gives an explicit clear — click the X,
 * or press Esc.
 */
export function SearchHighlightChip() {
  const active = useSyncExternalStore(subscribeHighlight, isHighlightActive)
  const query = useSyncExternalStore(subscribeHighlight, highlightQuery)

  useEffect(() => {
    if (!active) return
    const onKey = (e: KeyboardEvent) => {
      // Don't steal Esc from a focused input (the search box clears its own
      // text first, dialogs close); only clear when the user isn't typing.
      if (e.key === 'Escape' && !isTyping(e.target)) clearSearchHighlight()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [active])

  if (!active) return null
  return (
    <div className="shrink-0 flex items-center gap-1.5 rounded-full border border-amber-300 dark:border-amber-700/60 bg-amber-50 dark:bg-amber-950/40 pl-2.5 pr-1 py-1 text-xs text-amber-800 dark:text-amber-200">
      <Highlighter className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span className="truncate max-w-[10rem]">“{query}”</span>
      <button
        type="button"
        onClick={() => clearSearchHighlight()}
        title="Clear highlight (Esc)"
        aria-label="Clear highlight"
        className="inline-flex items-center justify-center h-5 w-5 rounded-full hover:bg-amber-200/70 dark:hover:bg-amber-800/50"
      >
        <X className="h-3.5 w-3.5" aria-hidden />
      </button>
    </div>
  )
}

function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || target.isContentEditable
}
