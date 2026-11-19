import { describe, it, expect } from 'vitest'
import { milestoneDocLink, milestoneRefFromPath } from './milestoneRef'

describe('milestoneRefFromPath', () => {
  it('extracts <project>/<slug> from a relative milestone path', () => {
    expect(milestoneRefFromPath('../projects/relaunch/milestones/beta.md')).toBe(
      'relaunch/beta',
    )
  })

  it('handles backslash separators', () => {
    expect(milestoneRefFromPath('..\\projects\\relaunch\\milestones\\beta.md')).toBe(
      'relaunch/beta',
    )
  })

  it('returns null for a non-milestone path', () => {
    expect(milestoneRefFromPath('../0abc-some-item/index.md')).toBeNull()
    expect(milestoneRefFromPath('kb/adr/0001.md')).toBeNull()
  })
})

describe('milestoneDocLink', () => {
  it('yields a roadmap-relative link to the milestone sub-doc', () => {
    expect(milestoneDocLink('beta')).toBe('milestones/beta.md')
  })
})
