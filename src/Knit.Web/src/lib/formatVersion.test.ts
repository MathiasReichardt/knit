import { describe, expect, it } from 'vitest'
import { formatVersionWarning } from './formatVersion'

describe('formatVersionWarning', () => {
  it('returns null while the version is unknown', () => {
    expect(formatVersionWarning(undefined)).toBeNull()
  })

  it('returns null when the server omits the format fields', () => {
    expect(formatVersionWarning({ version: '0.1.0' })).toBeNull()
  })

  it('returns null for a matching or older repo', () => {
    expect(formatVersionWarning({ version: '0.1.0', formatVersion: 1, supportedFormatVersion: 1 })).toBeNull()
    expect(formatVersionWarning({ version: '0.1.0', formatVersion: 1, supportedFormatVersion: 2 })).toBeNull()
  })

  it('warns when the repo is newer than the server understands', () => {
    const msg = formatVersionWarning({ version: '0.1.0', formatVersion: 3, supportedFormatVersion: 1 })
    expect(msg).toContain('format 3')
    expect(msg).toContain('understands 1')
  })
})
