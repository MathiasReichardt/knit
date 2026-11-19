import { describe, expect, it } from 'vitest'
import { sanitizeSvg } from './sanitizeSvg'

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

describe('sanitizeSvg', () => {
  it('keeps the drawing and its data attributes', () => {
    const svg = sanitizeSvg(FILE)!
    expect(svg.getAttribute('viewBox')).toBe('0 0 200 100')
    expect(svg.querySelector('#shop')?.getAttribute('data-kind')).toBe('node')
    expect(svg.querySelector('#e1')?.getAttribute('data-from')).toBe('shop')
    expect(svg.querySelector('desc')?.textContent).toContain('Orders flow')
  })

  it.each([
    ['<script>alert(1)</script>', 'script'],
    ['<style>* { color: red }</style>', 'style'],
    ['<foreignObject><div>x</div></foreignObject>', 'foreignObject'],
  ])('strips %s', (inner, tag) => {
    const svg = sanitizeSvg(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1">${inner}<rect/></svg>`)!
    expect(svg.getElementsByTagName(tag)).toHaveLength(0)
    expect(svg.querySelector('rect')).not.toBeNull()
  })

  it('strips event handlers and external hrefs, keeps in-file ones', () => {
    const svg = sanitizeSvg(`<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
      <rect id="r" onclick="alert(1)"/>
      <a id="out" href="https://x.test/"><text>x</text></a>
      <use id="ext" xlink:href="other.svg#a"/>
      <a id="in" href="#r"><text>y</text></a>
    </svg>`)!
    expect(svg.querySelector('#r')?.hasAttribute('onclick')).toBe(false)
    expect(svg.querySelector('#out')?.hasAttribute('href')).toBe(false)
    expect(svg.querySelector('use')).toBeNull()
    expect(svg.querySelector('#in')?.getAttribute('href')).toBe('#r')
  })

  it('returns null for text that holds no SVG', () => {
    expect(sanitizeSvg('<p>no</p>')).toBeNull()
  })
})
