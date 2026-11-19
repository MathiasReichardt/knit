import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useItemReveal } from './itemReveal'

describe('itemReveal store', () => {
  beforeEach(() => useItemReveal.setState({ target: null }))
  afterEach(() => vi.useRealTimers())

  it('a repeat request for the same id is a new target, so the view reveals again', () => {
    useItemReveal.getState().request('a')
    const first = useItemReveal.getState().target!
    useItemReveal.getState().request('a')
    const second = useItemReveal.getState().target!
    expect(second).not.toBe(first)
    expect(second.nonce).toBe(first.nonce + 1)
  })

  it('take() returns a fresh request once and clears it', () => {
    useItemReveal.getState().request('a')
    expect(useItemReveal.getState().take()?.id).toBe('a')
    expect(useItemReveal.getState().target).toBeNull()
    expect(useItemReveal.getState().take()).toBeNull()
  })

  it('a request no view took in time is dropped, not queued for the next visit', () => {
    vi.useFakeTimers()
    useItemReveal.getState().request('a')
    vi.advanceTimersByTime(5000)
    expect(useItemReveal.getState().take()).toBeNull()
    expect(useItemReveal.getState().target).toBeNull()
  })
})
