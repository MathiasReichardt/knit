import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { AlertTriangle, Trash2 } from 'lucide-react'
import { api, qk } from '../api/client'
import { projectsApi } from '../api/projects'
import { useToasts } from '../stores/toasts'

interface Props {
  project: string
  slug: string
  title: string
  onClose: () => void
  onDeleted: () => void
}

/**
 * Confirm-before-remove for a milestone sub-doc. Opens with a dry-run that
 * lists the items whose Milestone back-links point here. Those back-links are
 * left dangling by default (matching `knit delete` and the "nothing vanishes"
 * verify/doctor invariant); an opt-in checkbox scrubs them instead.
 */
export function MilestoneDeleteDialog({ project, slug, title, onClose, onDeleted }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [scrubLinks, setScrubLinks] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const preview = useQuery({
    queryKey: ['projects', 'milestone-delete-preview', project, slug],
    queryFn: () => projectsApi.removeMilestone(project, slug, { dryRun: true }),
    staleTime: 0,
    gcTime: 0,
  })

  const items = useQuery({
    queryKey: qk.items,
    queryFn: () => api.items(),
    enabled: (preview.data?.danglingItemIds.length ?? 0) > 0,
  })
  const dangling = (preview.data?.danglingItemIds ?? []).map((id) => {
    const match = items.data?.find((it) => it.id === id)
    return { id, shortId: match?.shortId ?? id.slice(0, 8), title: match?.title ?? null }
  })

  const del = useMutation({
    mutationFn: () => projectsApi.removeMilestone(project, slug, { scrubLinks }),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: ['projects'] })
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      const parts = [`Deleted milestone ${project}/${slug}`]
      if (result.scrubbedItemIds.length > 0)
        parts.push(`${result.scrubbedItemIds.length} link(s) scrubbed`)
      else if (result.danglingItemIds.length > 0)
        parts.push(`${result.danglingItemIds.length} link(s) left dangling`)
      toast('success', parts.join(' · '))
      onDeleted()
    },
    onError: (e) => setError(String(e)),
  })

  const busy = del.isPending

  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={busy ? undefined : onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-red-300 dark:border-red-900 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="flex items-center gap-2 text-sm font-semibold text-red-700 dark:text-red-400">
          <AlertTriangle className="h-4 w-4" aria-hidden />
          Delete milestone {title}
        </h3>

        {preview.isLoading ? (
          <p className="text-xs text-zinc-500">Checking what this affects…</p>
        ) : preview.error ? (
          <p className="text-xs text-red-600">Failed to load: {String(preview.error)}</p>
        ) : (
          <div className="space-y-2 text-xs text-zinc-600 dark:text-zinc-300">
            <p>
              The milestone sub-doc (
              <span className="font-mono">.knit/projects/{project}/milestones/{slug}.md</span>)
              will be deleted.
            </p>

            {dangling.length > 0 ? (
              <div className="space-y-1.5 rounded border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/30 p-2">
                <p className="font-medium text-amber-700 dark:text-amber-400">
                  {dangling.length} item(s) link here (Milestone rows):
                </p>
                <ul className="space-y-0.5 max-h-32 overflow-y-auto">
                  {dangling.map((it) => (
                    <li key={it.id}>
                      <Link
                        to="/items"
                        search={(prev) => ({ ...prev, id: it.id })}
                        onClick={onClose}
                        title={`Open ${it.shortId} in items`}
                        className="flex items-baseline gap-2 rounded px-1 py-0.5 -mx-1 hover:bg-zinc-100 dark:hover:bg-zinc-800 hover:text-blue-600 dark:hover:text-blue-400"
                      >
                        <span className="font-mono text-zinc-500 shrink-0">{it.shortId}</span>
                        <span className="truncate">{it.title ?? <em className="text-zinc-400">(untitled)</em>}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
                <label className="flex items-center gap-2 pt-1 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={scrubLinks}
                    onChange={(e) => setScrubLinks(e.target.checked)}
                    className="h-3.5 w-3.5"
                  />
                  <span>Also remove those links (default: leave dangling, surfaced by verify)</span>
                </label>
              </div>
            ) : (
              <p className="text-zinc-500">No items link this milestone.</p>
            )}

            <p className="text-zinc-500">Recoverable via git until committed.</p>
          </div>
        )}

        {error && <p className="text-xs text-red-600">{error}</p>}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="text-xs px-2 py-1 rounded border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy || preview.isLoading || !!preview.error}
            onClick={() => {
              setError(null)
              del.mutate()
            }}
            title="Delete this milestone"
            className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden />
            {busy ? 'Deleting…' : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}
