import { describe, expect, it } from 'vitest'
import { forgeCommands, isPastedLink, resolvePastedLink } from './shareLink'
import { resolveLink } from './resolveLink'

const ID = 'K3F9ABCDEFGHJKMNPQ'
const item = `.knit/items/${ID}-add-rate-limiting.md`
const kb = '.knit/kb/docs/guide.md'
const design = '.knit/designs/web-ui-design.md'

const origins = {
  github: { web: 'https://github.com/acme/app', repoName: 'app' },
  gitea: { web: 'https://git.example.com/team/app', repoName: 'app' },
  gitlab: { web: 'https://gitlab.com/acme/app', repoName: 'app' },
  azure: { web: 'https://dev.azure.com/org/My%20Project/_git/app', repoName: 'app' },
}

const urls: Record<keyof typeof origins, (p: string) => string> = {
  github: (p) => `https://github.com/acme/app/blob/HEAD/${p}`,
  gitea: (p) => `https://git.example.com/team/app/src/branch/HEAD/${p}`,
  gitlab: (p) => `https://gitlab.com/acme/app/-/blob/HEAD/${p}`,
  azure: (p) => `https://dev.azure.com/org/My%20Project/_git/app?path=${encodeURIComponent('/' + p)}`,
}

const ok = (path: string) => ({ ok: true, path })

describe('resolvePastedLink', () => {
  for (const forge of Object.keys(origins) as (keyof typeof origins)[]) {
    it(`resolves a ${forge} URL for an item, a KB entry and a design`, () => {
      for (const p of [item, kb, design]) expect(resolvePastedLink(urls[forge](p), origins[forge])).toEqual(ok(p))
    })
  }

  it('resolves the repo-name form', () => {
    for (const p of [item, kb, design]) expect(resolvePastedLink(`app:${p}`, origins.github)).toEqual(ok(p))
  })

  it('drops a query, a fragment and a line anchor', () => {
    expect(resolvePastedLink(`${urls.github(kb)}?plain=1#L12`, origins.github)).toEqual(ok(kb))
    expect(resolvePastedLink(`${urls.azure(kb)}&version=GBmain&line=3`, origins.azure)).toEqual(ok(kb))
  })

  it('takes an item by the id in its file name, whatever the slug', () => {
    const r = resolvePastedLink(urls.github(`.knit/items/${ID}-old-title.md`), origins.github)
    expect(r.ok && resolveLink('', r.path)).toEqual({ kind: 'item', id: ID })
  })

  it('ignores case in host and owner, and decodes the Azure project name', () => {
    expect(resolvePastedLink(urls.github(kb).replace('github.com/acme/app', 'GitHub.com/ACME/App'), origins.github))
      .toEqual(ok(kb))
    expect(resolvePastedLink(urls.azure(kb).replace('My%20Project', 'my project'), origins.azure)).toEqual(ok(kb))
  })

  it('refuses another repo, undecodable text and two paths', () => {
    expect(resolvePastedLink('https://github.com/acme/other/blob/HEAD/' + kb, origins.github).ok).toBe(false)
    expect(resolvePastedLink('https://github.com/acme/application/blob/HEAD/' + kb, origins.github).ok).toBe(false)
    expect(resolvePastedLink(`other:${kb}`, origins.github).ok).toBe(false)
    expect(resolvePastedLink(`${urls.github(kb)}%E0%A4%A`, origins.github).ok).toBe(false)
    expect(resolvePastedLink(`${urls.github(kb)} ${urls.github(item)}`, origins.github).ok).toBe(false)
    expect(resolvePastedLink(urls.github(kb), { web: null, repoName: 'app' }).ok).toBe(false)
  })
})

describe('isPastedLink', () => {
  it('takes a URL or path form holding .knit/, not a plain path or ref', () => {
    expect(isPastedLink(urls.github(kb))).toBe(true)
    expect(isPastedLink(urls.azure(kb))).toBe(true)
    expect(isPastedLink(`app:${kb}`)).toBe(true)
    expect(isPastedLink(kb)).toBe(false)
    expect(isPastedLink('doc:guide')).toBe(false)
  })
})

describe('forgeCommands', () => {
  it('offers one config command per kind', () => {
    expect(forgeCommands('git.example.com')[2]).toEqual({ label: 'gitea', command: 'knit config set forges.git.example.com gitea' })
  })
})
