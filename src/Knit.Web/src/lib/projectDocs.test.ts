import { describe, expect, it } from 'vitest'
import { docFolderName, isFolderFormDoc, isReservedDoc } from './projectDocs'

describe('isReservedDoc', () => {
  it('flags readme/roadmap (case-insensitive) as reserved', () => {
    expect(isReservedDoc('readme.md')).toBe(true)
    expect(isReservedDoc('ROADMAP.md')).toBe(true)
  })

  it('treats free-form docs as not reserved', () => {
    expect(isReservedDoc('notes.md')).toBe(false)
    // A nested readme belongs to a free-form folder doc, not the project readme.
    expect(isReservedDoc('guide/readme.md')).toBe(false)
  })
})

describe('folder-form docs', () => {
  it('detects folder form by the trailing readme', () => {
    expect(isFolderFormDoc('guide/readme.md')).toBe(true)
    expect(isFolderFormDoc('a/b/readme.md')).toBe(true)
    expect(isFolderFormDoc('notes.md')).toBe(false)
  })

  it('extracts the owning folder name', () => {
    expect(docFolderName('guide/readme.md')).toBe('guide')
    expect(docFolderName('a/b/readme.md')).toBe('a/b')
    expect(docFolderName('notes.md')).toBeNull()
  })
})
