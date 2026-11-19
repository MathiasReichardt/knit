import { describe, expect, it } from 'vitest'
import { createGesture, DRAG_THRESHOLD, FIT, fitView, MAX_SCALE, MIN_SCALE, wheelFactor, zoomAt } from './panZoom'

describe('createGesture', () => {
  it('treats a press within the threshold as a click', () => {
    const g = createGesture()
    g.down(10, 10)
    expect(g.move(10 + DRAG_THRESHOLD, 10)).toBeNull()
    g.up()
    expect(g.click()).toBe(true)
  })

  it('pans past the threshold and swallows the click after the drag', () => {
    const g = createGesture()
    g.down(10, 10)
    expect(g.move(20, 10)).toEqual({ dx: 10, dy: 0 })
    expect(g.dragging).toBe(true)
    expect(g.move(25, 13)).toEqual({ dx: 5, dy: 3 })
    g.up()
    expect(g.click()).toBe(false)
    expect(g.click()).toBe(true)
  })

  it('keeps panning after the pointer comes back inside the threshold', () => {
    const g = createGesture()
    g.down(0, 0)
    g.move(10, 0)
    expect(g.move(0, 0)).toEqual({ dx: -10, dy: 0 })
  })

  it('ignores moves without a press', () => {
    const g = createGesture()
    expect(g.move(50, 50)).toBeNull()
  })

  it('does not let a drag without a click swallow the next press', () => {
    const g = createGesture()
    g.down(0, 0)
    g.move(30, 0)
    g.up()
    g.down(0, 0)
    g.up()
    expect(g.click()).toBe(true)
  })
})

describe('zoomAt', () => {
  it('keeps the point under the cursor fixed', () => {
    const v = zoomAt({ x: 10, y: 20, scale: 1 }, 2, 110, 70)
    expect(v.scale).toBe(2)
    // Diagram point under (110, 70) was (100, 50); it stays there.
    expect((110 - v.x) / v.scale).toBe(100)
    expect((70 - v.y) / v.scale).toBe(50)
  })

  it('clamps the scale', () => {
    expect(zoomAt(FIT, 100, 0, 0).scale).toBe(MAX_SCALE)
    expect(zoomAt(FIT, 0.001, 0, 0).scale).toBe(MIN_SCALE)
  })
})

describe('fitView', () => {
  it('scales to the tighter side and centres the other', () => {
    expect(fitView(800, 400, 200, 200)).toEqual({ x: 200, y: 0, scale: 2 })
  })

  it('falls back to identity for an unmeasured diagram', () => {
    expect(fitView(800, 400, 0, 0)).toEqual(FIT)
  })
})

describe('wheelFactor', () => {
  it('zooms in on wheel up, out on wheel down', () => {
    expect(wheelFactor(-100, 0)).toBeGreaterThan(1)
    expect(wheelFactor(100, 0)).toBeLessThan(1)
  })

  it('scales line-mode deltas like pixels', () => {
    expect(wheelFactor(3, 1)).toBeCloseTo(wheelFactor(48, 0))
  })
})
