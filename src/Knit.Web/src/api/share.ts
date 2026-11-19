import { ApiError, type ProblemDetails } from './client'

/** GET /api/share/origin — what this repo's share links are built from. */
export interface ShareOrigin {
  kind: 'forge' | 'none' | 'unknown_host' | 'invalid'
  /** `github`, `gitlab`, `gitea`, `azure` or `custom`; forge only. */
  forge: string | null
  host: string | null
  /** The repo's web address — what a pasted URL must start with; forge only. */
  web: string | null
  repoName: string
  /** Why there is no link, with the command that fixes it. */
  error: string | null
}

/** GET /api/share?path= — the link to one file. */
export interface ShareLink {
  origin: ShareOrigin
  /** The forge URL or `<repo_name>:<path>`; null when `origin.error` stands. */
  url: string | null
  path: string
}

async function json<T>(r: Response): Promise<T> {
  if (!r.ok) {
    let problem: ProblemDetails | null = null
    try { problem = (await r.json()) as ProblemDetails } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  return (await r.json()) as T
}

export const shareQk = { origin: ['share', 'origin'] as const }

export const shareApi = {
  origin: async (): Promise<ShareOrigin> => json(await fetch('/api/share/origin')),
  link: async (path: string): Promise<ShareLink> =>
    json(await fetch(`/api/share?path=${encodeURIComponent(path)}`)),
}
