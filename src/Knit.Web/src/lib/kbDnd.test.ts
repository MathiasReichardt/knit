import { describe, expect, it } from 'vitest'
import { checkKbDrop, kbDropSlug, type KbDragPayload } from './kbDnd'
import type { KbBucket } from '../api/kb'

const entry = (bucket: KbBucket, slug: string): KbDragPayload => ({
  ref: `${bucket}:${slug}`,
  bucket,
  slug,
  title: slug,
})

describe('checkKbDrop', () => {
  it('refuses another bucket', () => {
    const v = checkKbDrop(entry('doc', 'a'), { kind: 'bucket', bucket: 'adr' })
    expect(v.kind === 'refused' && v.reason).toBe("A doc entry can't move to adr")
  })

  it('asks for a split before a flat entry can take sub-docs', () => {
    const v = checkKbDrop(entry('doc', 'a'), { kind: 'flat', bucket: 'doc', slug: 'b' })
    expect(v).toEqual({ kind: 'refused', reason: "doc:b can't hold sub-docs yet", hint: 'Split it into folder form first' })
  })

  it('points to the bucket folder where entries never take sub-docs', () => {
    const v = checkKbDrop(entry('adr', 'a'), { kind: 'flat', bucket: 'adr', slug: 'b' })
    expect(v.kind === 'refused' && v.reason).toBe("adr entries can't hold sub-docs")
  })

  it('is a no-op onto itself, below itself, or where it already is', () => {
    expect(checkKbDrop(entry('doc', 'a'), { kind: 'flat', bucket: 'doc', slug: 'a' }).kind).toBe('noop')
    expect(checkKbDrop(entry('doc', 'a'), { kind: 'entry', bucket: 'doc', parentSlug: 'a/b' }).kind).toBe('noop')
    expect(checkKbDrop(entry('doc', 'p/a'), { kind: 'entry', bucket: 'doc', parentSlug: 'p' }).kind).toBe('noop')
    expect(checkKbDrop(entry('doc', 'a'), { kind: 'bucket', bucket: 'doc' }).kind).toBe('noop')
  })
})

describe('kbDropSlug', () => {
  it('nests under a folder-form entry and lifts to the top level', () => {
    expect(kbDropSlug(entry('doc', 'a'), { kind: 'entry', bucket: 'doc', parentSlug: 'p' })).toBe('p/a')
    expect(kbDropSlug(entry('doc', 'p/a'), { kind: 'bucket', bucket: 'doc' })).toBe('a')
  })

  it('is null for a drop that does not land', () => {
    expect(kbDropSlug(entry('doc', 'a'), { kind: 'flat', bucket: 'doc', slug: 'b' })).toBeNull()
  })
})
