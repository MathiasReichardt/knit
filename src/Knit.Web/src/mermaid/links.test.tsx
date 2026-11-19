import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { DiagramView } from './DiagramView'
import { standaloneSvg } from './imageExport'
import { parseLinks } from './links'

describe('parseLinks', () => {
  it('reads click and link lines, per id', () => {
    const code = [
      'flowchart LR',
      '  A[⇢ Checkout] --> B',
      '  click A href "design:shop#checkout"',
      '  click B,C "doc:guide"',
      '  link Order "adr:0007" "tooltip"',
      '  click D call show()',
      '  %% click E href "design:x"',
    ].join('\n')
    expect(parseLinks(code)).toEqual([
      { id: 'A', target: 'design:shop#checkout' },
      { id: 'B', target: 'doc:guide' },
      { id: 'C', target: 'doc:guide' },
      { id: 'Order', target: 'adr:0007' },
    ])
  })
})

// Trimmed mermaid 11.17 flowchart output: A is linked, mermaid wrapped it in an <a>.
const SVG = `<svg id="m1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
  <g class="nodes">
    <a xlink:href="design:shop#checkout" transform="translate(30, 40)"><g class="node default clickable" id="m1-flowchart-A-0"><rect class="basic"/></g></a>
    <g class="node default" id="m1-flowchart-B-1"><rect class="basic"/></g>
    <g class="node default" id="m1-flowchart-C-2"><rect class="basic"/></g>
  </g>
</svg>`

function setup() {
  const onPick = vi.fn()
  const onFollow = vi.fn()
  const onCopy = vi.fn()
  const rect = Element.prototype.getBoundingClientRect
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    if (this.id === 'm1-flowchart-A-0') return { left: 10, top: 20, right: 60, bottom: 40, width: 50, height: 20 } as DOMRect
    if (this.id === 'm1-flowchart-C-2') return { left: 150, top: 0, right: 200, bottom: 20, width: 50, height: 20 } as DOMRect
    if (this.classList.contains('origin-top-left')) return { left: 0, top: 0, right: 200, bottom: 100, width: 200, height: 100 } as DOMRect
    return rect.call(this)
  })
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(200)
  render(
    <DiagramView
      rendered={{ svg: SVG, diagramType: 'flowchart-v2' }}
      theme="light"
      docRef="item:abc"
      fence={1}
      onPick={onPick}
      links={[
        { id: 'A', target: 'design:shop#checkout' },
        { id: 'C', target: 'https://example.org' },
      ]}
      canFollow={(t) => t.startsWith('design:')}
      onFollow={onFollow}
      onCopy={onCopy}
    />,
  )
  return { onPick, onFollow, onCopy }
}

describe('DiagramView node links', () => {
  it('puts a badge at the linked node corner that follows the link', () => {
    const { onFollow, onPick } = setup()
    const badge = screen.getByRole('button', { name: 'Go to design:shop#checkout' })
    expect(badge.style.left).toBe('60px')
    expect(badge.style.top).toBe('20px')
    fireEvent.click(badge)
    expect(onFollow).toHaveBeenCalledWith('design:shop#checkout')
    expect(onPick).not.toHaveBeenCalled()
    vi.restoreAllMocks()
  })

  it('keeps a node click a pick, and the browser never follows mermaid’s <a>', () => {
    const { onPick, onFollow } = setup()
    expect(document.querySelector('#m1 a')).toBeNull()
    expect(document.querySelector('#m1-flowchart-A-0')!.parentElement!.getAttribute('transform')).toBe('translate(30, 40)')
    fireEvent.click(document.querySelector('#m1-flowchart-A-0 rect')!)
    expect(onPick).toHaveBeenCalledWith('item:abc#mermaid-1/node/A', 'A')
    expect(onFollow).not.toHaveBeenCalled()
    vi.restoreAllMocks()
  })

  it('copies a target it cannot follow, for the reader to open by hand', () => {
    const { onFollow, onCopy } = setup()
    const badge = screen.getByRole('button', { name: 'Copy "https://example.org": not a Knit ref, open it yourself' })
    fireEvent.click(badge)
    expect(onCopy).toHaveBeenCalledWith('https://example.org')
    expect(onFollow).not.toHaveBeenCalled()
    vi.restoreAllMocks()
  })

  it('pulls a badge on the top-right edge inside the diagram', () => {
    setup()
    const badge = screen.getByRole('button', { name: /^Copy "https:\/\/example.org"/ })
    expect(badge.style.left).toBe('192px')
    expect(badge.style.top).toBe('8px')
    vi.restoreAllMocks()
  })
})

describe('export', () => {
  it('drops mermaid’s links', () => {
    const { text } = standaloneSvg(SVG, undefined)
    expect(text).not.toContain('<a')
    expect(text).not.toContain('design:shop')
    expect(text).toContain('m1-flowchart-A-0')
  })
})
