import { useQuery } from '@tanstack/react-query'
import { api, qk } from '../api/client'
import { kbApi, kbQk, type KbTreeNode } from '../api/kb'
import { tagVocabulary } from './tagVocabulary'

/** Every tagged node in a KB tree, flattened. Folders carry no tags. */
export function kbTaggedNodes(nodes: readonly KbTreeNode[]): { tags: string[] }[] {
  const out: { tags: string[] }[] = []
  const walk = (list: readonly KbTreeNode[]) => {
    for (const n of list) {
      if (n.tags.length > 0) out.push({ tags: n.tags })
      walk(n.children)
    }
  }
  walk(nodes)
  return out
}

/**
 * Tags in use across all items, for the tag input's autocomplete.
 * Rides the same unfiltered items query the rank control uses, so the
 * vocabulary is normally already cached and the field costs no extra request —
 * and it must be unfiltered, or the suggestions would shrink to whatever the
 * toolbar happens to be showing.
 */
export function useItemTagVocabulary(): string[] {
  const q = useQuery({ queryKey: qk.itemsFiltered({}), queryFn: () => api.items({}) })
  return tagVocabulary(q.data ?? [])
}

/** The KB counterpart, folded out of the tree the KB view already loads. */
export function useKbTagVocabulary(): string[] {
  const q = useQuery({ queryKey: kbQk.tree, queryFn: kbApi.tree })
  return tagVocabulary(kbTaggedNodes(q.data ?? []))
}
