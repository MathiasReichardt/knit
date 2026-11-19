import { describe, expect, it } from 'vitest'
import { brokenMounts, mountBlockReason, mountFixCommand, type MountRow } from './mounts'

const row = (over: Partial<MountRow> = {}): MountRow => ({
  name: 'docs',
  declaredPath: '../company-docs',
  effectivePath: '/repos/company-docs',
  source: 'config.json',
  resolves: true,
  portable: true,
  sourceCount: 1,
  reason: null,
  ...over,
})

describe('mountBlockReason', () => {
  it('lets a source with no mount through', () => {
    expect(mountBlockReason([], null)).toBeNull()
  })

  it('assumes the mount is fine until the list arrives', () => {
    // Otherwise every page load flashes "unresolved" before the query
    // resolves, on a mount that is perfectly healthy.
    expect(mountBlockReason(undefined, 'docs')).toBeNull()
  })

  it('lets a resolved mount through', () => {
    expect(mountBlockReason([row()], 'docs')).toBeNull()
  })

  it('blocks an unresolved mount, wording it as a config problem', () => {
    expect(mountBlockReason([row({ resolves: false })], 'docs')).toBe("mount 'docs' unresolved")
  })

  it('blocks a mount nothing declares', () => {
    expect(mountBlockReason([row({ name: 'other' })], 'docs')).toBe("mount 'docs' is not declared")
  })

  it('is not portable but resolves, so it does not block', () => {
    // Non-portable is a warning for the *next* machine, not a reason to
    // refuse opening the file on this one.
    expect(mountBlockReason([row({ portable: false, reason: 'absolute path…' })], 'docs')).toBeNull()
  })
})

describe('mountFixCommand', () => {
  it('repoints the committed config by default', () => {
    expect(mountFixCommand(row())).toBe('knit mount set docs <path>')
  })

  it('keeps a local override local', () => {
    expect(mountFixCommand(row({ source: 'config.local.json' }))).toBe(
      'knit mount set docs <path> --local',
    )
  })

  it('offers the committed form for a mount nothing declares', () => {
    expect(mountFixCommand(row({ source: null }))).toBe('knit mount set docs <path>')
  })
})

describe('brokenMounts', () => {
  it('keeps only mounts with a reason', () => {
    const bad = row({ name: 'old', resolves: false, reason: 'path does not exist' })
    expect(brokenMounts([row(), bad])).toEqual([bad])
  })

  it('treats a list not loaded yet as no broken mounts', () => {
    expect(brokenMounts(undefined)).toEqual([])
  })
})
