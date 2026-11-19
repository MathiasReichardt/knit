// Subtle, monochrome tag pills for tree/list rows. Deliberately neutral so
// they don't compete with the priority tint or due color; capped to keep rows
// tidy, with a "+N" overflow that lists the rest on hover.
//
// Takes the tags, not the item: items and KB entries both carry them, and one
// pill style for one concept beats a per-tree fork.
const MAX_SHOWN = 3

export function TagBadges({ tags }: { tags: readonly string[] | null | undefined }) {
  if (!tags || tags.length === 0) return null
  const shown = tags.slice(0, MAX_SHOWN)
  const extra = tags.length - shown.length
  return (
    <span className="inline-flex items-center gap-1 shrink-0" title={`Tags: ${tags.join(', ')}`}>
      {shown.map((t) => (
        <span
          key={t}
          className="inline-flex items-center rounded-full border border-zinc-200 dark:border-zinc-700 px-1.5 py-px text-[10px] font-mono text-zinc-500 dark:text-zinc-400"
        >
          {t}
        </span>
      ))}
      {extra > 0 && (
        <span className="text-[10px] font-mono text-zinc-400 dark:text-zinc-500">+{extra}</span>
      )}
    </span>
  )
}
