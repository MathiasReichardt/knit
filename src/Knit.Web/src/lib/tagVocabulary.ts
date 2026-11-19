// Autocomplete vocabulary for the tag inputs. Tags have no
// registry — they are free-form labels that exist only on the entries carrying
// them — so the vocabulary is folded out of the lists the SPA has already
// fetched. Pure, so the ranking is testable without a query client.

/**
 * Tags in use, most-used first then alphabetical, deduped case-insensitively
 * on the *most frequent* spelling. The server normalizes case-insensitively on
 * write, so offering both `Api` and `api` would be offering the same tag twice.
 */
export function tagVocabulary(taggedRows: readonly { tags?: readonly string[] }[]): string[] {
  const counts = new Map<string, { label: string; count: number; spellings: Map<string, number> }>()
  for (const row of taggedRows) {
    for (const raw of row.tags ?? []) {
      const tag = raw.trim()
      if (!tag) continue
      const key = tag.toLowerCase()
      const hit = counts.get(key) ?? { label: tag, count: 0, spellings: new Map() }
      hit.count++
      hit.spellings.set(tag, (hit.spellings.get(tag) ?? 0) + 1)
      counts.set(key, hit)
    }
  }
  return [...counts.values()]
    .map((hit) => ({
      count: hit.count,
      // Ordinal, not locale — a locale collation would make the chosen
      // spelling depend on the machine's language.
      label: [...hit.spellings.entries()].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))[0]![0],
    }))
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label))
    .map((hit) => hit.label)
}

/**
 * Suggestions for a draft. Prefix matches lead (the common case — you type the
 * start of a tag you remember), then substring matches; tags already on the
 * entry are dropped because re-adding one is a no-op. An empty draft offers
 * the whole vocabulary, so opening the input shows what exists at all — that
 * is the point of the feature for someone who has forgotten the spelling.
 */
export function tagSuggestions(
  vocabulary: readonly string[],
  draft: string,
  already: readonly string[],
  limit = 8,
): string[] {
  const taken = new Set(already.map((t) => t.trim().toLowerCase()))
  const q = draft.trim().toLowerCase()
  const free = vocabulary.filter((t) => !taken.has(t.toLowerCase()))
  if (!q) return free.slice(0, limit)
  const prefix = free.filter((t) => t.toLowerCase().startsWith(q))
  const infix = free.filter((t) => !t.toLowerCase().startsWith(q) && t.toLowerCase().includes(q))
  return [...prefix, ...infix].slice(0, limit)
}
