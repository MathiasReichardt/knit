import { useQuery } from '@tanstack/react-query'
import { api, qk } from '../api/client'
import { handleVocabulary } from './handleVocabulary'

/**
 * Handles in use across all items plus your own, for the assignee input (37).
 * Rides the same unfiltered items query as `useItemTagVocabulary`, so it costs
 * no extra request — and must be unfiltered, or the list would shrink to
 * whatever the toolbar happens to be showing.
 */
export function useHandleVocabulary(): string[] {
  const items = useQuery({ queryKey: qk.itemsFiltered({}), queryFn: () => api.items({}) })
  const identity = useQuery({ queryKey: qk.identity, queryFn: api.identity })
  return handleVocabulary(items.data ?? [], identity.data?.handle)
}
