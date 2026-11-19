import { ApiError } from './client'

export interface FileView {
  path: string
  sizeBytes: number
  mime: string
  truncated: boolean
  content: string
  filePath: string
  // Set when the file was read through a config mount; `path` is then relative
  // to the mount rather than to the repo root.
  mount: string | null
}

export const fileApi = {
  async view(path: string, mount?: string | null): Promise<FileView> {
    const q = new URLSearchParams({ path })
    if (mount) q.set('mount', mount)
    const r = await fetch(`/api/file?${q.toString()}`)
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as FileView
  },
}

export const fileQk = {
  view: (path: string, mount?: string | null) => ['file', 'view', mount ?? '', path] as const,
}
