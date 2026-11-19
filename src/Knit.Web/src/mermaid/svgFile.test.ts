import { describe, expect, it } from 'vitest'
import { applySvgTheme, formatSvgPointer, svgExportName, svgFileHit } from './svgFile'

const parse = (text: string) =>
  new DOMParser().parseFromString(text, 'image/svg+xml').documentElement as unknown as SVGSVGElement

const FILE = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
  <desc>Orders flow from the shop to billing.</desc>
  <g data-kind="group" id="backend" class="k-group">
    <title>Backend</title>
    <rect width="190" height="60" fill="#eee" stroke="#999"/>
    <g data-kind="node" id="shop" class="k-node"><title>Shop</title><rect width="40" height="24" fill="#fff"/><text>Shop</text></g>
    <g data-kind="node" id="billing" class="k-node k-accent"><title>Billing</title><rect width="40" height="24"/></g>
  </g>
  <path data-kind="edge" id="e1" data-from="shop" data-to="billing" class="k-edge" d="M48,20 L96,20"/>
  <circle cx="5" cy="5" r="2"/>
</svg>`

describe('applySvgTheme', () => {
  it('colors each role and modifier inline, and leaves unclassed elements alone', () => {
    const svg = parse(FILE)
    applySvgTheme(svg, 'dark')
    const style = (sel: string) => (svg.querySelector(sel) as SVGElement).style
    expect(style('#shop rect').fill).toBe('rgb(39, 39, 42)')
    expect(style('#shop text').fill).toBe('rgb(244, 244, 245)')
    expect(style('#billing rect').stroke).toBe('rgb(96, 165, 250)')
    expect(style('#backend > rect').stroke).toBe('rgb(82, 82, 91)')
    expect(style('#e1').fill).toBe('none')
    expect(style('#e1').stroke).toBe('rgb(161, 161, 170)')
    expect(svg.querySelector('circle')?.getAttribute('style')).toBeNull()
  })

  it('exports with the theme colors and no <style> element', () => {
    const svg = parse(FILE)
    applySvgTheme(svg, 'light')
    const text = svg.outerHTML
    expect(text).toContain('fill: rgb(244, 244, 245)')
    expect(text).not.toContain('<style')
  })
})

describe('svgFileHit', () => {
  const svg = parse(FILE)

  it('picks the nearest element with a data-kind and an id', () => {
    const hit = svgFileHit(svg.querySelector('#shop text')!, svg)!
    expect(hit.el.id).toBe('shop')
    expect(hit.pick).toEqual({ kind: 'node', dslId: 'shop', label: 'Shop' })
    expect(svgFileHit(svg.querySelector('#backend > rect')!, svg)?.pick.dslId).toBe('backend')
    expect(svgFileHit(svg.querySelector('#e1')!, svg)?.pick).toEqual({ kind: 'edge', dslId: 'e1' })
  })

  it('leaves the background unpicked', () => {
    expect(svgFileHit(svg.querySelector('circle')!, svg)).toBeUndefined()
  })
})

describe('formatSvgPointer', () => {
  it('names the file, the kind and the id', () => {
    expect(formatSvgPointer('doc:guide', 'order-flow.svg', { kind: 'node', dslId: 'shop', label: 'Shop' })).toBe(
      'doc:guide#order-flow.svg/node/shop',
    )
    expect(formatSvgPointer('doc:guide', 'order-flow.svg')).toBe('doc:guide#order-flow.svg')
  })

  it('names an export after the doc and the file', () => {
    expect(svgExportName('doc:guide', 'order-flow.svg', 'png')).toBe('doc-guide.order-flow.png')
  })
})
