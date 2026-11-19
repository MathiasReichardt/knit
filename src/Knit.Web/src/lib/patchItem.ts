import type { QueryClient } from '@tanstack/react-query'
import { ApiError, api, qk, type ItemPatch } from '../api/client'
import { suppressDragRefetch } from '../api/sse'
import type { Item } from '../api/types'
import { useBlockedDependents } from '../stores/blockedDependents'

export interface PatchResult {
  ok: boolean
  item?: Item | undefined
  error?: ApiError | undefined
}

/**
 * Patch an item with a single automatic refetch-and-retry on stale ETag
 * (412). The first attempt uses the cached item etag if present;
 * on 412 we GET the current item, build a fresh If-Match, and re-send.
 * This keeps DnD and action-button flows from flaking when SSE hasn't
 * caught up yet.
 */
export async function patchItemSafe(
  qc: QueryClient,
  id: string,
  patch: ItemPatch,
  opts: { suppressDuringWrite?: boolean } = {},
): Promise<PatchResult> {
  const release = opts.suppressDuringWrite ? suppressDragRefetch() : () => {}
  try {
    const fetched = await api.fetchItemWithEtag(id)
    const etag = fetched.etag ?? ''
    try {
      const res = await api.patchItem(id, patch, etag)
      qc.setQueryData(qk.item(id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      useBlockedDependents.getState().showFor([res.item])
      return { ok: true, item: res.item }
    } catch (e) {
      if (e instanceof ApiError && e.status === 412) {
        // One automatic retry with a freshly fetched etag.
        const refreshed = await api.fetchItemWithEtag(id)
        try {
          const res = await api.patchItem(id, patch, refreshed.etag)
          qc.setQueryData(qk.item(id), res.item)
          qc.invalidateQueries({ queryKey: qk.items })
          qc.invalidateQueries({ queryKey: qk.tree })
          useBlockedDependents.getState().showFor([res.item])
          return { ok: true, item: res.item }
        } catch (e2) {
          if (e2 instanceof ApiError) return { ok: false, error: e2 }
          throw e2
        }
      }
      if (e instanceof ApiError) return { ok: false, error: e }
      throw e
    }
  } finally {
    release()
  }
}
