import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { DiagramView } from './DiagramView'
import type { LayoutModel } from './LayoutMenu'
import { layoutOptions } from './layoutOptions'

const SVG = '<svg id="m1" xmlns="http://www.w3.org/2000/svg"><g class="nodes"></g></svg>'

function setup(model: Partial<LayoutModel> = {}, diagramType = 'flowchart-v2') {
  const apply = vi.fn()
  const layout: LayoutModel = {
    options: layoutOptions(diagramType),
    values: {},
    directive: {},
    busy: false,
    apply,
    ...model,
  }
  render(<DiagramView rendered={{ svg: SVG, diagramType }} theme="light" fence={1} layout={layout} />)
  return { apply }
}

const openMenu = () => fireEvent.click(screen.getByRole('button', { name: 'Layout options' }))

describe('LayoutMenu', () => {
  it('shows no menu button for a type without options', () => {
    setup({ options: [] }, 'pie')
    expect(screen.queryByRole('button', { name: 'Layout options' })).toBeNull()
  })

  it('marks the active line style and applies another', () => {
    const { apply } = setup({ values: { 'flowchart.curve': 'linear' } })
    openMenu()
    expect(screen.getByRole('button', { name: 'linear' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'basis' }).getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(screen.getByRole('button', { name: 'stepAfter' }))
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ path: ['flowchart', 'curve'] }), 'stepAfter')
  })

  it('applies a valid number and shows why an invalid one is not applied', () => {
    const { apply } = setup()
    openMenu()
    const input = screen.getByLabelText('Node spacing')
    fireEvent.change(input, { target: { value: '500' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(apply).not.toHaveBeenCalled()
    expect(screen.getByText('10–300')).toBeTruthy()
    expect(input.getAttribute('aria-invalid')).toBe('true')
    fireEvent.change(input, { target: { value: '120' } })
    fireEvent.blur(input)
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ path: ['flowchart', 'nodeSpacing'] }), 120)
  })

  it('shows restore default only where a value is set, and restores by removing the key', () => {
    const { apply } = setup({ values: { 'flowchart.padding': 30, 'flowchart.nodeSpacing': 50 } })
    openMenu()
    const restores = screen.getAllByRole('button', { name: /^Restore default/ })
    expect(restores.filter((b) => !b.classList.contains('invisible'))).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Restore default Node padding' }))
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ path: ['flowchart', 'padding'] }), undefined)
  })

  it('offers the flowchart direction, TB by default', () => {
    const { apply } = setup()
    openMenu()
    const group = screen.getByRole('group', { name: 'Direction' })
    expect(within(group).getByRole('button', { name: 'Top to bottom' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(within(group).getByRole('button', { name: 'Left to right' }))
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ path: ['direction'], direction: 'flowchart' }), 'LR')
  })

  it('locks an option a legacy directive sets', () => {
    setup({ directive: { 'flowchart.curve': 'linear' } })
    openMenu()
    expect((screen.getByRole('button', { name: 'basis' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText(/%%\{init\}%% directive/)).toBeTruthy()
  })

  it('closes on Esc before the overlay does', () => {
    setup()
    fireEvent.click(screen.getByRole('button', { name: 'Maximize' }))
    const overlay = screen.getByRole('dialog', { name: 'Diagram' })
    fireEvent.click(within(overlay).getByRole('button', { name: 'Layout options' }))
    expect(document.querySelector('[data-diagram-popover]')).not.toBeNull()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(document.querySelector('[data-diagram-popover]')).toBeNull()
    expect(screen.getByRole('dialog', { name: 'Diagram' })).toBeTruthy()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByRole('dialog', { name: 'Diagram' })).toBeNull()
  })
})
