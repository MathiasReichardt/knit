import { describe, expect, it } from 'vitest'
import { tagSuggestions, tagVocabulary } from './tagVocabulary'

describe('tagVocabulary', () => {
  it('ranks by use, then alphabetically', () => {
    const rows = [
      { tags: ['api', 'ux'] },
      { tags: ['api', 'perf'] },
      { tags: ['api'] },
      { tags: ['perf'] },
    ]
    expect(tagVocabulary(rows)).toEqual(['api', 'perf', 'ux'])
  })

  it('folds case to one entry and keeps the commonest spelling', () => {
    // The server dedupes case-insensitively on write, so offering both would
    // be offering the same tag twice.
    const rows = [{ tags: ['API'] }, { tags: ['api'] }, { tags: ['api'] }]
    expect(tagVocabulary(rows)).toEqual(['api'])
  })

  it('breaks a spelling tie deterministically', () => {
    expect(tagVocabulary([{ tags: ['API'] }, { tags: ['api'] }])).toEqual(['API'])
  })

  it('ignores rows with no tags and blank entries', () => {
    expect(tagVocabulary([{}, { tags: [] }, { tags: ['  ', 'ux'] }])).toEqual(['ux'])
  })

  it('trims before comparing', () => {
    expect(tagVocabulary([{ tags: [' ux '] }, { tags: ['ux'] }])).toEqual(['ux'])
  })
})

describe('tagSuggestions', () => {
  const vocab = ['api', 'api-gateway', 'perf', 'ux', 'legacy-api']

  it('offers the whole vocabulary for an empty draft', () => {
    // Opening the input is how you find out what tags exist at all.
    expect(tagSuggestions(vocab, '', [])).toEqual(vocab)
  })

  it('puts prefix matches before substring matches', () => {
    expect(tagSuggestions(vocab, 'api', [])).toEqual(['api', 'api-gateway', 'legacy-api'])
  })

  it('is case-insensitive', () => {
    expect(tagSuggestions(vocab, 'API', [])).toEqual(['api', 'api-gateway', 'legacy-api'])
  })

  it('drops tags the entry already carries', () => {
    expect(tagSuggestions(vocab, 'api', ['API'])).toEqual(['api-gateway', 'legacy-api'])
  })

  it('honours the limit', () => {
    expect(tagSuggestions(vocab, '', [], 2)).toEqual(['api', 'api-gateway'])
  })

  it('returns nothing when the draft matches nothing', () => {
    expect(tagSuggestions(vocab, 'zzz', [])).toEqual([])
  })

  it('ignores surrounding whitespace in the draft', () => {
    expect(tagSuggestions(vocab, '  ux  ', [])).toEqual(['ux'])
  })
})
