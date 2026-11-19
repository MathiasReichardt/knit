import { describe, it, expect } from 'vitest'
import { GfmSlugger, gfmAnchorBase, withGfmHeadingIds } from './gfmSlug'

// The same cases as StatusPageRendererTests, so the SPA lands where the page's
// tables of contents point.
describe('gfmAnchorBase', () => {
  it.each([
    ['A -- B (v2)', 'a----b-v2'],
    ['Outside any project', 'outside-any-project'],
    ['Ünïcode 한글 v1.2', 'ünïcode-한글-v12'],
    ['snake_case & more!', 'snake_case--more'],
    ['🎯 Goal', '-goal'],
  ])('%s → %s', (heading, anchor) => {
    expect(gfmAnchorBase(heading)).toBe(anchor)
  })
})

describe('GfmSlugger', () => {
  it('suffixes repeats from -1 and skips a suffix already taken', () => {
    const s = new GfmSlugger()
    expect(['Projects', 'Projects', 'Projects-1', 'Projects'].map((h) => s.next(h)))
      .toEqual(['projects', 'projects-1', 'projects-1-1', 'projects-2'])
  })
})

describe('withGfmHeadingIds', () => {
  it('counts every heading level in document order', () => {
    const html = withGfmHeadingIds('<h1>Repo — status</h1><h2>Projects</h2><h3>Projects</h3><h4>x</h4>', new GfmSlugger())
    const ids = Array.from(new DOMParser().parseFromString(html, 'text/html').querySelectorAll('[id]')).map((e) => e.id)
    expect(ids).toEqual(['repo--status', 'projects', 'projects-1', 'x'])
  })
})
