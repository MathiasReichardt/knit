import { describe, expect, it } from 'vitest'
import { listboxKey, opensEditor, textKey } from './inlineKeys'

describe('listboxKey', () => {
  it('moves down and wraps at the tail', () => {
    expect(listboxKey('ArrowDown', 0, 3)).toEqual({ kind: 'move', index: 1 })
    expect(listboxKey('ArrowDown', 2, 3)).toEqual({ kind: 'move', index: 0 })
  })

  it('moves up and wraps at the head', () => {
    expect(listboxKey('ArrowUp', 2, 3)).toEqual({ kind: 'move', index: 1 })
    expect(listboxKey('ArrowUp', 0, 3)).toEqual({ kind: 'move', index: 2 })
  })

  it('jumps to the ends', () => {
    expect(listboxKey('Home', 2, 3)).toEqual({ kind: 'move', index: 0 })
    expect(listboxKey('End', 0, 3)).toEqual({ kind: 'move', index: 2 })
  })

  it('does not commit while arrowing', () => {
    // Holding ArrowDown must not write once per row.
    expect(listboxKey('ArrowDown', 1, 3).kind).toBe('move')
  })

  it('commits on Enter and Space', () => {
    expect(listboxKey('Enter', 1, 3)).toEqual({ kind: 'commit', index: 1 })
    expect(listboxKey(' ', 2, 3)).toEqual({ kind: 'commit', index: 2 })
  })

  it('commits and leaves on Tab', () => {
    expect(listboxKey('Tab', 1, 3)).toEqual({ kind: 'commitAndLeave', index: 1 })
  })

  it('cancels on Escape', () => {
    expect(listboxKey('Escape', 1, 3)).toEqual({ kind: 'cancel' })
  })

  it('ignores everything else', () => {
    expect(listboxKey('a', 0, 3)).toEqual({ kind: 'none' })
  })

  it('clamps an out-of-range active index rather than committing it', () => {
    expect(listboxKey('Enter', 9, 3)).toEqual({ kind: 'commit', index: 2 })
    expect(listboxKey('Enter', -1, 3)).toEqual({ kind: 'commit', index: 0 })
  })

  it('stays closable with no options at all', () => {
    expect(listboxKey('Escape', 0, 0)).toEqual({ kind: 'cancel' })
    expect(listboxKey('Enter', 0, 0)).toEqual({ kind: 'none' })
  })
})

describe('opensEditor', () => {
  it('opens on Enter, Space and either arrow', () => {
    for (const k of ['Enter', ' ', 'ArrowDown', 'ArrowUp']) expect(opensEditor(k)).toBe(true)
  })

  it('leaves other keys to the browser', () => {
    for (const k of ['Tab', 'Escape', 'a', 'ArrowLeft']) expect(opensEditor(k)).toBe(false)
  })
})

describe('textKey', () => {
  it('commits on Enter and reverts on Escape', () => {
    expect(textKey('Enter')).toEqual({ kind: 'commit' })
    expect(textKey('Escape')).toEqual({ kind: 'revert' })
  })

  it('leaves typing alone', () => {
    expect(textKey('a')).toEqual({ kind: 'none' })
  })
})
