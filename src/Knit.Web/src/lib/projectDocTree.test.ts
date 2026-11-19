import { describe, it, expect } from 'vitest'
import {
  buildDocTree,
  docDropTarget,
  folderContainerOf,
  promoteToFolderTarget,
} from './projectDocTree'

describe('buildDocTree', () => {
  it('returns flat docs at root, name-sorted', () => {
    const t = buildDocTree(['notes.md', 'agenda.md'])
    expect(t.map((n) => n.name)).toEqual(['agenda.md', 'notes.md'])
    expect(t.every((n) => !n.isFolder && n.container === '')).toBe(true)
  })

  it('groups a folder-form entry into a folder node with children', () => {
    const t = buildDocTree(['specs/readme.md', 'specs/paint-shop.md', 'vendor-notes.md'])
    // Folders sort before flat docs.
    expect(t.map((n) => `${n.name}:${n.isFolder}`)).toEqual(['specs:true', 'vendor-notes.md:false'])
    const specs = t[0]!
    expect(specs.path).toBe('specs/readme.md')
    expect(specs.container).toBe('')
    // readme.md is the folder node itself, not a child.
    expect(specs.children.map((c) => c.name)).toEqual(['paint-shop.md'])
    expect(specs.children[0]!.path).toBe('specs/paint-shop.md')
    expect(specs.children[0]!.container).toBe('specs')
  })

  it('handles nested folders', () => {
    const t = buildDocTree(['docs/specs/readme.md', 'docs/readme.md'])
    const docs = t[0]!
    expect(docs.name).toBe('docs')
    expect(docs.path).toBe('docs/readme.md')
    const specs = docs.children[0]!
    expect(specs.isFolder).toBe(true)
    expect(specs.path).toBe('docs/specs/readme.md')
    expect(specs.container).toBe('docs')
  })

  it('ignores empty / stray-separator paths', () => {
    expect(buildDocTree(['', '  '])).toEqual([])
  })
})

describe('folderContainerOf', () => {
  it('is the folder path a folder node receives children into', () => {
    const [specs] = buildDocTree(['specs/readme.md'])
    expect(folderContainerOf(specs!)).toBe('specs')
  })
})

describe('docDropTarget', () => {
  it('moves a flat root doc into a folder', () => {
    expect(docDropTarget('notes.md', 'specs')).toBe('specs/notes.md')
  })

  it('moves a doc out of a folder back to root', () => {
    expect(docDropTarget('specs/notes.md', '')).toBe('notes.md')
  })

  it('moves a doc between folders', () => {
    expect(docDropTarget('a/notes.md', 'b')).toBe('b/notes.md')
  })

  it('is a no-op when already in the target container', () => {
    expect(docDropTarget('notes.md', '')).toBeNull()
    expect(docDropTarget('specs/notes.md', 'specs')).toBeNull()
  })

  it('moves a whole folder-form doc into another folder (dest without readme.md)', () => {
    // Move folder `specs` (root) into `docs` → destination folder `docs/specs`;
    // the move endpoint rebuilds docs/specs/readme.md.
    expect(docDropTarget('specs/readme.md', 'docs')).toBe('docs/specs')
  })

  it('moves a folder-form doc back to root', () => {
    expect(docDropTarget('docs/specs/readme.md', '')).toBe('specs')
  })

  it('refuses to drop a folder into itself or its own subtree', () => {
    expect(docDropTarget('specs/readme.md', 'specs')).toBeNull()
    expect(docDropTarget('specs/readme.md', 'specs/sub')).toBeNull()
  })
})

describe('promoteToFolderTarget', () => {
  it('turns a flat root doc into a folder-form entry', () => {
    expect(promoteToFolderTarget('notes.md')).toBe('notes/readme.md')
  })

  it('promotes a flat doc nested inside a folder', () => {
    expect(promoteToFolderTarget('a/b.md')).toBe('a/b/readme.md')
  })
})
