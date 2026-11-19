import { describe, expect, it } from 'vitest'
import { ApiError } from './client'
import { forceReportCommand, pageFolder, reportToast } from './status'
import { resolveLink } from '../lib/resolveLink'

describe('reportToast', () => {
  it('names the written path', () =>
    expect(reportToast({ path: 'docs/status.md', written: true })).toEqual({ kind: 'success', message: 'Wrote docs/status.md' }))

  it('offers the force command when the target is hand-written', () => {
    const err = new ApiError(409, { title: 'not_generated', status: 409, code: 'not_generated', detail: 'project-status.md exists and was not written by knit' })
    expect(reportToast(err)).toEqual({ kind: 'error', message: 'project-status.md exists and was not written by knit', command: forceReportCommand })
  })

  it('offers no command for any other failure', () => {
    const err = new ApiError(400, { title: 'invalid_path', status: 400, code: 'invalid_path', detail: 'status_report.path: outside the repo' })
    expect(reportToast(err).command).toBeUndefined()
  })
})

// The page's links are relative to its own folder; the view resolves them
// against it. One case per link kind the renderer writes.
describe('status page links', () => {
  const id = 'ABCDEFGHJKMNPQRSTV'
  it.each([
    ['project-status.md', ''],
    ['docs/status.md', '../'],
  ])('resolve from %s', (path, up) => {
    const parent = pageFolder(path)
    expect(resolveLink(parent, `${up}.knit/items/${id}-shop.md`)).toEqual({ kind: 'item', id })
    expect(resolveLink(parent, `${up}.knit/projects/web/milestones/beta.md`)).toEqual({ kind: 'milestone', project: 'web', slug: 'beta' })
    expect(resolveLink(parent, `${up}.knit/projects/web/readme.md`)).toEqual({ kind: 'project', slug: 'web', path: 'readme.md' })
    expect(resolveLink(parent, `${up}.knit/designs/search-design.md`)).toEqual({ kind: 'design', slug: 'search' })
    expect(resolveLink(parent, `${up}.knit/kb/adrs/0001-pick-a-store.md`)).toEqual({ kind: 'kb-entry', bucket: 'adr', slug: '0001-pick-a-store' })
  })

  it('keeps the folder of a nested page', () => {
    expect(pageFolder('docs/team/status.md')).toBe('docs/team')
    expect(pageFolder('status.md')).toBe('')
  })
})
