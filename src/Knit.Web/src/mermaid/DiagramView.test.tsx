import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { DiagramView } from './DiagramView'

// Trimmed mermaid 11.17 flowchart output: one node and one edge.
const SVG = `<svg id="m1" xmlns="http://www.w3.org/2000/svg">
  <g class="edgePaths"><path id="m1-L_A_B_0" data-et="edge" data-id="L_A_B_0" d="M0,0L10,10"/></g>
  <g class="nodes">
    <g class="node default" id="m1-flowchart-A-0"><rect class="basic"/></g>
    <g class="node default" id="m1-flowchart-B-1"><rect class="basic"/></g>
  </g>
</svg>`

function setup() {
  const onPick = vi.fn()
  render(
    <DiagramView
      rendered={{ svg: SVG, diagramType: 'flowchart-v2' }}
      theme="light"
      docRef="item:abc"
      fence={2}
      onPick={onPick}
    />,
  )
  const pickNode = () => fireEvent.click(document.querySelector('#m1-flowchart-A-0 rect')!)
  const pickEdge = () => fireEvent.click(document.querySelector('#m1-L_A_B_0')!)
  return { onPick, pickNode, pickEdge }
}

describe('DiagramView maximize', () => {
  it('moves the one SVG to the overlay and back, picking the same pointers', () => {
    const { onPick, pickNode, pickEdge } = setup()
    pickNode()
    pickEdge()
    const inline = onPick.mock.calls.map((c) => c[0])
    expect(inline[0]).toContain('item:abc#mermaid-2/node/A')

    fireEvent.click(screen.getByRole('button', { name: 'Maximize' }))
    const dialog = screen.getByRole('dialog')
    expect(document.querySelectorAll('#m1')).toHaveLength(1)
    expect(dialog.contains(document.querySelector('#m1'))).toBe(true)
    pickNode()
    pickEdge()

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.querySelectorAll('#m1')).toHaveLength(1)
    pickNode()
    pickEdge()

    expect(onPick.mock.calls.map((c) => c[0])).toEqual([...inline, ...inline, ...inline])
  })

  it('closes with the Close button and gives focus back to maximize', () => {
    setup()
    const maximize = screen.getByRole('button', { name: 'Maximize' })
    fireEvent.click(maximize)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(maximize)
  })

  it('keeps the edge hit areas across the move', () => {
    setup()
    expect(document.querySelectorAll('.mermaid-edge-hit')).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Maximize' }))
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(document.querySelectorAll('.mermaid-edge-hit')).toHaveLength(1)
  })})

describe('DiagramView clicks', () => {
  const background = () => fireEvent.click(document.querySelector('#m1')!)

  it('maximizes on a background click, not on an element click', () => {
    const { onPick, pickNode } = setup()
    pickNode()
    expect(screen.queryByRole('dialog')).toBeNull()
    background()
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(onPick).toHaveBeenCalledTimes(1)
  })

  it('stays maximized on a background click', () => {
    setup()
    background()
    background()
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('keeps the usage hints in the help popover, not on the diagram', () => {
    setup()
    expect(document.querySelector('#m1')!.closest('[title]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Diagram help' }))
    const help = screen.getByRole('dialog', { name: 'Diagram help' })
    expect(help.textContent).toContain('Click the background to maximize')
  })

  it('only closes an open popover on a diagram click', () => {
    const { onPick, pickNode } = setup()
    const node = document.querySelector('#m1-flowchart-A-0 rect')!
    fireEvent.click(screen.getByRole('button', { name: 'Diagram help' }))
    fireEvent.pointerDown(node)
    fireEvent.click(node)
    expect(screen.queryByRole('dialog', { name: 'Diagram help' })).toBeNull()
    expect(onPick).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Diagram help' }))
    fireEvent.pointerDown(document.querySelector('#m1')!)
    background()
    expect(screen.queryByRole('dialog')).toBeNull()

    pickNode()
    expect(onPick).toHaveBeenCalledTimes(1)
  })

  it('copies the whole-diagram pointer from its button', () => {
    const { onPick } = setup()
    fireEvent.click(screen.getByRole('button', { name: 'Copy diagram pointer' }))
    expect(onPick).toHaveBeenCalledWith('item:abc#mermaid-2(flowchart)', 'flowchart diagram')
  })
})
