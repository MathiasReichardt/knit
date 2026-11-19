import { describe, it, expect } from 'vitest'
import {
  resolveLink,
  joinPosix,
  parentFromAbsoluteUnderRepo,
  KB_REF_RE,
  bodyRef,
} from './resolveLink'

describe('bodyRef', () => {
  it('cites an item with its quoted title and a doc ref bare', () => {
    expect(bodyRef('item:G7H8K2M4P9Q1R5T3VX', 'Add column')).toBe('`item:G7H8K2M4P9Q1R5T3VX` "Add column"')
    expect(bodyRef('adr:0007-refund-policy')).toBe('`adr:0007-refund-policy`')
  })
})

describe('KB_REF_RE', () => {
  it('matches canonical bucket:slug refs', () => {
    expect(KB_REF_RE.exec('adr:my-decision')?.slice(1)).toEqual(['adr', 'my-decision'])
    expect(KB_REF_RE.exec('feature:assembly-line/wiring')?.slice(1)).toEqual([
      'feature',
      'assembly-line/wiring',
    ])
  })

  it('rejects unknown buckets and malformed slugs', () => {
    expect(KB_REF_RE.test('item:abc')).toBe(false)
    expect(KB_REF_RE.test('adr:')).toBe(false)
    expect(KB_REF_RE.test('adr:Foo')).toBe(false) // uppercase not allowed
    expect(KB_REF_RE.test('adr:foo bar')).toBe(false)
  })
})

describe('joinPosix', () => {
  it('normalizes . and .. segments', () => {
    expect(joinPosix('.knit/kb/features/foo', '../bar')).toBe('.knit/kb/features/bar')
    expect(joinPosix('.knit/kb/features/foo', './baz.md')).toBe(
      '.knit/kb/features/foo/baz.md',
    )
    expect(joinPosix('a/b', '../../c')).toBe('c')
  })
})

describe('resolveLink', () => {
  const kbFeatures = '.knit/kb/features/assembly-line'

  it('resolves a bare bucket:slug ref to a kb-entry (before protocol guard)', () => {
    expect(resolveLink('anywhere', 'adr:my-decision')).toEqual({
      kind: 'kb-entry',
      bucket: 'adr',
      slug: 'my-decision',
    })
  })

  it('ignores protocol, root-absolute, and anchor hrefs', () => {
    expect(resolveLink(kbFeatures, 'https://example.com')).toEqual({ kind: 'ignore' })
    expect(resolveLink(kbFeatures, 'mailto:a@b.c')).toEqual({ kind: 'ignore' })
    expect(resolveLink(kbFeatures, '/items')).toEqual({ kind: 'ignore' })
    expect(resolveLink(kbFeatures, '#section')).toEqual({ kind: 'ignore' })
  })

  it('maps an item md path to an item id', () => {
    const id = 'G7H8K2M4P9Q1R5T3VX'
    expect(resolveLink('.knit/items', `${id}-add-column.md`)).toEqual({ kind: 'item', id })
    expect(resolveLink('.knit/kb/docs', `../../items/${id}-add-column.md`)).toEqual({ kind: 'item', id })
  })

  it('opens a file in a folder under items as a file, not an item', () => {
    const id = 'G7H8K2M4P9Q1R5T3VX'
    expect(resolveLink('.knit/items', `${id}-add-column/index.md`)).toEqual({
      kind: 'file',
      path: `.knit/items/${id}-add-column/index.md`,
    })
  })

  it('resolves an item ref with the full id, and only the full id', () => {
    expect(resolveLink('anywhere', 'item:g7h8k2m4p9q1r5t3vx')).toEqual({
      kind: 'item',
      id: 'G7H8K2M4P9Q1R5T3VX',
    })
    expect(resolveLink('anywhere', 'item:G7H8')).toEqual({ kind: 'ignore' })
  })

  it('resolves a project ref to its doc folder', () => {
    expect(resolveLink('anywhere', 'project:launch')).toEqual({ kind: 'project', slug: 'launch' })
  })

  it('resolves a flat KB entry', () => {
    expect(resolveLink('.knit/kb/adrs', 'my-decision.md')).toEqual({
      kind: 'kb-entry',
      bucket: 'adr',
      slug: 'my-decision',
    })
  })

  it('resolves a folder-form KB entry via readme.md', () => {
    expect(resolveLink('.knit/kb/features', 'assembly-line/readme.md')).toEqual({
      kind: 'kb-entry',
      bucket: 'feature',
      slug: 'assembly-line',
    })
  })

  it('resolves a folder-form sub-doc', () => {
    expect(resolveLink('.knit/kb/features', 'assembly-line/wiring.md')).toEqual({
      kind: 'kb-entry',
      bucket: 'feature',
      slug: 'assembly-line/wiring',
    })
  })

  it('drops an SVG pointer fragment, which is no section', () => {
    expect(resolveLink('', 'doc:guide#order-flow.svg/node/shop')).toEqual({ kind: 'kb-entry', bucket: 'doc', slug: 'guide' })
    expect(resolveLink('', 'doc:guide#order-flow.svg')).toEqual({ kind: 'kb-entry', bucket: 'doc', slug: 'guide' })
  })

  it('treats a bucket index.md as the folder card', () => {
    expect(resolveLink('.knit/kb/adrs', 'index.md')).toEqual({
      kind: 'kb-folder',
      subpath: 'adrs',
    })
  })

  it('routes a non-bucket KB file (attachment) to the file viewer', () => {
    expect(resolveLink('.knit/kb', 'diagram.png')).toEqual({
      kind: 'file',
      path: '.knit/kb/diagram.png',
    })
  })

  it('forwards arbitrary repo paths to the file viewer', () => {
    expect(resolveLink('.knit/kb/adrs', '../../../design/foo.md')).toEqual({
      kind: 'file',
      path: 'design/foo.md',
    })
  })

  it('ignores an empty href', () => {
    expect(resolveLink(kbFeatures, '')).toEqual({ kind: 'ignore' })
  })

  it('resolves a bare design:slug ref to a design link', () => {
    expect(resolveLink('anywhere', 'design:web-ui')).toEqual({ kind: 'design', slug: 'web-ui' })
  })

  it('maps an active design doc path to a design link', () => {
    expect(resolveLink('.knit/designs', 'web-ui-design.md')).toEqual({
      kind: 'design',
      slug: 'web-ui',
    })
  })

  it('maps an archived design path to its slug via the completed folder', () => {
    expect(resolveLink('.knit/designs/completed/web-ui', 'm1c-design.md')).toEqual({
      kind: 'design',
      slug: 'web-ui',
    })
  })

  it('routes a non-design file under .knit/designs to the file viewer', () => {
    expect(resolveLink('.knit/designs', 'diagram.png')).toEqual({
      kind: 'file',
      path: '.knit/designs/diagram.png',
    })
  })
})

describe('parentFromAbsoluteUnderRepo', () => {
  it('derives the repo-relative parent dir from an absolute path', () => {
    expect(
      parentFromAbsoluteUnderRepo(
        'C:/data/dev/Knit/.knit/kb/features/assembly-line/readme.md',
        '.knit',
      ),
    ).toBe('.knit/kb/features/assembly-line')
  })

  it('handles backslash paths', () => {
    expect(
      parentFromAbsoluteUnderRepo(
        'C:\\repo\\.knit\\items\\01950000-0000-7000-8000-000000000000\\readme.md',
        '.knit',
      ),
    ).toBe('.knit/items/01950000-0000-7000-8000-000000000000')
  })

  it('returns null when the marker is absent', () => {
    expect(parentFromAbsoluteUnderRepo('/tmp/elsewhere/foo.md', '.knit')).toBeNull()
  })
})
