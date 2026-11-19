import { describe, expect, it } from 'vitest'
import type { ItemSummary, ItemType, TreeNode } from '../api/types'
import {
  buildMilestoneMap,
  collectIds,
  externalWorkRoots,
  findNode,
  splitProjectChildren,
} from './projectTree'

function node(id: string, type: ItemType, children: TreeNode[] = []): TreeNode {
  const item = { id, type, title: id } as unknown as ItemSummary
  return { item, children, match: true }
}

describe('collectIds / findNode', () => {
  const forest = [node('a', 'epic', [node('b', 'task'), node('c', 'task', [node('d', 'task')])])]

  it('collects every id inclusive of roots', () => {
    expect([...collectIds(forest)].sort()).toEqual(['a', 'b', 'c', 'd'])
  })

  it('finds a nested node and misses absent ones', () => {
    expect(findNode(forest, 'd')?.item.id).toBe('d')
    expect(findNode(forest, 'z')).toBeNull()
  })
})

describe('splitProjectChildren', () => {
  it('partitions direct children into todos and work items', () => {
    const project = node('p', 'project', [
      node('e', 'epic'),
      node('t1', 'todo', [node('t2', 'todo')]),
      node('bug', 'bug'),
    ])
    const { todos, work } = splitProjectChildren(project)
    expect(todos.map((n) => n.item.id)).toEqual(['t1'])
    expect(work.map((n) => n.item.id)).toEqual(['e', 'bug'])
  })
})

describe('buildMilestoneMap', () => {
  it('maps items to their first milestone and groups linked ids by project', () => {
    const { itemMilestone, linkedByProject } = buildMilestoneMap([
      { project: 'relaunch', slug: 'q1', backlinks: [{ id: 'x' }, { id: 'y' }] },
      // y links a second milestone — first (q1) wins for the badge.
      { project: 'relaunch', slug: 'q2', backlinks: [{ id: 'y' }] },
      { project: 'other', slug: 'm', backlinks: [{ id: 'z' }] },
    ])
    expect(itemMilestone.get('x')).toBe('q1')
    expect(itemMilestone.get('y')).toBe('q1')
    expect(itemMilestone.get('z')).toBe('m')
    expect([...(linkedByProject.get('relaunch') ?? [])].sort()).toEqual(['x', 'y'])
    expect([...(linkedByProject.get('other') ?? [])]).toEqual(['z'])
  })
})

describe('externalWorkRoots', () => {
  // full tree: project P {epic E}, and a separate epic X {feature F}.
  const fullTree = [
    node('P', 'project', [node('E', 'epic')]),
    node('X', 'epic', [node('F', 'feature')]),
  ]
  const projectSubtreeIds = collectIds([fullTree[0]]) // P, E

  it('surfaces a cross-project linked item as an extra root', () => {
    const roots = externalWorkRoots(fullTree, projectSubtreeIds, new Set(['X']))
    expect(roots.map((n) => n.item.id)).toEqual(['X'])
  })

  it('excludes linked items already inside the project subtree', () => {
    const roots = externalWorkRoots(fullTree, projectSubtreeIds, new Set(['E', 'X']))
    expect(roots.map((n) => n.item.id)).toEqual(['X'])
  })

  it('does not re-root a linked item nested under another linked root', () => {
    // both X and its child F link a milestone — only X roots; F is badged in place.
    const roots = externalWorkRoots(fullTree, projectSubtreeIds, new Set(['X', 'F']))
    expect(roots.map((n) => n.item.id)).toEqual(['X'])
  })
})
