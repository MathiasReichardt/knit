import { describe, expect, it } from 'vitest'
import { addEdgeHitAreas, HIT_CLASS, pickTarget } from './hitArea'

function mount(inner: string): SVGSVGElement {
  document.body.innerHTML = `<svg id="m1" xmlns="http://www.w3.org/2000/svg">${inner}</svg>`
  return document.querySelector('svg')!
}

describe('addEdgeHitAreas', () => {
  it('puts a geometry-only wide copy after each edge and message line', () => {
    const svg = mount(`
      <path id="m1-L_A_B_0" class="flowchart-link" data-et="edge" data-id="L_A_B_0" d="M0,0L10,10" marker-end="url(#x)"/>
      <line data-et="message" data-from="A" data-to="B" x1="0" y1="5" x2="50" y2="5"/>
      <g data-et="participant" data-id="A"><rect/></g>`)
    addEdgeHitAreas(svg)
    const hits = svg.querySelectorAll(`.${HIT_CLASS}`)
    expect(hits).toHaveLength(2)
    const [edgeHit, msgHit] = [hits[0]!, hits[1]!]
    expect(edgeHit.previousElementSibling?.id).toBe('m1-L_A_B_0')
    expect(edgeHit.getAttribute('d')).toBe('M0,0L10,10')
    expect(edgeHit.hasAttribute('id') || edgeHit.hasAttribute('data-et') || edgeHit.hasAttribute('marker-end')).toBe(false)
    expect(msgHit.tagName).toBe('line')
    expect(msgHit.getAttribute('x2')).toBe('50')
  })

  it('covers an SVG file edge, and draws no marker a parent group passes down', () => {
    const svg = mount(`
      <g marker-end="url(#arrow)">
        <path data-kind="edge" id="a" d="M0,0 H10"/>
        <g data-kind="edge" id="b"><polyline points="0,0 5,5"/></g>
      </g>`)
    addEdgeHitAreas(svg)
    const hits = Array.from(svg.querySelectorAll<SVGElement>(`.${HIT_CLASS}`))
    expect(hits.map((h) => h.previousElementSibling?.id || h.previousElementSibling?.tagName)).toEqual(['a', 'polyline'])
    expect(hits[0]!.getAttribute('style')).toContain('marker: none')
  })

  it('is idempotent', () => {
    const svg = mount(`<path data-et="edge" data-id="e" d="M0,0"/>`)
    addEdgeHitAreas(svg)
    addEdgeHitAreas(svg)
    expect(svg.querySelectorAll(`.${HIT_CLASS}`)).toHaveLength(1)
  })
})

describe('pickTarget', () => {
  it('maps a hit copy back to its line and leaves other targets alone', () => {
    const svg = mount(`<path data-et="edge" data-id="e" d="M0,0"/><rect/>`)
    addEdgeHitAreas(svg)
    expect(pickTarget(svg.querySelector(`.${HIT_CLASS}`)!)).toBe(svg.querySelector('path'))
    expect(pickTarget(svg.querySelector('rect')!)).toBe(svg.querySelector('rect'))
  })
})
