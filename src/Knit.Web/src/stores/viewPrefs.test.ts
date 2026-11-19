import { describe, expect, it } from 'vitest'
import { isViewCustomized, VIEW_PREF_DEFAULTS } from './viewPrefs'

describe('isViewCustomized', () => {
  it('is false on the defaults and true once any pref differs', () => {
    expect(isViewCustomized(VIEW_PREF_DEFAULTS)).toBe(false)
    expect(isViewCustomized({ ...VIEW_PREF_DEFAULTS, due: 'overdue' })).toBe(true)
    expect(isViewCustomized({ ...VIEW_PREF_DEFAULTS, showIds: false })).toBe(true)
  })

  it('shows ids by default', () => {
    expect(VIEW_PREF_DEFAULTS.showIds).toBe(true)
  })
})
