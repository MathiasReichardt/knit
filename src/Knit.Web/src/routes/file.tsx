import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { ArrowLeft, FileWarning } from 'lucide-react'
import { fileApi, fileQk } from '../api/file'
import { ApiError } from '../api/client'
import { CodeView } from '../components/CodeView'
import { CopyPathIconButton } from '../components/CopyButton'
import { ReadOnlyBadge } from '../components/ReadOnlyBadge'

const fileSearchSchema = z.object({
  path: z.string().min(1),
  // Name of a config mount the path is relative to; absent means the repo root.
  mount: z.string().optional(),
})

export const Route = createFileRoute('/file')({
  validateSearch: fileSearchSchema,
  component: FileScreen,
})

function FileScreen() {
  const { path, mount } = Route.useSearch()
  const navigate = useNavigate()

  const view = useQuery({
    queryKey: fileQk.view(path, mount),
    queryFn: () => fileApi.view(path, mount),
    retry: false,
  })

  const sizeLabel = view.data ? formatSize(view.data.sizeBytes) : ''

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 py-3 border-b border-zinc-200 dark:border-zinc-800 flex items-center gap-2">
        <button
          type="button"
          onClick={() => navigate({ to: '..' as never })}
          title="Back"
          aria-label="Back"
          className="p-1.5 rounded text-zinc-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 dark:text-zinc-300"
        >
          <ArrowLeft className="h-4 w-4" />
        </button>
        <div className="flex-1 min-w-0">
          <div className="text-xs font-mono text-zinc-500 flex items-center gap-1">
            {mount && (
              <span
                className="shrink-0 rounded bg-zinc-200 px-1 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300"
                title="Read through a config mount"
              >
                {mount}
              </span>
            )}
            {path}
            {view.data && <CopyPathIconButton value={view.data.filePath} title="Copy file path" />}
            {view.data && <ReadOnlyBadge reason="The file viewer only shows files; edit them in your editor" />}
          </div>
          {view.data && (
            <div className="text-xs text-zinc-500 mt-0.5">
              {view.data.mime} · {sizeLabel}
              {view.data.truncated && (
                <span className="ml-2 text-amber-600 dark:text-amber-400">truncated</span>
              )}
            </div>
          )}
        </div>
      </div>
      <div className="flex-1 min-h-0 overflow-auto p-4">
        {view.isLoading && <div className="text-sm text-zinc-500">Loading…</div>}
        {view.error && <ErrorView error={view.error} />}
        {view.data && <Body data={view.data} />}
      </div>
    </div>
  )
}

function Body({ data }: { data: { mime: string; truncated: boolean; sizeBytes: number; content: string } }) {
  if (data.truncated) {
    return (
      <Placeholder
        icon={<FileWarning className="h-8 w-8 text-amber-500" />}
        title="File too large to preview"
        detail={`${formatSize(data.sizeBytes)} exceeds the in-app viewer limit. Open it in your editor instead.`}
      />
    )
  }
  if (data.mime === 'application/octet-stream') {
    return (
      <Placeholder
        icon={<FileWarning className="h-8 w-8 text-zinc-400" />}
        title="Binary file"
        detail="Not previewable in the code viewer."
      />
    )
  }
  return <CodeView value={data.content} mime={data.mime} />
}

function ErrorView({ error }: { error: unknown }) {
  const apiError = error instanceof ApiError ? error : null
  const status = apiError?.status
  const detail = apiError?.problem?.detail ?? (error instanceof Error ? error.message : String(error))
  return (
    <Placeholder
      icon={<FileWarning className="h-8 w-8 text-red-500" />}
      title={status ? `HTTP ${status}` : 'Failed to load file'}
      detail={detail}
    />
  )
}

function Placeholder({ icon, title, detail }: { icon: React.ReactNode; title: string; detail: string }) {
  return (
    <div className="rounded border border-dashed border-zinc-300 dark:border-zinc-700 p-8 flex flex-col items-center gap-2 text-center">
      {icon}
      <div className="text-sm font-medium text-zinc-700 dark:text-zinc-200">{title}</div>
      <div className="text-xs text-zinc-500 max-w-md">{detail}</div>
    </div>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MiB`
}
