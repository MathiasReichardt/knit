import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderInput } from 'lucide-react'
import { qk } from '../api/client'
import { projectsApi, projectsQk, type MilestoneMoveResult } from '../api/projects'
import { useToasts } from '../stores/toasts'

interface Props {
  project: string
  slug: string
  title: string
  onClose: () => void
  onMoved: (result: MilestoneMoveResult) => void
}

/**
 * Move a milestone sub-doc into another project. The destination is picked from
 * the existing project folders (never free text, so an unknown project can't be
 * typed), and the new-slug field only appears when the destination already
 * holds this slug — the one case the server rejects without it.
 *
 * Items keep their Milestone link: the rows are re-pointed, not reassigned, so
 * some may end up serving another project's milestone. The toast reports that
 * count, because it is the part the user has to learn.
 */
export function MilestoneMoveDialog({ project, slug, title, onClose, onMoved }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [toProject, setToProject] = useState('')
  const [newSlug, setNewSlug] = useState('')
  const [error, setError] = useState<string | null>(null)

  const folders = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const destinations = useMemo(
    () => (folders.data ?? []).filter((f) => f.slug !== project),
    [folders.data, project],
  )
  const collides = useMemo(
    () => destinations.find((f) => f.slug === toProject)?.milestones.some((m) => m.slug === slug) ?? false,
    [destinations, toProject, slug],
  )

  const move = useMutation({
    mutationFn: () =>
      projectsApi.moveMilestone(project, slug, toProject, collides ? newSlug.trim() : undefined),
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ['projects'] })
      void qc.invalidateQueries({ queryKey: qk.items })
      void qc.invalidateQueries({ queryKey: qk.tree })
      // The sub-doc changes parent folder — two FS events the watcher
      // sometimes collapses, so force the tree rather than wait for SSE.
      void qc.refetchQueries({ queryKey: qk.tree })
      const parts = [`Moved → ${r.toProject}/${r.newSlug}`]
      if (r.repointedItemIds.length > 0) parts.push(`${r.repointedItemIds.length} item(s) re-pointed`)
      if (r.crossProjectItemIds.length > 0)
        parts.push(`${r.crossProjectItemIds.length} now link across projects`)
      toast('success', parts.join(' · '))
      onMoved(r)
    },
    onError: (e) => setError(String(e)),
  })

  const busy = move.isPending
  const ready = toProject !== '' && (!collides || newSlug.trim() !== '')

  return (
    <div
      className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center p-4"
      onClick={busy ? undefined : onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900 p-4 space-y-3"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="flex items-center gap-2 text-sm font-semibold text-zinc-800 dark:text-zinc-100">
          <FolderInput className="h-4 w-4" aria-hidden />
          Move milestone {title}
        </h3>

        {folders.isLoading ? (
          <p className="text-xs text-zinc-500">Loading projects…</p>
        ) : destinations.length === 0 ? (
          <p className="text-xs text-zinc-500">
            No other project to move into — <span className="font-mono">{project}</span> is the only one.
          </p>
        ) : (
          <div className="space-y-2 text-xs text-zinc-600 dark:text-zinc-300">
            <label className="flex flex-col gap-1">
              <span className="font-medium">Destination project</span>
              <select
                autoFocus
                value={toProject}
                onChange={(e) => setToProject(e.target.value)}
                className="bg-transparent border border-zinc-300 dark:border-zinc-700 rounded px-1.5 py-1 focus:outline-none focus:ring-1 focus:ring-blue-500"
              >
                <option value="">Pick a project…</option>
                {destinations.map((f) => (
                  <option key={f.slug} value={f.slug}>{f.slug}</option>
                ))}
              </select>
            </label>

            {collides && (
              <label className="flex flex-col gap-1">
                <span className="font-medium text-amber-700 dark:text-amber-400">
                  <span className="font-mono">{toProject}</span> already has a milestone
                  <span className="font-mono"> {slug}</span> — pick a new slug
                </span>
                <input
                  type="text"
                  value={newSlug}
                  onChange={(e) => setNewSlug(e.target.value)}
                  placeholder={slug}
                  aria-label="New milestone slug"
                  className="bg-transparent border border-zinc-300 dark:border-zinc-700 rounded px-1.5 py-1 font-mono focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
              </label>
            )}

            <p className="text-zinc-500">
              Linked items are re-pointed, never reassigned — an item may end up serving
              another project&apos;s milestone. <span className="font-mono">knit verify</span> lists
              those as informational.
            </p>
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
            disabled={busy || !ready}
            onClick={() => { setError(null); move.mutate() }}
            title="Move this milestone into the selected project"
            className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
          >
            <FolderInput className="h-3.5 w-3.5" aria-hidden />
            {busy ? 'Moving…' : 'Move'}
          </button>
        </div>
      </div>
    </div>
  )
}
