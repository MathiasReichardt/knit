import { ApiError } from './client'

/** One page of a header-paged list feed. */
export interface Paged<T> {
  rows: T[]
  total: number
  hasMore: boolean
}

/**
 * Fetch a bare-array list endpoint that carries paging metadata in the
 * `X-Total-Count` / `X-Has-More` response headers (the landing rows).
 * The body stays a plain `T[]`; this reads the headers alongside it.
 */
export async function fetchPaged<T>(url: string): Promise<Paged<T>> {
  const r = await fetch(url)
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  const rows = (await r.json()) as T[]
  const total = Number(r.headers.get('X-Total-Count') ?? rows.length)
  const hasMore = (r.headers.get('X-Has-More') ?? 'false').toLowerCase() === 'true'
  return { rows, total: Number.isFinite(total) ? total : rows.length, hasMore }
}
