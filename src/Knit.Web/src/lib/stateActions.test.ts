import { describe, it, expect } from 'vitest'
import { Glasses } from 'lucide-react'
import { stateActions } from './stateActions'
import { STATE_COLOR, STATE_ICON, STATE_LABEL } from './glyphs'

describe('review glyph', () => {
  it('is violet glasses labelled Review', () => {
    expect(STATE_ICON.review).toBe(Glasses)
    expect(STATE_COLOR.review).toContain('violet')
    expect(STATE_LABEL.review).toBe('Review')
  })
})

describe('stateActions', () => {
  it('offers submit only from in-progress', () => {
    expect(stateActions('in-progress').submit).toBe(true)
    for (const s of ['draft', 'todo', 'review', 'done', 'dismissed'] as const)
      expect(stateActions(s).submit).toBe(false)
  })

  it('offers done and send back from review', () => {
    const a = stateActions('review')
    expect(a.done).toBe(true)
    expect(a.startable).toBe(true)
    expect(a.sendBack).toBe(true)
    expect(a.dismiss).toBe(true)
  })

  it('starts but never sends back outside review', () => {
    expect(stateActions('todo')).toMatchObject({ startable: true, sendBack: false })
    expect(stateActions('in-progress').startable).toBe(false)
  })

  it('keeps a draft from closing and closed items from moving on', () => {
    expect(stateActions('draft')).toMatchObject({ approve: true, done: false, reopen: false })
    expect(stateActions('done')).toMatchObject({ startable: false, done: false, dismiss: false, reopen: true })
  })
})
