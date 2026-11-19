import { describe, expect, it } from 'vitest'
import { headingAtLine, isEditorOutlineKey, isOutlineKey, outlinePinned, outlineStep, PIN_MIN_WIDTH } from './outlineNav'

const ids = ['a', 'b', 'c']

describe('outlineStep', () => {
  it('steps ↑/↓ and clamps at the ends', () => {
    expect(outlineStep(ids, 'a', 'ArrowDown')).toBe('b')
    expect(outlineStep(ids, 'c', 'ArrowDown')).toBe('c')
    expect(outlineStep(ids, 'b', 'ArrowUp')).toBe('a')
    expect(outlineStep(ids, 'a', 'ArrowUp')).toBe('a')
  })

  it('enters at the first heading without a focus', () => {
    expect(outlineStep(ids, null, 'ArrowDown')).toBe('a')
    expect(outlineStep(ids, 'gone', 'ArrowUp')).toBe('a')
  })

  it('Home / End go to the first / last heading', () => {
    expect(outlineStep(ids, 'b', 'Home')).toBe('a')
    expect(outlineStep(ids, 'b', 'End')).toBe('c')
  })

  it('gives nothing for an empty outline', () => {
    expect(outlineStep([], null, 'End')).toBeNull()
  })

  it('knows its keys', () => {
    expect(isOutlineKey('Home')).toBe(true)
    expect(isOutlineKey('ArrowLeft')).toBe(false)
  })
})

describe('outlinePinned', () => {
  it('pins only when asked and the pane is wide enough', () => {
    expect(outlinePinned(true, PIN_MIN_WIDTH)).toBe(true)
    expect(outlinePinned(true, PIN_MIN_WIDTH - 1)).toBe(false)
    expect(outlinePinned(false, 2 * PIN_MIN_WIDTH)).toBe(false)
  })
})

describe('headingAtLine', () => {
  const items = [
    { id: 'a', line: 3 },
    { id: 'b', line: 10 },
  ]
  it('picks the last heading at or above the line', () => {
    expect(headingAtLine(items, 1)).toBeNull()
    expect(headingAtLine(items, 3)).toBe('a')
    expect(headingAtLine(items, 9)).toBe('a')
    expect(headingAtLine(items, 40)).toBe('b')
  })
})

describe('isEditorOutlineKey', () => {
  const key = (init: KeyboardEventInit & { altGr?: boolean }) => {
    const e = new KeyboardEvent('keydown', init)
    if (init.altGr) Object.defineProperty(e, 'getModifierState', { value: (k: string) => k === 'AltGraph' })
    return e
  }
  it('takes Ctrl+Shift+O and Alt+O', () => {
    expect(isEditorOutlineKey(key({ key: 'O', code: 'KeyO', ctrlKey: true, shiftKey: true }))).toBe(true)
    expect(isEditorOutlineKey(key({ key: 'ø', code: 'KeyO', altKey: true }))).toBe(true)
  })
  it('leaves plain o, Ctrl+O and AltGr+O alone', () => {
    expect(isEditorOutlineKey(key({ key: 'o', code: 'KeyO' }))).toBe(false)
    expect(isEditorOutlineKey(key({ key: 'o', code: 'KeyO', ctrlKey: true }))).toBe(false)
    expect(isEditorOutlineKey(key({ key: 'ó', code: 'KeyO', ctrlKey: true, altKey: true, altGr: true }))).toBe(false)
  })
})
