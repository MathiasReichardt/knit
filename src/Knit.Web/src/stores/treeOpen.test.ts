import { beforeEach, describe, expect, it } from 'vitest'
import { repoKey, useTreeOpenStore } from './treeOpen'

const reset = () => useTreeOpenStore.setState({ repo: null, byRepo: {} })
const ids = (tree: 'items' | 'kb' | 'projects', repo = 'r1') =>
  useTreeOpenStore.getState().byRepo[repo]?.[tree] ?? []

describe('repoKey', () => {
  it('normalizes separators, trailing slashes and case so one repo is one key', () => {
    expect(repoKey('C:\\data\\dev\\Knit')).toBe('c:/data/dev/knit')
    expect(repoKey('C:/DATA/dev/knit/')).toBe('c:/data/dev/knit')
    expect(repoKey('/home/me/knit')).toBe('/home/me/knit')
  })
})

describe('treeOpen store', () => {
  beforeEach(reset)

  it('drops writes until a repo key is set, so nothing lands unscoped', () => {
    useTreeOpenStore.getState().toggle('items', 'a')
    expect(useTreeOpenStore.getState().byRepo).toEqual({})
  })

  it('toggles a node on and off', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.toggle('items', 'a')
    expect(ids('items')).toEqual(['a'])
    useTreeOpenStore.getState().toggle('items', 'a')
    expect(ids('items')).toEqual([])
  })

  it('keeps each tree in its own namespace, so one id in two trees stays apart', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.toggle('items', 'shared')
    expect(ids('items')).toEqual(['shared'])
    expect(ids('projects')).toEqual([])
  })

  it('keys state per repo, so two repos on one port never bleed', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.toggle('items', 'a')
    useTreeOpenStore.getState().setRepo('r2')
    expect(ids('items', 'r2')).toEqual([])
    useTreeOpenStore.getState().toggle('items', 'b')
    expect(ids('items', 'r1')).toEqual(['a'])
    expect(ids('items', 'r2')).toEqual(['b'])
  })

  it('replace overwrites (collapse all clears, expand all sets) and dedupes', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.replace('kb', ['', 'adr', 'adr', 'doc'])
    expect(ids('kb')).toEqual(['', 'adr', 'doc'])
    useTreeOpenStore.getState().replace('kb', [])
    expect(ids('kb')).toEqual([])
  })

  it('expand is additive and a no-op once every id is open', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.replace('items', ['a'])
    const before = ids('items')
    useTreeOpenStore.getState().expand('items', ['a'])
    // Same array identity: an unchanged expand must not re-render the tree.
    expect(ids('items')).toBe(before)
    useTreeOpenStore.getState().expand('items', ['a', 'b'])
    expect(ids('items')).toEqual(['a', 'b'])
  })

  it('prune keeps only live ids, and leaves the array alone when all survive', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.replace('items', ['a', 'b', 'gone'])
    useTreeOpenStore.getState().prune('items', new Set(['a', 'b', 'gone']))
    expect(ids('items')).toEqual(['a', 'b', 'gone'])
    useTreeOpenStore.getState().prune('items', new Set(['a', 'b']))
    expect(ids('items')).toEqual(['a', 'b'])
  })

  it('prunes only the tree it is given', () => {
    const s = useTreeOpenStore.getState()
    s.setRepo('r1')
    s.replace('items', ['a'])
    useTreeOpenStore.getState().replace('kb', ['a'])
    useTreeOpenStore.getState().prune('items', new Set())
    expect(ids('items')).toEqual([])
    expect(ids('kb')).toEqual(['a'])
  })
})
