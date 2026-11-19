import { describe, expect, it } from 'vitest'
import { handleSuggestions, handleVocabulary } from './handleVocabulary'

describe('handleVocabulary', () => {
  it('ranks by use, then ordinal', () => {
    const rows = [{ assignee: 'zed' }, { assignee: 'zed' }, { assignee: 'amy' }, { assignee: null }, { assignee: '' }]
    expect(handleVocabulary(rows)).toEqual(['zed', 'amy'])
  })

  it('keeps case variants apart — the server matches exactly', () => {
    expect(handleVocabulary([{ assignee: 'bob' }, { assignee: 'Bob' }, { assignee: 'bob' }])).toEqual(['bob', 'Bob'])
  })

  it('always offers your own handle, even unused', () => {
    expect(handleVocabulary([{ assignee: 'bob' }], 'newcomer')).toEqual(['bob', 'newcomer'])
    expect(handleVocabulary([{ assignee: 'me' }, { assignee: 'me' }, { assignee: 'bob' }], 'me')).toEqual(['me', 'bob'])
  })
})

describe('handleSuggestions', () => {
  const vocab = ['bob', 'Bob', 'robert', 'alice']

  it('offers everything for an empty draft', () => {
    expect(handleSuggestions(vocab, '')).toEqual(vocab)
  })

  it('leads with prefix matches, then substring, case-insensitively', () => {
    expect(handleSuggestions(vocab, 'b')).toEqual(['bob', 'Bob', 'robert'])
  })

  it('drops the exact draft but keeps its case variant', () => {
    expect(handleSuggestions(vocab, 'bob')).toEqual(['Bob'])
  })
})
