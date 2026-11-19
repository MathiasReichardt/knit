import { ApiError, apiWrite } from './http'

/**
 * One slate from GET /api/slates. Registry rows come first in table
 * order; `inRegistry: false` rows are slate values found only on items
 * (hand-typed / post-delete leftovers) unioned in so they never vanish
 * from the assign dropdown.
 */
export interface Slate {
  name: string
  status: 'planned' | 'active' | 'archived'
  notes: string | null
  itemCount: number
  inRegistry: boolean
}

export interface SlateCreateRequest {
  name: string
  status?: string | null
  notes?: string | null
}

/** Every field optional; `before`/`after` (one of them) reorder the row. */
export interface SlatePatchRequest {
  rename?: string | null
  status?: string | null
  notes?: string | null
  before?: string | null
  after?: string | null
}

export interface SlatePatchResult {
  slate: Slate
  itemsUpdated: number
}

export interface SlateDeleteResult {
  name: string
  itemsCleared: number
}

async function parse<T>(r: Response): Promise<T> {
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  return (await r.json()) as T
}

export const slatesApi = {
  list: async (): Promise<Slate[]> => parse(await fetch('/api/slates')),
  create: async (body: SlateCreateRequest): Promise<Slate> =>
    parse(await apiWrite('/api/slates', 'POST', { body })),
  patch: async (name: string, body: SlatePatchRequest): Promise<SlatePatchResult> =>
    parse(await apiWrite(`/api/slates/${encodeURIComponent(name)}`, 'PATCH', { body })),
  /** Also clears `slate:` from the items that carry it. */
  remove: async (name: string): Promise<SlateDeleteResult> =>
    parse(await apiWrite(`/api/slates/${encodeURIComponent(name)}`, 'DELETE')),
}

export const slatesQk = {
  list: ['slates'] as const,
}
