import { describe, expect, it } from 'vitest'
import { layoutOptions, optionKey, parseNumberOption, writeOption, type NumberOption } from './layoutOptions'

describe('layoutOptions', () => {
  it('has options for the picked diagram types', () => {
    for (const t of ['flowchart-v2', 'stateDiagram', 'classDiagram', 'sequence', 'mindmap', 'timeline', 'block', 'architecture'])
      expect(layoutOptions(t).length).toBeGreaterThan(0)
  })

  it('offers the keys the block and architecture renderers read', () => {
    expect(layoutOptions('block').map(optionKey)).toEqual(['block.padding'])
    expect(layoutOptions('architecture').map(optionKey)).toEqual([
      'architecture.nodeSeparation',
      'architecture.padding',
      'architecture.iconSize',
      'architecture.fontSize',
      'architecture.idealEdgeLengthMultiplier',
      'architecture.edgeElasticity',
    ])
  })

  it('has none for other types', () => {
    expect(layoutOptions('pie')).toEqual([])
    expect(layoutOptions('gantt')).toEqual([])
  })
})

describe('writeOption', () => {
  const [direction, curve] = layoutOptions('flowchart-v2')

  it('removes the key when the default is picked', () => {
    const code = '---\nconfig:\n  flowchart:\n    curve: linear\n---\nflowchart LR\n  A --> B'
    expect(writeOption(code, curve!, 'basis')).toBe('flowchart LR\n  A --> B')
  })

  it('writes a header option to the header line', () => {
    expect(writeOption('flowchart LR\n  A --> B', direction!, 'TB')).toBe('flowchart TB\n  A --> B')
    expect(writeOption('flowchart LR\n  A --> B', direction!, 'RL')).toBe('flowchart RL\n  A --> B')
  })

  it('writes each block and architecture key to the frontmatter', () => {
    for (const header of ['block-beta', 'architecture-beta']) {
      for (const o of layoutOptions(header.replace('-beta', ''))) {
        const value = o.kind === 'float' ? 0.75 : 20
        const [section, key] = o.path
        expect(writeOption(`${header}\n  a`, o, value)).toBe(
          `---\nconfig:\n  ${section}:\n    ${key}: ${value}\n---\n${header}\n  a`,
        )
      }
    }
  })
})

describe('parseNumberOption', () => {
  const find = (type: string, key: string) => layoutOptions(type).find((x) => optionKey(x) === key) as NumberOption
  const o = find('flowchart-v2', 'flowchart.nodeSpacing')

  it('accepts a whole number in range', () => {
    expect(parseNumberOption(o, ' 120 ')).toEqual({ value: 120 })
    expect(parseNumberOption(o, '10')).toEqual({ value: 10 })
  })

  it('rejects non-integers and out-of-range values', () => {
    expect(parseNumberOption(o, '12.5')).toHaveProperty('error')
    expect(parseNumberOption(o, 'abc')).toHaveProperty('error')
    expect(parseNumberOption(o, '')).toHaveProperty('error')
    expect(parseNumberOption(o, '9')).toEqual({ error: '10–300' })
    expect(parseNumberOption(o, '301')).toEqual({ error: '10–300' })
  })

  it('takes a fraction for a float option', () => {
    const f = find('architecture', 'architecture.edgeElasticity')
    expect(parseNumberOption(f, '0.3')).toEqual({ value: 0.3 })
    expect(parseNumberOption(f, '1')).toEqual({ value: 1 })
    expect(parseNumberOption(f, '0,3')).toEqual({ error: 'Number' })
    expect(parseNumberOption(f, '2')).toEqual({ error: '0.05–1' })
  })
})
