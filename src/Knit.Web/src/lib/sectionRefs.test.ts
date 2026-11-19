import { describe, it, expect } from 'vitest'
import table from './heading-anchors.fixture.json'
import { extractOutline, HeadingSlugger } from './outline'
import { renderMarkdown, SECTION_LINK_ATTR } from './markdown'
import { navTarget, resolveLink, splitSection } from './resolveLink'

// The same table pins Knit.Core's DocSections (DocSectionsTests): an anchor
// the CLI accepts must be the id the renderer emits.
describe('shared heading-anchor table', () => {
  for (const c of table) {
    it(c.name, () => {
      expect(extractOutline(c.body).map((h) => h.id)).toEqual(c.anchors)
      const tpl = document.createElement('template')
      tpl.innerHTML = renderMarkdown(c.body, new HeadingSlugger(), undefined, { leading: false })
      expect(Array.from(tpl.content.querySelectorAll('h2[id], h3[id]')).map((h) => h.id)).toEqual(c.anchors)
    })
  }
})

describe('section links on headings', () => {
  const render = (sectionLinks: boolean) => {
    const tpl = document.createElement('template')
    tpl.innerHTML = renderMarkdown('## Goals\n\ntext', new HeadingSlugger(), undefined, { sectionLinks })
    return tpl.content.querySelector('h2')!
  }

  it('emits a labelled copy button that survives sanitizing and adds no text', () => {
    const h = render(true)
    const button = h.querySelector(`button[${SECTION_LINK_ATTR}]`)!
    expect(button.getAttribute('aria-label')).toBe('Copy link to section')
    expect(button.getAttribute('title')).toBe('Copy link to section')
    expect(h.textContent).toBe('Goals')
  })

  it('emits none unless asked', () => {
    expect(render(false).querySelector('button')).toBeNull()
  })
})

describe('section refs in code-spans', () => {
  const refs = (md: string) => {
    const tpl = document.createElement('template')
    tpl.innerHTML = renderMarkdown(md)
    return Array.from(tpl.content.querySelectorAll('a.kb-ref')).map((a) => a.getAttribute('data-ref'))
  }

  it('linkifies a ref with a section and a .knit path ref', () => {
    expect(refs('See `design:web#goals` and `.knit/projects/p/milestones/m1.md#scope`.')).toEqual([
      'design:web#goals',
      '.knit/projects/p/milestones/m1.md#scope',
    ])
  })

  it('leaves a path outside .knit or a non-md path alone', () => {
    expect(refs('`.knit/config.json` `docs/x.md`')).toEqual([])
  })
})

describe('resolving a section', () => {
  it('splits the section and drops a pointer fragment', () => {
    expect(splitSection('design:web#goals')).toEqual({ doc: 'design:web', section: 'goals' })
    expect(splitSection('design:web#mermaid-2/node/a')).toEqual({ doc: 'design:web' })
    expect(splitSection('design:web')).toEqual({ doc: 'design:web' })
  })

  it('carries the section into the nav target as the hash', () => {
    expect(navTarget(resolveLink('', 'design:web#goals'))).toEqual({
      to: '/designs',
      search: { slug: 'web' },
      hash: 'goals',
    })
    expect(navTarget(resolveLink('.knit/items', 'item:G7H8K2M4P9Q1R5T3VX#scope'))).toEqual({
      to: '/items',
      search: { id: 'G7H8K2M4P9Q1R5T3VX' },
      hash: 'scope',
    })
    expect(navTarget(resolveLink('.knit/kb/docs', 'other.md#intro'))?.hash).toBe('intro')
    expect(navTarget(resolveLink('', 'design:web#mermaid-1(flowchart)'))?.hash).toBeUndefined()
  })

  it('opens a .knit path ref from any parent: milestones and project docs in their panels', () => {
    expect(navTarget(resolveLink('.knit/items', '.knit/projects/p/milestones/m1.md#scope'))).toEqual({
      to: '/projects',
      search: { mp: 'p', ms: 'm1' },
      hash: 'scope',
    })
    expect(navTarget(resolveLink('.knit/kb/docs', '.knit/projects/p/notes/plan.md'))).toEqual({
      to: '/projects',
      search: { doc: 'p/notes/plan.md' },
    })
    expect(navTarget(resolveLink('', 'project:p#goals'))).toEqual({
      to: '/projects',
      search: { doc: 'p/readme.md' },
      hash: 'goals',
    })
  })

  it('ignores a bare in-page fragment', () => {
    expect(resolveLink('.knit/items', '#goals')).toEqual({ kind: 'ignore' })
  })
})
