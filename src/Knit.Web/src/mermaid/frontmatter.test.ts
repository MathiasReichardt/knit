import { describe, expect, it } from 'vitest'
import { readConfig, writeConfig } from './frontmatter'

const DIAGRAM = 'flowchart TD\n  A --> B'

describe('writeConfig', () => {
  it('adds a frontmatter block to a fence without one', () => {
    expect(writeConfig(DIAGRAM, ['flowchart', 'curve'], 'linear')).toBe(
      '---\nconfig:\n  flowchart:\n    curve: linear\n---\n' + DIAGRAM,
    )
  })

  it('merges into an existing block and keeps title, other keys and comments', () => {
    const code = '---\ntitle: Flow\n# layout\nconfig:\n  theme: base\n  flowchart:\n    nodeSpacing: 80\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], 'linear')).toBe(
      '---\ntitle: Flow\n# layout\nconfig:\n  theme: base\n  flowchart:\n    nodeSpacing: 80\n    curve: linear\n---\n' + DIAGRAM,
    )
  })

  it('adds config under a block that has only a title', () => {
    expect(writeConfig('---\ntitle: Flow\n---\n' + DIAGRAM, ['nodeSpacing'], 70)).toBe(
      '---\ntitle: Flow\nconfig:\n  nodeSpacing: 70\n---\n' + DIAGRAM,
    )
  })

  it('replaces a value in place and follows the existing indent', () => {
    const code = '---\nconfig:\n    flowchart:\n        curve: basis\n        padding: 4\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], 'stepAfter')).toBe(
      '---\nconfig:\n    flowchart:\n        curve: stepAfter\n        padding: 4\n---\n' + DIAGRAM,
    )
  })

  it('removes a key and keeps its siblings', () => {
    const code = '---\nconfig:\n  flowchart:\n    curve: linear\n    padding: 4\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], undefined)).toBe(
      '---\nconfig:\n  flowchart:\n    padding: 4\n---\n' + DIAGRAM,
    )
  })

  it('drops emptied maps but keeps the title', () => {
    const code = '---\ntitle: Flow\nconfig:\n  flowchart:\n    curve: linear\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], undefined)).toBe('---\ntitle: Flow\n---\n' + DIAGRAM)
  })

  it('drops the whole block once it is empty', () => {
    const code = '---\nconfig:\n  flowchart:\n    curve: linear\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], undefined)).toBe(DIAGRAM)
  })

  it('leaves a legacy init directive and the diagram text untouched', () => {
    const body = '%%{init: {"flowchart": {"curve": "linear"}}}%%\n' + DIAGRAM
    expect(writeConfig(body, ['flowchart', 'padding'], 20)).toBe(
      '---\nconfig:\n  flowchart:\n    padding: 20\n---\n' + body,
    )
  })

  it('is a no-op when removing an unset key', () => {
    expect(writeConfig(DIAGRAM, ['flowchart', 'curve'], undefined)).toBe(DIAGRAM)
    const code = '---\ntitle: Flow\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], undefined)).toBe(code)
  })

  it('writes booleans and numbers plain, odd strings quoted', () => {
    expect(writeConfig(DIAGRAM, ['sequence', 'mirrorActors'], false)).toContain('mirrorActors: false\n')
    expect(writeConfig(DIAGRAM, ['x'], 'a b')).toContain('x: "a b"\n')
    expect(writeConfig(DIAGRAM, ['x'], 'true')).toContain('x: "true"\n')
  })

  it('refuses forms it does not edit', () => {
    expect(writeConfig('---\nconfig: {flowchart: {curve: linear}}\n---\n' + DIAGRAM, ['flowchart', 'curve'], 'basis')).toBeNull()
    expect(writeConfig('---\nconfig:\n  flowchart:\n    - a\n---\n' + DIAGRAM, ['flowchart', 'curve'], 'basis')).toBeNull()
    expect(writeConfig('---\nconfig:\n  flowchart: basis\n---\n' + DIAGRAM, ['flowchart', 'curve'], 'basis')).toBeNull()
    expect(writeConfig('---\nconfig:\n' + DIAGRAM, ['flowchart', 'curve'], 'basis')).toBeNull()
  })

  it('edits around a flow-style sibling it does not touch', () => {
    const code = '---\nconfig:\n  themeVariables: {primaryColor: "#fff"}\n---\n' + DIAGRAM
    expect(writeConfig(code, ['flowchart', 'curve'], 'linear')).toBe(
      '---\nconfig:\n  themeVariables: {primaryColor: "#fff"}\n  flowchart:\n    curve: linear\n---\n' + DIAGRAM,
    )
  })
})

describe('readConfig', () => {
  const code = '---\nconfig:\n  nodeSpacing: 70\n  flowchart:\n    curve: "stepAfter" # tweak\n    wrap: true\n---\n' + DIAGRAM

  it('reads scalars on the path', () => {
    expect(readConfig(code, ['flowchart', 'curve'])).toBe('stepAfter')
    expect(readConfig(code, ['flowchart', 'wrap'])).toBe(true)
    expect(readConfig(code, ['nodeSpacing'])).toBe(70)
  })

  it('is undefined when unset or without frontmatter', () => {
    expect(readConfig(code, ['flowchart', 'padding'])).toBeUndefined()
    expect(readConfig(DIAGRAM, ['flowchart', 'curve'])).toBeUndefined()
  })

  it('is null for a form it does not edit', () => {
    expect(readConfig('---\nconfig: {flowchart: {curve: linear}}\n---\n' + DIAGRAM, ['flowchart', 'curve'])).toBeNull()
  })
})
