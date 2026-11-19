import { describe, expect, it } from 'vitest'
import type { ItemSummary } from '../api/types'
import type { KbSummary } from '../api/kb'
import type { DesignSummary } from '../api/designs'
import { jumpMatches, type JumpPool } from './jumpMatches'

const item = (id: string, title: string, state: ItemSummary['state'] = 'todo'): ItemSummary =>
  ({ id, shortId: id.slice(0, 4), title, type: 'task', state }) as ItemSummary

const kb = (bucket: KbSummary['bucket'], slug: string, title: string): KbSummary =>
  ({ bucket, slug, ref: `${bucket}:${slug}`, title }) as KbSummary

const design = (slug: string, title: string): DesignSummary => ({ slug, title }) as DesignSummary

const pool: JumpPool = {
  items: [
    item('a3f91111', 'Wire the gripper'),
    item('b7c22222', 'Calibrate arm'),
    item('c0de3333', 'Arm firmware', 'done'),
  ],
  kb: [kb('adr', '0002-arm-protocol', 'Arm protocol'), kb('doc', 'guide', 'Operator guide')],
  designs: [design('web-ui', 'Web UI')],
}

const keys = (input: string) => jumpMatches(input, pool).map((m) => m.key)

describe('jumpMatches', () => {
  it('suggests nothing for blank input', () => {
    expect(jumpMatches('  ', pool)).toEqual([])
  })

  it('finds an item by short-id prefix', () => {
    expect(keys('a3')[0]).toBe('a3f91111')
  })

  it('ranks a title word start ahead of a substring hit', () => {
    expect(keys('arm').slice(0, 2)).toEqual(['b7c22222', 'adr:0002-arm-protocol'])
  })

  it('ranks closed items behind open ones on a tie', () => {
    const k = keys('arm')
    expect(k.indexOf('c0de3333')).toBeGreaterThan(k.indexOf('adr:0002-arm-protocol'))
  })

  it('matches every word anywhere', () => {
    expect(keys('guide oper')).toEqual(['doc:guide'])
  })

  it('narrows to a KB bucket on a bucket head', () => {
    expect(keys('adr:')).toEqual(['adr:0002-arm-protocol'])
    expect(keys('doc:gu')).toEqual(['doc:guide'])
  })

  it('narrows to designs on a design head', () => {
    expect(keys('design:web')).toEqual(['design:web-ui'])
  })

  it('treats an unknown head as plain text', () => {
    expect(keys('foo:bar')).toEqual([])
  })

  it('caps the list', () => {
    expect(jumpMatches('a', pool, 2)).toHaveLength(2)
  })
})
