import { useRouterState } from '@tanstack/react-router'
import { ExternalLink } from 'lucide-react'

// The current view in another tab. An anchor, not window.open: a middle- or
// Ctrl+click opens it in the background, which a script cannot.
export function OpenInNewTabIconButton() {
  const href = useRouterState({ select: (s) => s.location.href })
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener"
      title="Open in new tab"
      aria-label="Open in new tab"
      className="p-1 rounded shrink-0 text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800"
    >
      <ExternalLink className="h-3 w-3" />
    </a>
  )
}
