import { ApiError, apiWrite } from './http'

export type ConventionsScopeName = 'user' | 'repo'

/** One scope from GET /api/conventions — what `knit conventions --json` lists. */
export interface ConventionsScope {
  scope: ConventionsScopeName
  path: string
  exists: boolean
  /** Empty when the file doesn't exist. */
  text: string
  /** False for the user scope on a host bound beyond loopback. */
  writable: boolean
  etag: string
}

export interface ConventionsPayload {
  /** User, then repo. */
  scopes: ConventionsScope[]
}

async function fail(r: Response): Promise<never> {
  let problem = null
  try { problem = await r.json() } catch { /* noop */ }
  throw new ApiError(r.status, problem)
}

export const conventionsApi = {
  async get(): Promise<ConventionsPayload> {
    const r = await fetch('/api/conventions')
    if (!r.ok) await fail(r)
    return (await r.json()) as ConventionsPayload
  },

  /** Blank text removes the file. A 412 means someone else changed it since `etag`. */
  async put(scope: ConventionsScopeName, text: string, etag: string): Promise<ConventionsScope> {
    const r = await apiWrite(`/api/conventions/${scope}`, 'PUT', { body: { text }, ifMatch: etag })
    if (!r.ok) await fail(r)
    return (await r.json()) as ConventionsScope
  },

  async remove(scope: ConventionsScopeName, etag: string): Promise<ConventionsScope> {
    const r = await apiWrite(`/api/conventions/${scope}`, 'DELETE', { ifMatch: etag })
    if (!r.ok) await fail(r)
    return (await r.json()) as ConventionsScope
  },
}

export const conventionsQk = {
  get: () => ['conventions'] as const,
}

export const SCOPE_LABEL: Record<ConventionsScopeName, string> = {
  repo: 'This repo — committed, shared with everyone',
  user: 'Personal — this machine, every repo',
}

export type SettingsTab = 'config' | 'conventions'

/** What a scope's section offers: the text or an empty state; edit and delete only where the host may write. */
export function scopeView(s: ConventionsScope): { show: 'text' | 'empty'; canEdit: boolean; canDelete: boolean } {
  return { show: s.exists ? 'text' : 'empty', canEdit: s.writable, canDelete: s.writable && s.exists }
}

/** The landing page links the repo's conventions only when the repo has some. */
export function hasRepoConventions(p: ConventionsPayload | undefined): boolean {
  return p?.scopes.some((s) => s.scope === 'repo' && s.exists) ?? false
}
