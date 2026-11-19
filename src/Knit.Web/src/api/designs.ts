import { ApiError, apiWrite, getJson, postJson } from './http'
import { fetchPaged, type Paged } from './paging'

export interface DesignSummary {
  slug: string
  title: string
  filePath: string
  completed: boolean
  modified: string
}

export interface DesignDoc {
  slug: string
  title: string
  filePath: string
  completed: boolean
  body: string
  modified: string
}

export interface DesignCreateRequest {
  slug: string
  title?: string | null
}

export interface DesignArchiveResult {
  slug: string
  oldPath: string
  newPath: string
  filesRelinked: string[]
}

// Fetch the doc plus its concurrency etag. The etag captured here
// rides into the next PATCH's If-Match so a save that lands after another
// writer touched the doc gets a 412 instead of silently clobbering.
async function fetchWithEtag(slug: string): Promise<{ doc: DesignDoc; etag: string }> {
  const r = await fetch(`/api/designs/${encodeURIComponent(slug)}`)
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  const doc = (await r.json()) as DesignDoc
  return { doc, etag: r.headers.get('ETag') ?? '' }
}

async function patch(
  slug: string,
  body: string,
  ifMatch: string,
): Promise<{ doc: DesignDoc; etag: string }> {
  const r = await apiWrite(`/api/designs/${encodeURIComponent(slug)}`, 'PATCH', { body: { body }, ifMatch })
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem, r.headers.get('ETag'))
  }
  const doc = (await r.json()) as DesignDoc
  return { doc, etag: r.headers.get('ETag') ?? '' }
}

// Landing row: open designs, mtime-desc. One capped fetch; `hasMore`
// reports whether more exist beyond the cap (drives the trailing View-all card).
async function landing(limit: number): Promise<Paged<DesignSummary>> {
  const p = new URLSearchParams({ open: '1', sort: 'mtime:desc', limit: String(limit) })
  return fetchPaged<DesignSummary>(`/api/designs?${p.toString()}`)
}

export const designsApi = {
  list: (archived = false) =>
    getJson<DesignSummary[]>(archived ? '/api/designs?archived=true' : '/api/designs'),
  landing,
  get: (slug: string) => getJson<DesignDoc>(`/api/designs/${encodeURIComponent(slug)}`),
  fetchWithEtag,
  create: (body: DesignCreateRequest) => postJson<DesignDoc>('/api/designs', body),
  patch,
  archive: (slug: string, cycle?: string) =>
    postJson<DesignArchiveResult>(`/api/designs/${encodeURIComponent(slug)}/archive`, {
      cycle: cycle ?? null,
    }),
}

export const designsQk = {
  list: (archived: boolean) => ['designs', 'list', archived ? 'completed' : 'active'] as const,
  entry: (slug: string) => ['designs', 'entry', slug] as const,
  // Keyed under 'designs' so the SSE design invalidation (sse.ts) refreshes it.
  landing: ['designs', 'landing'] as const,
}
