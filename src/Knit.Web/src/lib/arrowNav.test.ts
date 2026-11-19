import { describe, it, expect } from 'vitest'
import { arrowNavStep, isArrowKey, isTyping, type NavRow } from './arrowNav'

// Helper: terse row builder.
function row(
  key: string,
  parentKey: string | null,
  opts: { expandable?: boolean; expanded?: boolean } = {},
): NavRow {
  return {
    key,
    parentKey,
    expandable: opts.expandable ?? false,
    expanded: opts.expanded ?? false,
    data: null,
  }
}

// Two expanded folders with two leaves each, in flattened render order.
//   a            (folder, expanded)
//   a/1          (leaf)
//   a/2          (leaf)
//   b            (folder, expanded)
//   b/1          (leaf)
//   b/2          (leaf)
const twoFolders: NavRow[] = [
  row('a', null, { expandable: true, expanded: true }),
  row('a/1', 'a'),
  row('a/2', 'a'),
  row('b', null, { expandable: true, expanded: true }),
  row('b/1', 'b'),
  row('b/2', 'b'),
]

describe('isArrowKey', () => {
  it('matches the four arrows only', () => {
    expect(isArrowKey('ArrowUp')).toBe(true)
    expect(isArrowKey('ArrowDown')).toBe(true)
    expect(isArrowKey('ArrowLeft')).toBe(true)
    expect(isArrowKey('ArrowRight')).toBe(true)
    expect(isArrowKey('Enter')).toBe(false)
    expect(isArrowKey('a')).toBe(false)
  })
})

describe('isTyping', () => {
  it('is true for inputs/textareas/contenteditable, false otherwise', () => {
    const input = { tagName: 'INPUT', isContentEditable: false } as unknown as HTMLElement
    const ta = { tagName: 'TEXTAREA', isContentEditable: false } as unknown as HTMLElement
    const ce = { tagName: 'DIV', isContentEditable: true } as unknown as HTMLElement
    const div = { tagName: 'DIV', isContentEditable: false } as unknown as HTMLElement
    expect(isTyping(input)).toBe(true)
    expect(isTyping(ta)).toBe(true)
    expect(isTyping(ce)).toBe(true)
    expect(isTyping(div)).toBe(false)
    expect(isTyping(null)).toBe(false)
  })
})

describe('arrowNavStep — vertical', () => {
  it('returns none on an empty row list', () => {
    expect(arrowNavStep([], null, 'ArrowDown')).toEqual({ type: 'none' })
  })

  // No cursor yet → the first arrow enters at row 0; it must not skip to row 1.
  it('enters at the first row when the cursor is null or unknown', () => {
    expect(arrowNavStep(twoFolders, null, 'ArrowDown')).toEqual({ type: 'activate', key: 'a' })
    expect(arrowNavStep(twoFolders, null, 'ArrowUp')).toEqual({ type: 'activate', key: 'a' })
    expect(arrowNavStep(twoFolders, null, 'ArrowRight')).toEqual({ type: 'activate', key: 'a' })
    expect(arrowNavStep(twoFolders, 'missing', 'ArrowDown')).toEqual({ type: 'activate', key: 'a' })
  })

  // The KB root folder row has key '' (empty string). It is a real cursor, not
  // "no cursor" — stepping from it must move, not re-enter row 0.
  it('treats an empty-string key as a real cursor (KB root)', () => {
    const rows = [
      row('', null, { expandable: true, expanded: true }),
      row('a', '', {}),
      row('b', '', {}),
    ]
    expect(arrowNavStep(rows, '', 'ArrowDown')).toEqual({ type: 'activate', key: 'a' })
    expect(arrowNavStep(rows, '', 'ArrowLeft')).toEqual({ type: 'toggle', key: '' })
  })

  it('↓ walks folder → its children → next folder', () => {
    expect(arrowNavStep(twoFolders, 'a', 'ArrowDown')).toEqual({ type: 'activate', key: 'a/1' })
    expect(arrowNavStep(twoFolders, 'a/1', 'ArrowDown')).toEqual({ type: 'activate', key: 'a/2' })
    expect(arrowNavStep(twoFolders, 'a/2', 'ArrowDown')).toEqual({ type: 'activate', key: 'b' })
  })

  it('↓ clamps at the last row', () => {
    expect(arrowNavStep(twoFolders, 'b/2', 'ArrowDown')).toEqual({ type: 'activate', key: 'b/2' })
  })

  it('↑ clamps at the first row', () => {
    expect(arrowNavStep(twoFolders, 'a', 'ArrowUp')).toEqual({ type: 'activate', key: 'a' })
  })

  it('↑ across a folder boundary lands on the previous folder’s last child', () => {
    expect(arrowNavStep(twoFolders, 'b', 'ArrowUp')).toEqual({ type: 'activate', key: 'a/2' })
    expect(arrowNavStep(twoFolders, 'a/2', 'ArrowUp')).toEqual({ type: 'activate', key: 'a/1' })
    expect(arrowNavStep(twoFolders, 'a/1', 'ArrowUp')).toEqual({ type: 'activate', key: 'a' })
  })
})

describe('arrowNavStep — horizontal', () => {
  it('→ expands a collapsed expandable row', () => {
    const rows = [row('a', null, { expandable: true, expanded: false })]
    expect(arrowNavStep(rows, 'a', 'ArrowRight')).toEqual({ type: 'toggle', key: 'a' })
  })

  it('→ on an expanded row steps into its first child', () => {
    expect(arrowNavStep(twoFolders, 'a', 'ArrowRight')).toEqual({ type: 'activate', key: 'a/1' })
  })

  it('→ on a leaf node does nothing', () => {
    const rows = [row('leaf', null, { expandable: false })]
    expect(arrowNavStep(rows, 'leaf', 'ArrowRight')).toEqual({ type: 'none' })
  })

  it('← collapses an expanded expandable row', () => {
    expect(arrowNavStep(twoFolders, 'a', 'ArrowLeft')).toEqual({ type: 'toggle', key: 'a' })
  })

  it('← on a leaf hops back to its parent', () => {
    expect(arrowNavStep(twoFolders, 'a/2', 'ArrowLeft')).toEqual({ type: 'activate', key: 'a' })
  })

  it('← on a collapsed child hops to its parent', () => {
    const rows = [
      row('p', null, { expandable: true, expanded: true }),
      row('c', 'p', { expandable: true, expanded: false }),
    ]
    expect(arrowNavStep(rows, 'c', 'ArrowLeft')).toEqual({ type: 'activate', key: 'p' })
  })

  it('← on a root collapsed row does nothing', () => {
    const rows = [row('a', null, { expandable: true, expanded: false })]
    expect(arrowNavStep(rows, 'a', 'ArrowLeft')).toEqual({ type: 'none' })
  })
})
