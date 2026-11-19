// Autocomplete vocabulary for the assignee input. Like tags, handles
// have no registry in M1, so the set is folded out of the items the SPA has
// already fetched. Pure, so the ranking is testable without a query client.

/**
 * Assignees in use, most-assigned first then ordinal. **Not** case-folded,
 * unlike tags: the server matches assignees exactly, so `Bob` and `bob` are two
 * handles and folding would offer a spelling that filters to nothing. Your own
 * handle is always offered — a newcomer is absent from every item yet, and
 * without it the list would only ever steer them onto someone else's name.
 */
export function handleVocabulary(
  rows: readonly { assignee?: string | null }[],
  me?: string | null,
): string[] {
  const counts = new Map<string, number>()
  for (const row of rows) {
    const h = row.assignee?.trim()
    if (h) counts.set(h, (counts.get(h) ?? 0) + 1)
  }
  const self = me?.trim()
  if (self && !counts.has(self)) counts.set(self, 0)
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))
    .map(([h]) => h)
}

/**
 * Suggestions for a draft: prefix matches, then substring matches, both
 * case-insensitive so typing `bo` finds `Bob`. The draft itself is dropped —
 * offering exactly what is already typed is noise — but a case variant of it
 * stays, because that near-duplicate is what the list is for.
 */
export function handleSuggestions(vocabulary: readonly string[], draft: string, limit = 8): string[] {
  const typed = draft.trim()
  const q = typed.toLowerCase()
  const free = vocabulary.filter((h) => h !== typed)
  if (!q) return free.slice(0, limit)
  const prefix = free.filter((h) => h.toLowerCase().startsWith(q))
  const infix = free.filter((h) => !h.toLowerCase().startsWith(q) && h.toLowerCase().includes(q))
  return [...prefix, ...infix].slice(0, limit)
}
