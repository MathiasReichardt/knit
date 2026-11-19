import { describe, it, expect } from 'vitest'
import { hasRepoConventions, scopeView, type ConventionsScope } from './conventions'

const scope = (over: Partial<ConventionsScope>): ConventionsScope => ({
  scope: 'repo',
  path: '/r/.knit/conventions.md',
  exists: false,
  text: '',
  writable: true,
  etag: '"0"',
  ...over,
})

describe('scopeView', () => {
  it('shows the text of an existing file', () => {
    expect(scopeView(scope({ exists: true, text: 'Rule.\n' }))).toEqual({ show: 'text', canEdit: true, canDelete: true })
  })

  it('shows the empty state for a missing file', () => {
    expect(scopeView(scope({}))).toEqual({ show: 'empty', canEdit: true, canDelete: false })
  })

  it('offers no edit for a scope the host cannot write', () => {
    expect(scopeView(scope({ scope: 'user', writable: false, exists: true }))).toMatchObject({ canEdit: false, canDelete: false })
  })
})

describe('hasRepoConventions', () => {
  it('is true only when the repo file exists', () => {
    expect(hasRepoConventions(undefined)).toBe(false)
    expect(hasRepoConventions({ scopes: [scope({ scope: 'user', exists: true }), scope({})] })).toBe(false)
    expect(hasRepoConventions({ scopes: [scope({ exists: true })] })).toBe(true)
  })
})
