import { useState } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { AlertTriangle, Trash2 } from 'lucide-react'
import { Link } from '@tanstack/react-router'
import { kbApi, type KbBucket, type KbDeleteResult } from '../api/kb'
import { api, qk } from '../api/client'

interface Props {
  entryRef: string
  onClose: () => void
  onDeleted: (result: KbDeleteResult) => void
}

/**
 * Confirm-before-delete for a KB entry. Opens with a dry-run that reports
 * how many items' kb_refs will be stripped and — for a folder-form entry
 * with sub-docs/attachments — the blocking files. Those siblings require an
 * explicit "delete folder" opt-in before the entry can go.
 */
export function KbDeleteDialog({ entryRef, onClose, onDeleted }: Props) {
  const [deleteFolder, setDeleteFolder] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const preview = useQuery({
    queryKey: ['kb', 'delete-preview', entryRef],
    queryFn: () => kbApi.delete(entryRef, { dryRun: true }),
    staleTime: 0,
    gcTime: 0,
  })

  // Resolve the affected item ids (the preview returns full uuids) to their
  // short id + title so the dialog can name the items losing a ref.
  const items = useQuery({
    queryKey: qk.items,
    queryFn: () => api.items(),
    enabled: (preview.data?.itemsAffected.length ?? 0) > 0,
  })
  const affected = (preview.data?.itemsAffected ?? []).map((id) => {
    const match = items.data?.find((it) => it.id === id)
    return { id, shortId: match?.shortId ?? id.slice(0, 8), title: match?.title ?? null }
  })

  // KB entries whose frontmatter refs (supersedes / superseded_by /
  // feature) point at the entry being deleted — those rows get cleared.
  const kbAffected = (preview.data?.kbEntriesAffected ?? []).map((ref) => {
    const colon = ref.indexOf(':')
    const bucket = (colon > 0 ? ref.slice(0, colon) : '') as KbBucket
    const slug = colon > 0 ? ref.slice(colon + 1) : ref
    return { ref, bucket, slug }
  })

  const hasBlockers = (preview.data?.blockingFiles.length ?? 0) > 0
  const blockedUnconfirmed = hasBlockers && !deleteFolder

  const del = useMutation({
    mutationFn: () => kbApi.delete(entryRef, { deleteFolder }),
    onSuccess: (result) => {
      // A 409 comes back as a result with blockingFiles set, not a throw.
      if (result.blockingFiles.length > 0 && !deleteFolder) {
        setError('This entry has sub-docs — confirm "delete folder" to remove it.')
        return
      }
      onDeleted(result)
    },
    onError: (e) => setError(String(e)),
  })

  const busy = del.isPending
  const path = preview.data?.deletedPath ?? null

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
          Delete {entryRef}
        </h3>

        {preview.isLoading ? (
          <p className="text-xs text-zinc-500">Checking what this affects…</p>
        ) : preview.error ? (
          <p className="text-xs text-red-600">Failed to load: {String(preview.error)}</p>
        ) : (
          <div className="space-y-2 text-xs text-zinc-600 dark:text-zinc-300">
            {path && (
              <p className="font-mono break-all text-zinc-500">{path}</p>
            )}
            {affected.length === 0 ? (
              <p>No items reference this entry.</p>
            ) : (
              <div className="space-y-1.5">
                <p>
                  {affected.length} item(s) link to this entry. The matching{' '}
                  <span className="font-medium">KB ref</span> row will be removed
                  from each item's <span className="font-mono">## Links</span>{' '}
                  table. Markdown links in item descriptions are not touched.
                </p>
                <ul className="space-y-0.5 max-h-32 overflow-y-auto rounded border border-zinc-200 dark:border-zinc-800 p-1.5">
                  {affected.map((it) => (
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
              </div>
            )}
            {kbAffected.length > 0 && (
              <div className="space-y-1.5">
                <p>
                  {kbAffected.length} KB entry(s) reference this one in
                  frontmatter (<span className="font-mono">supersedes</span> /{' '}
                  <span className="font-mono">superseded_by</span> /{' '}
                  <span className="font-mono">feature</span>). Those refs will
                  be cleared.
                </p>
                <ul className="space-y-0.5 max-h-32 overflow-y-auto rounded border border-zinc-200 dark:border-zinc-800 p-1.5">
                  {kbAffected.map((e) => (
                    <li key={e.ref}>
                      <Link
                        to="/kb"
                        search={{ bucket: e.bucket, slug: e.slug }}
                        onClick={onClose}
                        title={`Open ${e.ref}`}
                        className="flex items-baseline gap-2 rounded px-1 py-0.5 -mx-1 hover:bg-zinc-100 dark:hover:bg-zinc-800 hover:text-blue-600 dark:hover:text-blue-400"
                      >
                        <span className="font-mono truncate">{e.ref}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {hasBlockers && (
              <div className="space-y-1.5 rounded border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/30 p-2">
                <p className="font-medium text-amber-700 dark:text-amber-400">
                  This entry's folder contains other files:
                </p>
                <ul className="font-mono space-y-0.5 max-h-32 overflow-y-auto">
                  {preview.data!.blockingFiles.map((f) => (
                    <li key={f} className="break-all">{f}</li>
                  ))}
                </ul>
                <label className="flex items-center gap-2 pt-1 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={deleteFolder}
                    onChange={(e) => {
                      setDeleteFolder(e.target.checked)
                      setError(null)
                    }}
                    className="h-3.5 w-3.5"
                  />
                  <span>Also delete the folder and all its contents</span>
                </label>
              </div>
            )}
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
            disabled={busy || preview.isLoading || !!preview.error || blockedUnconfirmed}
            onClick={() => {
              setError(null)
              del.mutate()
            }}
            title={blockedUnconfirmed ? 'Confirm "delete folder" first' : 'Delete this entry'}
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
