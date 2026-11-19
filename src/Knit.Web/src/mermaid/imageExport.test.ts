import { describe, expect, it } from 'vitest'
import { exportFileName, pngScale, standaloneSvg } from './imageExport'

const SVG =
  '<svg id="m1" xmlns="http://www.w3.org/2000/svg" width="100%" viewBox="-8 -8 300 120" style="max-width: 300px;">' +
  '<foreignObject><div xmlns="http://www.w3.org/1999/xhtml"><span>a<br>b</span></div></foreignObject></svg>'

describe('standaloneSvg', () => {
  it('sizes the SVG from its viewBox and drops max-width', () => {
    const s = standaloneSvg(SVG, undefined)
    expect([s.width, s.height]).toEqual([300, 120])
    expect(s.text).toContain('width="300"')
    expect(s.text).toContain('height="120"')
    expect(s.text).not.toContain('max-width')
    expect(s.text).not.toContain('background')
  })

  it('writes well-formed XML from HTML labels', () => {
    const { text } = standaloneSvg(SVG, undefined)
    expect(new DOMParser().parseFromString(text, 'image/svg+xml').querySelector('parsererror')).toBeNull()
    expect(text).toContain('<br />')
  })

  it('sets the background', () => {
    expect(standaloneSvg(SVG, '#09090b').text).toMatch(/background-color: (#09090b|rgb\(9, 9, 11\))/)
  })

  it('refuses an SVG without a size', () => {
    expect(() => standaloneSvg('<svg xmlns="http://www.w3.org/2000/svg"></svg>', undefined)).toThrow(/no size/)
  })
})

describe('pngScale', () => {
  it('is 2x, less for a diagram the browser could not export at 2x', () => {
    expect(pngScale(800, 600)).toBe(2)
    expect(pngScale(10000, 100)).toBeCloseTo(1.6384)
    expect(pngScale(10000, 10000)).toBe(1)
  })
})

describe('exportFileName', () => {
  it('follows the block pointer', () => {
    expect(exportFileName('design:auth', 2, 'flowchart-v2', 'png')).toBe('design-auth.mermaid-2.flowchart.png')
    expect(exportFileName('kb:runbooks/deploy', 1, 'classDiagram', 'svg')).toBe('kb-runbooks-deploy.mermaid-1.classDiagram.svg')
  })

  it('names a diagram without a ref', () => {
    expect(exportFileName(undefined, 1, 'sequence', 'png')).toBe('diagram.mermaid-1.sequence.png')
  })
})
