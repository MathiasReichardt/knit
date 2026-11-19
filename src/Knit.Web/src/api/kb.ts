import { ApiError, apiWrite, getJson, postJson } from './http'
import { fetchPaged, type Paged } from './paging'

export type KbBucket = 'adr' | 'doc' | 'feature' | 'log' | 'idea' | 'source'

// Buckets whose entries may grow into folder form (`slug/readme.md`) and
// host sub-docs. Mirrors the server's `BucketFolders.AllowsGrowToFolder`.
const GROW_TO_FOLDER: ReadonlySet<KbBucket> = new Set(['feature', 'doc', 'source'])

export const allowsGrowToFolder = (b: KbBucket | string | null | undefined): boolean =>
  !!b && GROW_TO_FOLDER.has(b as KbBucket)

export interface KbFrontmatter {
  id: string | null
  title: string | null
  date: string | null
  status: string | null
  supersedes: string[]
  supersededBy: string | null
  feature: string[]
  origin: string[]
  tags: string[]
  summary: string | null
  sourceKind: string | null
  url: string | null
  path: string | null
  // Config mount `path` is relative to; null means the repo root.
  // Declared with `knit mount`, set on an entry with `knit kb set`.
  mount: string | null
}

export interface KbSummary {
  bucket: KbBucket
  slug: string
  ref: string
  title: string
  summary: string | null
  filePath: string
  modified: string
}

export interface KbEntry {
  bucket: KbBucket
  slug: string
  ref: string
  title: string
  filePath: string
  frontmatter: KbFrontmatter
  body: string
}

export interface KbTreeNode {
  name: string
  relPath: string
  isFolder: boolean
  bucket: KbBucket | null
  ref: string | null
  title: string | null
  filePath: string | null
  tags: string[]
  children: KbTreeNode[]
}

export interface KbFolder {
  relPath: string
  folderPath: string
  indexExists: boolean
  indexBody: string | null
}

export interface KbCreateRequest {
  bucket: KbBucket
  title?: string | null
  slug?: string | null
  summary?: string | null
  feature?: string | null
  date?: string | null
  sourceKind?: string | null
  url?: string | null
  path?: string | null
  mount?: string | null
}

export interface KbPatchRequest {
  frontmatter?: KbFrontmatter
  body?: string
}

export interface KbRenameResult {
  oldRef: string
  newRef: string
  oldPath: string
  newPath: string
  itemsRewritten: string[]
  kbEntriesAffected: string[]
  filesRelinked: string[]
}

export interface KbDeleteResult {
  ref: string
  deletedPath: string | null
  deletedFolder: boolean
  dryRun: boolean
  isFolderForm: boolean
  itemsAffected: string[]
  blockingFiles: string[]
  kbEntriesAffected: string[]
}

// Fetch the entry plus its concurrency etag. The etag rides into the
// next PATCH's If-Match so a save landing after another writer touched the
// entry gets a 412 instead of silently clobbering.
async function fetchEntryWithEtag(
  bucket: KbBucket,
  slug: string,
): Promise<{ entry: KbEntry; etag: string }> {
  const r = await fetch(entryUrl(bucket, slug))
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  const entry = (await r.json()) as KbEntry
  return { entry, etag: r.headers.get('ETag') ?? '' }
}

async function patchEntry(
  bucket: KbBucket,
  slug: string,
  body: KbPatchRequest,
  ifMatch: string,
): Promise<{ entry: KbEntry; etag: string }> {
  const r = await apiWrite(entryUrl(bucket, slug), 'PATCH', { body, ifMatch })
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem, r.headers.get('ETag'))
  }
  const entry = (await r.json()) as KbEntry
  return { entry, etag: r.headers.get('ETag') ?? '' }
}

// Delete tolerates 409: a folder-form entry with siblings comes back as a
// KbDeleteResult carrying the blocker list (not a problem), so the caller can
// offer "delete folder anyway?" without treating it as a hard error.
async function kbDelete(
  ref: string,
  opts: { deleteFolder?: boolean; dryRun?: boolean } = {},
): Promise<KbDeleteResult> {
  const r = await apiWrite('/api/kb/delete', 'POST', {
    body: { ref, deleteFolder: opts.deleteFolder ?? false, dryRun: opts.dryRun ?? false },
  })
  if (r.status === 409) return (await r.json()) as KbDeleteResult
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  return (await r.json()) as KbDeleteResult
}

function entryUrl(bucket: KbBucket, slug: string): string {
  // Slug may contain '/' (nested entries); encode each segment so the
  // server's catch-all route receives the original characters.
  const slugPath = slug.split('/').map(encodeURIComponent).join('/')
  return `/api/kb/entry/${encodeURIComponent(bucket)}/${slugPath}`
}

// Landing row: most-recently-edited KB entries across all buckets.
// One capped fetch; `hasMore` flags entries beyond the cap.
async function recent(limit: number): Promise<Paged<KbSummary>> {
  const p = new URLSearchParams({ recent: '1', limit: String(limit) })
  return fetchPaged<KbSummary>(`/api/kb/list?${p.toString()}`)
}

export const kbApi = {
  tree: () => getJson<KbTreeNode[]>('/api/kb/tree'),
  list: (bucket?: KbBucket) =>
    getJson<KbSummary[]>(bucket ? `/api/kb/list?bucket=${encodeURIComponent(bucket)}` : '/api/kb/list'),
  recent,
  folder: (subpath: string) =>
    getJson<KbFolder>(`/api/kb/folder/${subpath.split('/').filter(Boolean).map(encodeURIComponent).join('/')}`),
  entry: (bucket: KbBucket, slug: string) => getJson<KbEntry>(entryUrl(bucket, slug)),
  fetchWithEtag: fetchEntryWithEtag,
  create: (body: KbCreateRequest) => postJson<KbEntry>('/api/kb/entry', body),
  patch: patchEntry,
  rename: (ref: string, newSlug: string) =>
    postJson<KbRenameResult>('/api/kb/rename', { ref, newSlug }),
  supersede: (newRef: string, oldRef: string) =>
    postJson<{ newRef: string; oldRef: string }>('/api/kb/supersede', { newRef, oldRef }),
  split: (ref: string) =>
    postJson<{
      oldPath: string
      newPath: string
      ref: string
      itemsRewritten: string[]
      filesRelinked: string[]
    }>('/api/kb/split', { ref }),
  delete: kbDelete,
  rebuildIndex: () =>
    postJson<{ filesWritten: string[] }>('/api/kb/index/rebuild', {}),
}

export const kbQk = {
  tree: ['kb', 'tree'] as const,
  list: (bucket?: KbBucket) => ['kb', 'list', bucket ?? 'all'] as const,
  entry: (bucket: KbBucket, slug: string) => ['kb', 'entry', bucket, slug] as const,
  folder: (subpath: string) => ['kb', 'folder', subpath] as const,
  // Keyed under 'kb' so the SSE kb invalidation (sse.ts) refreshes it.
  recent: ['kb', 'recent'] as const,
}
