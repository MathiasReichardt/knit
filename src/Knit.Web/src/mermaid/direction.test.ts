import { describe, expect, it } from 'vitest'
import { readDirection, writeDirection } from './direction'

const FM = '---\nconfig:\n  flowchart:\n    curve: linear\n---\n'

describe('flowchart header', () => {
  it('reads the token after frontmatter, directives and comments', () => {
    expect(readDirection(FM + '%%{init: {}}%%\n%% note\n\nflowchart LR\n  A --> B', 'flowchart')).toBe('LR')
    expect(readDirection('graph RL;\n  A --> B', 'flowchart')).toBe('RL')
  })

  it('treats none, TB and TD as the default', () => {
    expect(readDirection('flowchart\n  A --> B', 'flowchart')).toBeUndefined()
    expect(readDirection('flowchart TB\n  A --> B', 'flowchart')).toBeUndefined()
    expect(readDirection('graph TD\n  A --> B', 'flowchart')).toBeUndefined()
  })

  it('is null without a flowchart header or with an unknown token', () => {
    expect(readDirection('sequenceDiagram\n  A->>B: hi', 'flowchart')).toBeNull()
    expect(readDirection('graph >\n  A --> B', 'flowchart')).toBeNull()
    expect(readDirection('flowchart-elk TD\n  A --> B', 'flowchart')).toBeNull()
  })

  it('replaces or adds the token and keeps the rest', () => {
    expect(writeDirection(FM + 'flowchart TD\n  A --> B', 'flowchart', 'LR')).toBe(FM + 'flowchart LR\n  A --> B')
    expect(writeDirection('  graph TD;\n  A --> B', 'flowchart', 'BT')).toBe('  graph BT;\n  A --> B')
    expect(writeDirection('flowchart\n  A --> B', 'flowchart', 'RL')).toBe('flowchart RL\n  A --> B')
  })

  it('restores an explicit TB, and leaves a header without a token alone', () => {
    expect(writeDirection('flowchart LR\n  A --> B', 'flowchart', undefined)).toBe('flowchart TB\n  A --> B')
    expect(writeDirection('flowchart\n  A --> B', 'flowchart', 'TB')).toBe('flowchart\n  A --> B')
  })

  it('refuses headers it does not edit', () => {
    expect(writeDirection('graph >\n  A --> B', 'flowchart', 'LR')).toBeNull()
  })
})

describe('timeline header', () => {
  it('reads TD, with LR the default', () => {
    expect(readDirection('timeline TD\n  2001 : a', 'timeline')).toBe('TD')
    expect(readDirection('timeline LR\n  2001 : a', 'timeline')).toBeUndefined()
    expect(readDirection('timeline\n  title T', 'timeline')).toBeUndefined()
  })

  it('adds TD and drops the token on restore', () => {
    expect(writeDirection('timeline\n  2001 : a', 'timeline', 'TD')).toBe('timeline TD\n  2001 : a')
    expect(writeDirection('timeline TD\n  2001 : a', 'timeline', undefined)).toBe('timeline\n  2001 : a')
  })
})

describe('direction statement', () => {
  const STATE = 'stateDiagram-v2\n  [*] --> A\n  state A {\n    direction LR\n    a --> b\n  }'

  it('ignores a statement inside a composite state', () => {
    expect(readDirection(STATE, 'statement')).toBeUndefined()
  })

  it('inserts a top-level statement after the header, in the body indent', () => {
    expect(writeDirection(STATE, 'statement', 'LR')).toBe(
      'stateDiagram-v2\n  direction LR\n  [*] --> A\n  state A {\n    direction LR\n    a --> b\n  }',
    )
  })

  it('reads and replaces an existing top-level statement', () => {
    const code = 'classDiagram\n  direction RL\n  A <|-- B'
    expect(readDirection(code, 'statement')).toBe('RL')
    expect(writeDirection(code, 'statement', 'BT')).toBe('classDiagram\n  direction BT\n  A <|-- B')
  })

  it('removes the statement on restore', () => {
    expect(writeDirection('classDiagram\n  direction RL\n  A <|-- B', 'statement', undefined)).toBe('classDiagram\n  A <|-- B')
    expect(writeDirection('classDiagram\n  A <|-- B', 'statement', 'TB')).toBe('classDiagram\n  A <|-- B')
  })
})
