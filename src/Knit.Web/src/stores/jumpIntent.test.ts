import { describe, it, expect, beforeEach } from 'vitest'
import { useJumpIntent } from './jumpIntent'

describe('jumpIntent store', () => {
  beforeEach(() => useJumpIntent.setState({ intent: null }))

  it('request() carries the target and bumps the nonce', () => {
    useJumpIntent.getState().request({ kind: 'item', targetId: 'a', field: 'body', bodyOffset: 12, query: 'hair' })
    const first = useJumpIntent.getState().intent!
    expect(first).toMatchObject({ kind: 'item', targetId: 'a', field: 'body', bodyOffset: 12, query: 'hair' })

    useJumpIntent.getState().request({ kind: 'item', targetId: 'a', field: 'body', bodyOffset: 12, query: 'hair' })
    // Re-picking the same hit produces a fresh nonce so the flash replays.
    expect(useJumpIntent.getState().intent!.nonce).toBe(first.nonce + 1)
  })

  it('consume() clears the intent so a later visit does not re-flash', () => {
    useJumpIntent.getState().request({ kind: 'design', targetId: 'a', field: 'body', bodyOffset: null, query: 'x' })
    useJumpIntent.getState().consume()
    expect(useJumpIntent.getState().intent).toBeNull()
  })
})
