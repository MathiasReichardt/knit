import { describe, it, expect } from 'vitest'
import { changedKeys, createHistoryPolicy, withoutHistory } from './urlHistory'

describe('createHistoryPolicy', () => {
  it('pushes a discrete change', () => {
    const p = createHistoryPolicy(['query'])
    expect(p.shouldPush(['slate'], 0)).toBe(true)
    expect(p.shouldPush(['under'], 10)).toBe(true)
  })

  it('pushes once per typing burst, then rewrites until idle', () => {
    const p = createHistoryPolicy(['query'], 1000)
    expect(p.shouldPush(['query'], 0)).toBe(true)
    expect(p.shouldPush(['query'], 300)).toBe(false)
    expect(p.shouldPush(['query'], 1200)).toBe(false)
    expect(p.shouldPush(['query'], 2300)).toBe(true)
  })

  it('starts a new burst after a discrete change or a reset', () => {
    const p = createHistoryPolicy(['query'], 1000)
    p.shouldPush(['query'], 0)
    p.shouldPush(['slate'], 100)
    expect(p.shouldPush(['query'], 200)).toBe(true)
    p.reset()
    expect(p.shouldPush(['query'], 300)).toBe(true)
  })

  it('never pushes inside withoutHistory', () => {
    const p = createHistoryPolicy()
    let push = true
    withoutHistory(() => {
      push = p.shouldPush(['slate'])
    })
    expect(push).toBe(false)
    expect(p.shouldPush(['slate'])).toBe(true)
  })
})

describe('changedKeys', () => {
  it('lists added, removed and changed keys', () => {
    expect(changedKeys({ a: 1, b: [1], c: 'x' }, { b: [2], c: 'x', d: true }).sort()).toEqual(['a', 'b', 'd'])
  })
})
