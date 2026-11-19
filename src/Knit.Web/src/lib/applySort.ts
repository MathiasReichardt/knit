import type { ApplySortResult } from '../api/client'

const plural = (n: number, one: string) => `${n} ${one}${n === 1 ? '' : 's'}`

/** The confirm line for "Apply state order to ranks", from the dry run's counts. */
export function applySortSummary(r: Pick<ApplySortResult, 'moved' | 'total' | 'groups'>): string {
  if (r.total === 0) return 'No children to reorder.'
  if (r.moved === 0) return 'Ranks already follow the state order — nothing to rewrite.'
  return `Rewrites rank on ${plural(r.moved, 'item')} in ${plural(r.groups, 'group')}.`
}
