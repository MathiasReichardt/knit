// Compact `3 Jul` for a muted provenance footer, with the full ISO for the
// hover `title`. Returns null for missing / unparseable input so callers can
// drop the item entirely rather than render a placeholder.
export function formatShortDate(iso: string | null | undefined): { short: string; full: string } | null {
  if (!iso) return null
  const d = new Date(iso)
  if (isNaN(d.getTime())) return null
  return {
    short: d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    full: d.toISOString(),
  }
}
