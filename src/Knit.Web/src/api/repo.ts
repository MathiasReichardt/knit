import { type UnsharedWorktree } from './client'
import { ApiError, apiWrite, type ProblemDetails } from './http'

/** GET /api/root/git — the HEAD of the checkout holding the knit repo. */
export interface GitHead {
  /** Null when the knit repo is not in a git repo, or HEAD can't be read. */
  checkout: string | null
  branch: string | null
  /** Abbreviated commit when HEAD is detached. */
  shortSha: string | null
  rebasing: boolean
  /** The linked worktree the host runs in, else null. */
  worktree: string | null
}

/** GET/PUT /api/root/name. */
export interface RepoName {
  name: string
  /** The folder name the repo falls back to when `repo_name` is unset. */
  default: string
  /** `config.json`, `config.local.json`, or `default`. */
  source: string
}

async function json<T>(r: Response): Promise<T> {
  if (!r.ok) {
    let problem: ProblemDetails | null = null
    try { problem = (await r.json()) as ProblemDetails } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  return (await r.json()) as T
}

export const repoApi = {
  git: async (): Promise<GitHead> => json(await fetch('/api/root/git')),
  name: async (): Promise<RepoName> => json(await fetch('/api/root/name')),
  /** A blank name unsets `repo_name`, back to the folder name. */
  setName: async (name: string): Promise<RepoName> =>
    json(await apiWrite('/api/root/name', 'PUT', { body: { name: name.trim() || null } })),
}

export const repoQk = {
  git: ['git'] as const,
  name: ['repoName'] as const,
}

/** The ref text after the branch glyph: the branch, or `@sha` when detached. */
export function headLabel(head: GitHead): string | null {
  if (head.branch) return head.branch
  if (head.shortSha) return `@${head.shortSha}`
  return null
}

export function headTitle(head: GitHead): string | null {
  const label = headLabel(head)
  if (!label || !head.checkout) return null
  if (head.branch) return `Branch ${head.branch} — ${head.checkout}`
  return `${head.rebasing ? 'Rebase in progress' : 'Detached HEAD'} at ${head.shortSha} — ${head.checkout}`
}

/**
 * The worktree tooltip. When the knit repo lives in another checkout (the
 * root redirect), the branch next to the icon is that checkout's, not the
 * worktree's — say so, or the pair reads as "this branch is a worktree".
 */
export function worktreeTitle(head: GitHead): string | null {
  if (!head.worktree) return null
  if (head.checkout && !samePath(head.checkout, head.worktree))
    return `In worktree mode — knit data from ${head.checkout}`
  return 'In worktree mode'
}

/**
 * What the unshared-worktree marker says; its popover lists the commands
 * beside it, copyable — the CLI's stderr note never reaches an SPA-only user.
 */
export function unsharedSummary(u: UnsharedWorktree): string {
  const other = u.target
    ? `${u.bare ? "the default branch's worktree" : 'the main checkout'} (${u.target}) has one too`
    : 'other worktrees of this bare repo have one too'
  return `This worktree uses its own .knit/, and ${other}.`
}

/** The way out, as copyable commands: share, or keep this one on purpose. */
export function unsharedCommands(u: UnsharedWorktree): { label: string; command: string }[] {
  return [
    { label: u.target ? 'Share it' : 'Share one', command: u.command },
    { label: 'Keep this one', command: 'knit root --local' },
  ]
}

function samePath(a: string, b: string): boolean {
  const norm = (p: string) => p.replace(/[\\/]+$/, '').toLowerCase()
  return norm(a) === norm(b)
}
