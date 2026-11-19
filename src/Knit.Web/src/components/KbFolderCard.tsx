import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Folder, RefreshCw } from 'lucide-react'
import { useNavigate } from '@tanstack/react-router'
import { kbApi, kbQk } from '../api/kb'
import { MarkdownRender } from './MarkdownRender'
import { CopyPathIconButton } from './CopyButton'
import { ReadOnlyBadge } from './ReadOnlyBadge'
import { useToasts } from '../stores/toasts'
import { resolveLink } from '../lib/resolveLink'
import { followLink } from '../lib/linkCheck'

interface Props {
  subpath: string
}

export function KbFolderCard({ subpath }: Props) {
  const qc = useQueryClient()
  const push = useToasts((s) => s.push)
  const navigate = useNavigate()

  const linkParent = subpath ? `.knit/kb/${subpath}` : '.knit/kb'
  const onInternalLink = (href: string) => {
    const t = resolveLink(linkParent, href)
    void followLink(t, href, {
      navigate: (to) => navigate(to as never),
      toast: push,
    })
  }

  const folder = useQuery({
    queryKey: kbQk.folder(subpath),
    queryFn: () => kbApi.folder(subpath),
  })

  const rebuild = useMutation({
    mutationFn: () => kbApi.rebuildIndex(),
    onSuccess: (r) => {
      push('success', `Regenerated ${r.filesWritten.length} index file(s)`)
      void qc.invalidateQueries({ queryKey: ['kb'] })
    },
    onError: (e) => push('error', `Failed: ${String(e)}`),
  })

  if (folder.isLoading) return <div className="p-4 text-sm text-zinc-500">Loading…</div>
  if (folder.error || !folder.data)
    return <div className="p-4 text-sm text-red-600">Failed to load folder.</div>

  const f = folder.data
  const shownPath = `.knit/kb/${f.relPath || ''}`.replace(/\/$/, '')
  return (
    <div className="flex flex-col h-full min-h-0 overflow-y-auto">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800 flex items-start gap-2">
        <Folder className="h-5 w-5 mt-0.5 text-amber-600 dark:text-amber-400 shrink-0" aria-hidden />
        <div className="flex-1 min-w-0">
          <div className="text-xs font-mono text-zinc-500 flex items-center gap-1">
            {shownPath}
            <CopyPathIconButton value={f.folderPath} title="Copy folder path" />
            {f.indexExists && (
              <ReadOnlyBadge reason="index.md is generated from the folder's entries; regenerate it instead of editing" />
            )}
          </div>
          <h1 className="text-lg font-semibold mt-0.5 text-zinc-900 dark:text-zinc-100">
            {f.relPath || 'Knowledge Base'}
          </h1>
        </div>
        <div className="flex items-center gap-1 shrink-0">
          <button
            type="button"
            onClick={() => rebuild.mutate()}
            disabled={rebuild.isPending}
            title="Regenerate KB index files"
            aria-label="Regenerate KB index files"
            className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300 disabled:opacity-50"
          >
            <RefreshCw className={'h-4 w-4 ' + (rebuild.isPending ? 'animate-spin' : '')} />
          </button>
        </div>
      </div>
      <div className="px-4 py-4">
        {f.indexExists && f.indexBody ? (
          <MarkdownRender
            body={f.indexBody}
            onInternalLink={onInternalLink}
            linkParent={linkParent}
            docRef={`${shownPath}/index.md`}
          />
        ) : (
          <div className="rounded border border-dashed border-zinc-300 dark:border-zinc-700 p-6 text-center">
            <div className="text-sm text-zinc-600 dark:text-zinc-400">
              No <code className="font-mono text-xs">index.md</code> in this folder yet.
            </div>
            <button
              type="button"
              onClick={() => rebuild.mutate()}
              disabled={rebuild.isPending}
              className="mt-3 inline-flex items-center gap-1 px-3 py-1.5 rounded bg-blue-600 text-white text-sm disabled:opacity-50"
            >
              <RefreshCw className={'h-3.5 w-3.5 ' + (rebuild.isPending ? 'animate-spin' : '')} />
              Generate index
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
