import { describe, expect, it } from 'vitest'
import { headLabel, headTitle, unsharedCommands, unsharedSummary, worktreeTitle, type GitHead } from './repo'

const head = (h: Partial<GitHead>): GitHead => ({
  checkout: 'C:\\dev\\app',
  branch: null,
  shortSha: null,
  rebasing: false,
  worktree: null,
  ...h,
})

describe('headLabel', () => {
  it('shows the branch', () => expect(headLabel(head({ branch: 'feat/x' }))).toBe('feat/x'))
  it('shows a detached HEAD as @sha', () => expect(headLabel(head({ shortSha: '1a2b3c4' }))).toBe('@1a2b3c4'))
  it('is null outside git', () => expect(headLabel(head({ checkout: null }))).toBeNull())
})

describe('headTitle', () => {
  it('names a rebase as the reason HEAD is detached', () =>
    expect(headTitle(head({ shortSha: '1a2b3c4', rebasing: true }))).toMatch(/^Rebase in progress at 1a2b3c4/))
  it('calls a plain detached HEAD that', () =>
    expect(headTitle(head({ shortSha: '1a2b3c4' }))).toMatch(/^Detached HEAD/))
})

describe('worktreeTitle', () => {
  it('is null outside a worktree', () => expect(worktreeTitle(head({ branch: 'main' }))).toBeNull())

  it('names the checkout the knit repo lives in', () =>
    expect(worktreeTitle(head({ branch: 'main', worktree: 'C:\\dev\\app-feat' })))
      .toBe('In worktree mode — knit data from C:\\dev\\app'))

  it('says only worktree mode when the knit repo is the worktree\'s own', () =>
    expect(worktreeTitle(head({ checkout: 'C:\\dev\\app-feat', branch: 'feat', worktree: 'C:\\dev\\app-feat\\' })))
      .toBe('In worktree mode'))
})

describe('unsharedSummary', () => {
  it('names the main checkout in a normal repo', () =>
    expect(unsharedSummary({ target: 'C:\\dev\\app', bare: false, command: 'knit root --worktree' }))
      .toBe('This worktree uses its own .knit/, and the main checkout (C:\\dev\\app) has one too.'))

  it("names the default branch's worktree in a bare repo", () =>
    expect(unsharedSummary({ target: 'C:\\gg\\main', bare: true, command: 'knit root C:\\gg\\main' }))
      .toContain("the default branch's worktree (C:\\gg\\main)"))

  it('says other worktrees when none can be named', () =>
    expect(unsharedSummary({ target: null, bare: true, command: 'knit root <path>' }))
      .toContain('other worktrees of this bare repo have one too'))
})

describe('unsharedCommands', () => {
  it('offers the share command first, then --local', () =>
    expect(unsharedCommands({ target: 'C:\\gg\\main', bare: true, command: 'knit root C:\\gg\\main' }).map(c => c.command))
      .toEqual(['knit root C:\\gg\\main', 'knit root --local']))
})
