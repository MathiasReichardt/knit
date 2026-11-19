import { describe, it, expect } from 'vitest'
import { useEditIntent } from './editIntent'

describe('editIntent store', () => {
  it('request() increments the nonce', () => {
    const start = useEditIntent.getState().nonce
    useEditIntent.getState().request()
    expect(useEditIntent.getState().nonce).toBe(start + 1)
  })

  it('notifies subscribers each time edit is requested', () => {
    let prev = useEditIntent.getState().nonce
    let fired = 0
    const unsub = useEditIntent.subscribe((s) => {
      if (s.nonce !== prev) {
        prev = s.nonce
        fired++
      }
    })
    useEditIntent.getState().request()
    useEditIntent.getState().request()
    unsub()
    expect(fired).toBe(2)
  })
})
