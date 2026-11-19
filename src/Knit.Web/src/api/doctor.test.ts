import { describe, expect, it } from 'vitest'
import { verifyIssueCount, type VerifyResult } from './doctor'

const empty: VerifyResult = {
  invalidItems: [],
  strayFiles: [],
  linkTargets: [],
  malformedItemLinks: [],
  titles: [],
  hierarchy: [],
  origins: [],
  duplicateIds: [],
  identityDrift: [],
  healthOnNonProject: [],
  projectPointers: [],
  malformedMilestones: [],
  multipleMilestones: [],
  doneWithOpenChildren: [],
  staleBlockers: [],
  crossProjectMilestones: [],
  unregisteredSlates: [],
  proseRefs: [],
  proseLinks: [],
  sourcePaths: [],
  svgFiles: [],
}

describe('verifyIssueCount', () => {
  it('is zero for an all-clear result', () => {
    expect(verifyIssueCount(empty)).toBe(0)
  })

  it('sums every class', () => {
    const v: VerifyResult = {
      invalidItems: [{ source: 'items/bad-x.md', reason: 'not a valid item id' }],
      strayFiles: [{ source: 'designs/foo.md', reason: 'missing -design.md suffix' }],
      linkTargets: [{ source: 'a', rel: 'related', targetText: 't', targetPath: 'p', expectedPath: 'e' }],
      malformedItemLinks: [
        { source: 'm', rel: 'blocked by', targetText: 't', targetPath: 'bad-id.md', extractedId: 'bad' },
      ],
      titles: [{ source: 'b', frontmatterTitle: 'x', bodyH1: 'y' }],
      hierarchy: [
        { source: 'c', childId: '1', childType: 'task', parentId: null, parentType: null },
        { source: 'd', childId: '2', childType: 'bug', parentId: '9', parentType: 'epic' },
      ],
      origins: [{ source: 'o', ref: 'design:gone' }],
      duplicateIds: [{ bucket: 'adrs', id: '0002', paths: ['kb/adrs/0002.md', 'kb/adrs/0002-x.md'] }],
      identityDrift: [
        { source: 'kb/sources/short.md', field: 'id', declared: 'short-but-longer', expected: 'short' },
      ],
      healthOnNonProject: [{ source: 'items/z/index.md', itemId: '3', type: 'task', health: 'at-risk' }],
      projectPointers: [
        { source: 'items/w/index.md', itemId: '4', type: 'task', slug: 'ghost', reason: 'project-only field' },
      ],
      malformedMilestones: [{ source: 'projects/p/milestones/notes.txt', reason: 'not a markdown file' }],
      multipleMilestones: [{ source: 'items/v/index.md', itemId: '5', count: 2 }],
      doneWithOpenChildren: [
        {
          source: 'items/u/index.md',
          parentId: '6',
          childId: '7',
          childType: 'task',
          childState: 'in-progress',
        },
      ],
      staleBlockers: [
        { source: 'items/q/index.md', itemId: '10', blockerId: '11', blockerState: 'done', sole: true },
      ],
      crossProjectMilestones: [
        {
          source: 'items/t/index.md',
          itemId: '8',
          itemProject: 'alpha',
          milestoneProject: 'beta',
          milestoneSlug: 'v2',
        },
      ],
      unregisteredSlates: [{ slate: 'typo', paths: ['items/s/index.md', 'items/r/index.md'] }],
      proseRefs: [{ source: 'e', ref: 'adr:nope', reason: 'unresolved', candidates: [] }],
      proseLinks: [{ source: 'f', kind: 'link', target: '../gone.md', expectedPath: 'g', reason: 'missing' }],
      sourcePaths: [{ source: 'kb/sources/spec.md', mount: 'docs', path: 'spec.pdf', reason: 'does not exist' }],
      svgFiles: [{ source: 'h.svg', reason: 'no viewBox' }],
    }
    expect(verifyIssueCount(v)).toBe(21)
  })

  it('excludes informational cross-project milestone links', () => {
    const v: VerifyResult = {
      ...empty,
      crossProjectMilestones: [
        {
          source: 'items/t/index.md',
          itemId: '8',
          itemProject: 'alpha',
          milestoneProject: 'beta',
          milestoneSlug: 'v2',
        },
      ],
    }
    expect(verifyIssueCount(v)).toBe(0)
  })
})
