import { describe, expect, it } from 'vitest'
import {
  DEFAULT_MARKDOWN_SIZE,
  MARKDOWN_SIZES,
  MARKDOWN_SIZE_LABEL,
  notProseSizeClass,
  proseSizeClass,
} from './markdownSize'

describe('markdown size ladder', () => {
  it('defaults to the historical prose-sm step', () => {
    expect(DEFAULT_MARKDOWN_SIZE).toBe('sm')
    expect(proseSizeClass(DEFAULT_MARKDOWN_SIZE)).toBe('prose-sm')
  })

  it('maps every step to a distinct prose and not-prose class', () => {
    const prose = MARKDOWN_SIZES.map(proseSizeClass)
    const notProse = MARKDOWN_SIZES.map(notProseSizeClass)
    expect(new Set(prose).size).toBe(MARKDOWN_SIZES.length)
    expect(new Set(notProse).size).toBe(MARKDOWN_SIZES.length)
  })

  it('labels every step', () => {
    for (const size of MARKDOWN_SIZES) expect(MARKDOWN_SIZE_LABEL[size]).toBeTruthy()
  })
})
