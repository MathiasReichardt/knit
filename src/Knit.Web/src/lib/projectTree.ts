import type { TreeNode } from '../api/types'

// Pure helpers behind the Projects nav tree. Kept DOM-free and co-located
// with a unit test — the work-items composition rule is fiddly enough to
// deserve one.

/** Every item id contained in a forest, inclusive of the roots. */
export function collectIds(nodes: TreeNode[], acc = new Set<string>()): Set<string> {
  for (const n of nodes) {
    acc.add(n.item.id)
    collectIds(n.children, acc)
  }
  return acc
}

/** First node with `id` found anywhere in the forest, else null. */
export function findNode(nodes: TreeNode[], id: string): TreeNode | null {
  for (const n of nodes) {
    if (n.item.id === id) return n
    const hit = findNode(n.children, id)
    if (hit) return hit
  }
  return null
}

/**
 * Partition a project node's direct children into todo subtrees vs work-item
 * (non-todo) subtrees. The hierarchy rules make each partition self-contained:
 * a todo's children are only todos, and no todo ever sits under a work item —
 * so the two never interleave.
 */
export function splitProjectChildren(project: TreeNode): { todos: TreeNode[]; work: TreeNode[] } {
  const todos: TreeNode[] = []
  const work: TreeNode[] = []
  for (const c of project.children) {
    if (c.item.type === 'todo') todos.push(c)
    else work.push(c)
  }
  return { todos, work }
}

/** A milestone detail, reduced to what the composition needs. */
export interface MilestoneBacklinks {
  project: string
  slug: string
  backlinks: { id: string }[]
}

/**
 * Reverse-index milestone backlinks: `itemMilestone` badges any item with the
 * (first) milestone it links; `linkedByProject` groups linked item ids per
 * project so each project's work-items composition sees only its own roadmap.
 * First milestone wins on conflict — a multi-milestone item is a verify
 * warning, not a display concern.
 */
export function buildMilestoneMap(details: MilestoneBacklinks[]): {
  itemMilestone: Map<string, string>
  linkedByProject: Map<string, Set<string>>
} {
  const itemMilestone = new Map<string, string>()
  const linkedByProject = new Map<string, Set<string>>()
  for (const d of details) {
    let set = linkedByProject.get(d.project)
    if (!set) {
      set = new Set<string>()
      linkedByProject.set(d.project, set)
    }
    for (const b of d.backlinks) {
      set.add(b.id)
      if (!itemMilestone.has(b.id)) itemMilestone.set(b.id, d.slug)
    }
  }
  return { itemMilestone, linkedByProject }
}

/**
 * Work-items composition: extra roots = milestone-linked items
 * that live *outside* the project's own subtree and are *not* nested inside
 * another linked item's subtree. A linked item already inside the project (or
 * inside another external root) is badged in place, never re-rooted — so no
 * item is displayed twice.
 */
export function externalWorkRoots(
  fullTree: TreeNode[],
  projectSubtreeIds: Set<string>,
  linkedIds: Set<string>,
): TreeNode[] {
  const nodes = [...linkedIds]
    .filter((id) => !projectSubtreeIds.has(id))
    .map((id) => findNode(fullTree, id))
    .filter((n): n is TreeNode => n !== null)

  const descendants = new Map<string, Set<string>>()
  for (const n of nodes) descendants.set(n.item.id, collectIds(n.children))

  return nodes.filter(
    (n) =>
      !nodes.some(
        (other) => other.item.id !== n.item.id && descendants.get(other.item.id)!.has(n.item.id),
      ),
  )
}
