import { describe, expect, it } from 'vitest'
import { replaceFence, splitChunks } from './fence'

const BODY = '# Doc\n\n```mermaid\nflowchart TD\n  A --> B\n```\n\ntext\n\n```mermaid\nsequenceDiagram\n  A->>B: hi\n```\n'

describe('replaceFence', () => {
  it('replaces only the code of the given fence', () => {
    const next = replaceFence(BODY, 2, '---\nconfig:\n  sequence:\n    wrap: true\n---\nsequenceDiagram\n  A->>B: hi')
    expect(next).toBe(BODY.replace('```mermaid\nsequenceDiagram', '```mermaid\n---\nconfig:\n  sequence:\n    wrap: true\n---\nsequenceDiagram'))
    expect(splitChunks(next).filter((c) => c.kind === 'mermaid')).toHaveLength(2)
  })

  it('leaves the body alone for a missing fence', () => {
    expect(replaceFence(BODY, 3, 'x')).toBe(BODY)
  })
})

describe('splitChunks', () => {
  it('splits out an image link to an SVG next to the doc', () => {
    const body = 'intro\n\n![Order flow](order-flow.svg)\n\n![Here](./b.svg)\nmore'
    expect(splitChunks(body).map((c) => [c.kind, c.text, c.alt ?? null, c.line])).toEqual([
      ['md', 'intro\n\n', null, 1],
      ['svg', 'order-flow.svg', 'Order flow', 3],
      ['md', '\n\n', null, 3],
      ['svg', 'b.svg', 'Here', 5],
      ['md', '\nmore', null, 5],
    ])
  })

  it('leaves an SVG in a folder, inline in text, or inside a code fence as markdown', () => {
    for (const body of ['![x](img/a.svg)', 'see ![x](a.svg) here', '```md\n![x](a.svg)\n```', '![x](https://x.test/a.svg)'])
      expect(splitChunks(body).map((c) => c.kind)).toEqual(['md'])
  })

  it('gives each chunk its start line', () => {
    expect(splitChunks(BODY).map((c) => [c.kind, c.line])).toEqual([
      ['md', 1],
      ['mermaid', 3],
      ['md', 6],
      ['mermaid', 10],
      ['md', 13],
    ])
  })
})
