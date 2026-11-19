import type { ItemType } from '../api/types'

/**
 * Client-side mirror of Knit.Core.Domain.Hierarchy.ParentAllowed.
 * The server is the source of truth — this is only used to disable
 * impossible options early in the new-item dialog and DnD.
 */
export function parentAllowed(child: ItemType, parent: ItemType | null): boolean {
  switch (child) {
    // Project is the top altitude — root only, no sub-projects.
    case 'project':
      return parent === null
    case 'epic':
      return parent === null || parent === 'project'
    case 'feature':
      return parent === null || parent === 'project' || parent === 'epic'
    case 'bug':
      return parent === null || parent === 'project' || parent === 'epic'
    case 'task':
      return (
        parent === null ||
        parent === 'project' ||
        parent === 'epic' ||
        parent === 'feature' ||
        parent === 'bug' ||
        parent === 'task'
      )
    // No loose todos: every todo sits under a project (directly, or through
    // a todo chain that itself roots at a project).
    case 'todo':
      return parent === 'project' || parent === 'todo'
  }
}

const ALL_TYPES: ItemType[] = ['project', 'epic', 'feature', 'bug', 'task', 'todo']

export function allowedParents(child: ItemType): ItemType[] {
  return ALL_TYPES.filter((p) => parentAllowed(child, p))
}

export function allowedAtRoot(child: ItemType): boolean {
  return parentAllowed(child, null)
}

function join(parts: string[]): string {
  return parts.length === 1
    ? parts[0]
    : `${parts.slice(0, -1).join(', ')} or ${parts[parts.length - 1]}`
}

function capitalize(s: string): string {
  return `${s[0].toUpperCase()}${s.slice(1)}`
}

function refusalParts(child: ItemType, parent: ItemType | null): { what: string; rule: string | null } {
  const what = parent
    ? `a ${child} cannot be a child of a ${parent}`
    : `a ${child} cannot sit at the repo root`

  const allowed: string[] = allowedParents(child)
  if (allowedAtRoot(child)) allowed.push('the repo root')

  return { what, rule: allowed.length === 0 ? null : `a ${child} belongs under ${join(allowed)}` }
}

/**
 * Mirror of Knit.Core.Domain.Hierarchy.ExplainRefusal — same wording, so the
 * web UI and the CLI read identically when they refuse the same placement.
 * Derived here once rather than re-phrased per dialog.
 */
export function explainRefusal(child: ItemType, parent: ItemType | null): string {
  const { what, rule } = refusalParts(child, parent)
  return rule ? `${what} — ${rule}` : what
}

/** {@link explainRefusal} as a standalone sentence, for inline error prose. */
export function refusalSentence(child: ItemType, parent: ItemType | null): string {
  return `${capitalize(explainRefusal(child, parent))}.`
}

/**
 * {@link explainRefusal} split for a drag ghost: why it can't land, then how
 * to fix it. The fix offers a retype only when it is short (one or two types)
 * and the item's own children could stay under the new type.
 */
export function dropRefusal(
  child: ItemType,
  parent: ItemType | null,
  childTypes: ItemType[],
): { reason: string; hint?: string } {
  const { what, rule } = refusalParts(child, parent)
  const retypes = ALL_TYPES.filter(
    (t) => t !== child && parentAllowed(t, parent) && childTypes.every((c) => parentAllowed(c, t)),
  )
  const retype = retypes.length > 0 && retypes.length <= 2 ? `retype it to ${join(retypes)}` : null
  const hint = rule && retype ? `${rule} — or ${retype}` : (rule ?? retype)
  return { reason: capitalize(what), hint: hint ? capitalize(hint) : undefined }
}
