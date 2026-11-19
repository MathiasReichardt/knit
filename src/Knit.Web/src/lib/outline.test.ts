import { describe, expect, it } from 'vitest'
import { extractOutline, HeadingSlugger, headingIdAtOffset } from './outline'

describe('extractOutline', () => {
  it('extracts h2 and h3 with depth + ids, ignoring h1 and h4', () => {
    const body = '# Title\n\n## Goals\n\n### Sub goal\n\n#### Too deep\n\n## Approach\n'
    expect(extractOutline(body)).toEqual([
      { depth: 2, text: 'Goals', id: 'goals', line: 3 },
      { depth: 3, text: 'Sub goal', id: 'sub-goal', line: 5 },
      { depth: 2, text: 'Approach', id: 'approach', line: 9 },
    ])
  })

  it('dedupes repeated heading text with numeric suffixes', () => {
    const body = '## Goals\n\n## Goals\n\n## Goals\n'
    expect(extractOutline(body).map((i) => i.id)).toEqual(['goals', 'goals-2', 'goals-3'])
  })

  it('ignores headings inside fenced code blocks', () => {
    const body = '## Real\n\n```\n## Fake in code\n```\n\n## Also real\n'
    expect(extractOutline(body).map((i) => i.text)).toEqual(['Real', 'Also real'])
  })

  it('ignores headings inside mermaid and tilde fences', () => {
    const body = '## A\n\n```mermaid\ngraph TD\n## not a heading\n```\n\n~~~\n## nope\n~~~\n\n## B\n'
    expect(extractOutline(body).map((i) => i.text)).toEqual(['A', 'B'])
  })

  it('skips a leading frontmatter block', () => {
    const body = '---\ntitle: Storage\nslug: storage\n---\n\n# Storage\n\n## Overview\n'
    expect(extractOutline(body)).toEqual([{ depth: 2, text: 'Overview', id: 'overview', line: 8 }])
  })

  it('trims ATX closing hashes', () => {
    expect(extractOutline('## Goals ##\n')).toEqual([{ depth: 2, text: 'Goals', id: 'goals', line: 1 }])
  })
})

describe('headingIdAtOffset', () => {
  // "intro line\n"(11) "## Alpha\n"(9) → Alpha at 11; "body a\n"(7) → Beta at 27.
  const body = 'intro line\n## Alpha\nbody a\n## Beta\nbody b\n'

  it('returns null before the first heading', () => {
    expect(headingIdAtOffset(body, 2)).toBeNull() // inside the intro paragraph
  })

  it('returns the section the offset falls within', () => {
    expect(headingIdAtOffset(body, 22)).toBe('alpha') // inside "body a"
    expect(headingIdAtOffset(body, 37)).toBe('beta') // inside "body b"
  })

  it('matches the dedup suffixes a renderer would emit', () => {
    const dup = 'lead\n## Goals\nx\n## Goals\ny\n'
    expect(headingIdAtOffset(dup, 9)).toBe('goals') // first "## Goals" line
    expect(headingIdAtOffset(dup, 20)).toBe('goals-2') // second "## Goals" line
  })

  it('ignores headings inside fenced code blocks', () => {
    const fenced = '## Real\n```\n## Fake\n```\nafter\n'
    // Offset lands after the fence; "## Fake" is inside it, so the only real
    // section is "Real".
    expect(headingIdAtOffset(fenced, fenced.length - 2)).toBe('real')
  })
})

describe('HeadingSlugger', () => {
  it('returns the base slug first, then suffixed duplicates', () => {
    const s = new HeadingSlugger()
    expect(s.slug('Open questions')).toBe('open-questions')
    expect(s.slug('Open questions')).toBe('open-questions-2')
    expect(s.slug('Status')).toBe('status')
  })
})
