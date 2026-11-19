import { describe, expect, it } from 'vitest'
import { tokenize, rangesInText, highlightSegments } from './searchHighlight'

describe('tokenize', () => {
  it('folds case + diacritics and splits on non-word chars', () => {
    expect(tokenize('Café, Hair-do')).toEqual(['cafe', 'hair', 'do'])
  })

  it('drops empties', () => {
    expect(tokenize('  ')).toEqual([])
  })
})

describe('rangesInText', () => {
  it('matches a token as a plain substring, including mid-word', () => {
    // "hair" hits "hair", "haircut" AND "mohair" (mid-word).
    const text = 'hair haircut mohair'
    expect(rangesInText(text, ['hair'])).toEqual([
      [0, 4],
      [5, 9],
      [15, 19],
    ])
  })

  it('is case- and diacritic-insensitive against the corpus', () => {
    const text = 'The Café is open'
    // offsets are into the original text (fold is length-preserving)
    expect(rangesInText(text, ['cafe'])).toEqual([[4, 8]])
  })

  it('finds every token across the text', () => {
    const text = 'install the long fluffy hair'
    expect(rangesInText(text, ['hair', 'install'])).toEqual([
      [24, 28], // hair
      [0, 7], // install
    ])
  })

  it('returns nothing when no token matches', () => {
    expect(rangesInText('round chatty model', ['hair'])).toEqual([])
  })
})

describe('highlightSegments', () => {
  it('splits text into plain + marked segments', () => {
    expect(highlightSegments('the brown fox', 'brown')).toEqual([
      { text: 'the ', mark: false },
      { text: 'brown', mark: true },
      { text: ' fox', mark: false },
    ])
  })

  it('merges overlapping/adjacent token matches into one mark', () => {
    // "cat" + "ate" overlap inside "category" → a single fused mark.
    expect(highlightSegments('category', 'cat ate')).toEqual([
      { text: 'cate', mark: true },
      { text: 'gory', mark: false },
    ])
  })

  it('returns a single unmarked segment when nothing matches', () => {
    expect(highlightSegments('nothing here', 'xyz')).toEqual([
      { text: 'nothing here', mark: false },
    ])
  })
})
