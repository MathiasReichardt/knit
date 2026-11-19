import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Trash2 } from 'lucide-react'
import { projectsApi, type ProjectDocDeleteResult } from '../api/projects'
import { docFolderName, isFolderFormDoc } from '../lib/projectDocs'
import { useToasts } from '../stores/toasts'

interface Props {
  project: string
  path: string
  onClose: () => void
  onDeleted: () => void
}

/**
 * Confirm-before-remove for a free-form project doc. Free-form docs are inert
 * to the link machinery (referenced only by untracked prose links), so there
 * are no back-links to re-point — but inbound prose links won't be repaired,
 * which we surface. A folder-form doc with siblings is refused (409) until the
 * user opts into deleting the whole folder.
 */
export function ProjectDocDeleteDialog({ project, path, onClose, onDeleted }: Props) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [error, setError] = useState<string | null>(null)
  const [blocking, setBlocking] = useState<string[] | null>(null)
  const folderName = docFolderName(path)
  const isFolder = isFolderFormDoc(path)

  const del = useMutation({
    mutationFn: (deleteFolder: boolean) => projectsApi.removeDoc(project, path, { deleteFolder }),
    onSuccess: (result: ProjectDocDeleteResult) => {
      if (!result.deleted) {
        // Folder-form doc with siblings — surface them and let the user confirm
        // the whole-folder delete.
        setBlocking(result.blockingFiles)
        return
      }
      qc.invalidateQueries({ queryKey: ['projects'] })
      toast('success', `Deleted ${project}/${path}`)
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
          Delete {isFolder ? `folder ${folderName}` : `document ${path}`}
        </h3>

        <div className="space-y-2 text-xs text-zinc-600 dark:text-zinc-300">
          <p>
            <span className="font-mono">.knit/projects/{project}/{path}</span>
            {isFolder ? ' and its containing folder' : ''} will be deleted.
          </p>
          <p className="text-zinc-500">
            Inbound prose links (from the roadmap or other docs) are <em>not</em> repaired —
            Knit doesn't track them.
          </p>

          {blocking && blocking.length > 0 && (
            <div className="space-y-1.5 rounded border border-amber-300 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/30 p-2">
              <p className="font-medium text-amber-700 dark:text-amber-400">
                This folder still holds {blocking.length} file(s):
              </p>
              <ul className="space-y-0.5 max-h-32 overflow-y-auto font-mono">
                {blocking.map((f) => (
                  <li key={f} className="truncate">{f}</li>
                ))}
              </ul>
              <p>Delete the whole folder and everything in it?</p>
            </div>
          )}

          <p className="text-zinc-500">Recoverable via git until committed.</p>
        </div>

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
            disabled={busy}
            onClick={() => {
              setError(null)
              // Once blocked, the next confirm forces the whole-folder delete.
              del.mutate(blocking !== null)
            }}
            title={blocking ? 'Delete the folder and all its contents' : 'Delete this document'}
            className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded bg-red-600 hover:bg-red-700 text-white disabled:opacity-50"
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden />
            {busy ? 'Deleting…' : blocking ? 'Delete folder + contents' : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}
