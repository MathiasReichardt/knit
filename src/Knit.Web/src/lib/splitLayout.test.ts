import { describe, expect, it } from 'vitest'
import { parseCollapsed, resolveDrag, stepCollapsed } from './splitLayout'

describe('parseCollapsed', () => {
  it('falls back to none for anything unknown', () => {
    expect(parseCollapsed(null)).toBe('none')
    expect(parseCollapsed('both')).toBe('none')
    expect(parseCollapsed('left')).toBe('left')
    expect(parseCollapsed('right')).toBe('right')
  })
})

describe('stepCollapsed', () => {
  it('collapses the side it points to when both are open', () => {
    expect(stepCollapsed('none', 'left')).toBe('left')
    expect(stepCollapsed('none', 'right')).toBe('right')
  })

  it('reopens the collapsed side instead of collapsing the other', () => {
    expect(stepCollapsed('left', 'right')).toBe('none')
    expect(stepCollapsed('right', 'left')).toBe('none')
  })

  it('stays put when pushed further the same way', () => {
    expect(stepCollapsed('left', 'left')).toBe('left')
    expect(stepCollapsed('right', 'right')).toBe('right')
  })
})

describe('resolveDrag', () => {
  const W = 1200
  const MIN = 320

  it('follows the pointer inside the allowed range', () => {
    expect(resolveDrag(600, W, MIN)).toEqual({ collapsed: 'none', leftWidth: 600 })
  })

  it('sticks at the minimum before the snap point', () => {
    expect(resolveDrag(200, W, MIN)).toEqual({ collapsed: 'none', leftWidth: MIN })
    expect(resolveDrag(W - 200, W, MIN)).toEqual({ collapsed: 'none', leftWidth: W - MIN })
  })

  it('snaps a pane shut below half its minimum', () => {
    expect(resolveDrag(MIN / 2 - 1, W, MIN).collapsed).toBe('left')
    expect(resolveDrag(W - MIN / 2 + 1, W, MIN).collapsed).toBe('right')
  })
})
