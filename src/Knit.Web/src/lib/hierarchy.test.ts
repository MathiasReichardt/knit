import { describe, expect, it } from 'vitest'
import type { ItemType } from '../api/types'
import { dropRefusal, explainRefusal, parentAllowed, refusalSentence } from './hierarchy'

// Mirror of Knit.Core.Domain.Hierarchy.ParentAllowed — these cases must stay
// in lockstep with the server switch (the server is the source of truth).
describe('parentAllowed', () => {
  it('project is root-only (no sub-projects)', () => {
    expect(parentAllowed('project', null)).toBe(true)
    const others: ItemType[] = ['project', 'epic', 'feature', 'bug', 'task', 'todo']
    for (const p of others) expect(parentAllowed('project', p)).toBe(false)
  })

  it('epic/feature/bug/task accept a project parent and can be roots', () => {
    for (const c of ['epic', 'feature', 'bug', 'task'] as ItemType[]) {
      expect(parentAllowed(c, null)).toBe(true)
      expect(parentAllowed(c, 'project')).toBe(true)
    }
  })

  it('classic altitudes still hold', () => {
    expect(parentAllowed('feature', 'epic')).toBe(true)
    expect(parentAllowed('feature', 'feature')).toBe(false)
    expect(parentAllowed('task', 'feature')).toBe(true)
    expect(parentAllowed('task', 'task')).toBe(true)
    expect(parentAllowed('epic', 'feature')).toBe(false)
  })

  it('todo is never a root — only under project or todo', () => {
    expect(parentAllowed('todo', null)).toBe(false)
    expect(parentAllowed('todo', 'project')).toBe(true)
    expect(parentAllowed('todo', 'todo')).toBe(true)
    expect(parentAllowed('todo', 'task')).toBe(false)
    expect(parentAllowed('todo', 'epic')).toBe(false)
  })
})

// Literal parity with Knit.Core.Domain.Hierarchy.ExplainRefusal — the CLI and
// the web UI must read identically when they refuse the same placement.
describe('explainRefusal', () => {
  it('names the pair and the rule for a bad parent', () => {
    expect(explainRefusal('bug', 'feature')).toBe(
      'a bug cannot be a child of a feature — a bug belongs under project, epic or the repo root',
    )
  })

  it('covers the root case', () => {
    expect(explainRefusal('todo', null)).toBe(
      'a todo cannot sit at the repo root — a todo belongs under project or todo',
    )
  })

  it('never emits a "(none)" placeholder', () => {
    expect(explainRefusal('project', 'epic')).not.toContain('(none)')
    expect(explainRefusal('todo', null)).not.toContain('(none)')
  })

  it('refusalSentence capitalizes and terminates', () => {
    expect(refusalSentence('todo', null)).toBe(
      'A todo cannot sit at the repo root — a todo belongs under project or todo.',
    )
  })
})

describe('dropRefusal', () => {
  it('splits the rule from the fix and offers a short retype', () => {
    expect(dropRefusal('feature', 'feature', [])).toEqual({
      reason: 'A feature cannot be a child of a feature',
      hint: 'A feature belongs under project, epic or the repo root — or retype it to task',
    })
  })

  it("offers no retype the item's own children could not follow", () => {
    // A task can't hold a bug, so a feature with a bug child stays a feature.
    expect(dropRefusal('feature', 'feature', ['bug']).hint).toBe(
      'A feature belongs under project, epic or the repo root',
    )
  })

  it('skips a retype with too many candidates to be a hint', () => {
    expect(dropRefusal('todo', null, []).hint).toBe('A todo belongs under project or todo')
  })
})
