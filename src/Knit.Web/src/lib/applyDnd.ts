import type { QueryClient } from '@tanstack/react-query'
import { ApiError, api, qk } from '../api/client'
import { suppressDragRefetch } from '../api/sse'

/**
 * Where a drag landed, in terms the server can place: the dragged ids in
 * their final order, the parent they end up under, and at most one neighbour
 * row they sit before / after (neither = append to the end of that group).
 * No ranks — the server computes those (30).
 */
export interface DndPlacement {
  ids: string[]
  parent: string | null
  before?: string
  after?: string
}

export interface DndApplyResult {
  ok: boolean
  /** Ids whose write failed — caller can highlight or refresh. */
  failedIds: string[]
  /** Human-readable error to surface in a toast on failure. */
  message?: string
}

/**
 * Apply one drop through POST /api/items/reorder — one call for the whole
 * dragged set, atomic server-side. SSE invalidations are suppressed for the
 * duration of the write.
 */
export async function applyDndPlacement(
  qc: QueryClient,
  placement: DndPlacement,
): Promise<DndApplyResult> {
  if (placement.ids.length === 0) return { ok: true, failedIds: [] }
  const release = suppressDragRefetch()
  try {
    return await place(qc, placement, false)
  } finally {
    release()
  }
}

async function place(
  qc: QueryClient,
  placement: DndPlacement,
  isRetry: boolean,
): Promise<DndApplyResult> {
  let rows: { id: string; ifMatch: string }[]
  try {
    rows = await Promise.all(
      placement.ids.map(async (id) => {
        const fetched = await api.fetchItemWithEtag(id)
        return { id, ifMatch: fetched.etag ?? '' }
      }),
    )
  } catch (e) {
    return {
      ok: false,
      failedIds: placement.ids,
      message: e instanceof Error ? e.message : 'Failed to fetch etags',
    }
  }

  try {
    const items = await api.reorderItems({
      items: rows,
      parent: placement.parent,
      before: placement.before,
      after: placement.after,
    })
    qc.invalidateQueries({ queryKey: qk.items })
    qc.invalidateQueries({ queryKey: qk.tree })
    for (const it of items) qc.setQueryData(qk.item(it.id), it)
    return { ok: true, failedIds: [] }
  } catch (e) {
    if (e instanceof ApiError) {
      // Something wrote one of these rows between the etag fetch and the POST.
      // Retry once with fresh etags, as the single-item PATCH path does.
      if (e.status === 412 && !isRetry) return place(qc, placement, true)
      return {
        ok: false,
        failedIds: placement.ids,
        message: e.problem?.detail ?? e.message,
      }
    }
    throw e
  }
}
