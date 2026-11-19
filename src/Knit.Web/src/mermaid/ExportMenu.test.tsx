import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { DiagramView } from './DiagramView'
import type { ExportModel } from './ExportMenu'
import type { LayoutModel } from './LayoutMenu'
import { layoutOptions } from './layoutOptions'

const SVG = '<svg id="m1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 20"><g class="nodes"></g></svg>'

const model = (): ExportModel => ({
  render: vi.fn(async () => SVG),
  background: { light: '#ffffff', dark: '#09090b' },
  fileName: (format) => `d.${format}`,
  notify: vi.fn(),
})

function setup(theme: 'light' | 'dark' = 'light') {
  const m = model()
  render(<DiagramView rendered={{ svg: SVG, diagramType: 'flowchart-v2' }} theme={theme} fence={1} exportImage={m} />)
  fireEvent.click(screen.getByRole('button', { name: 'Export image' }))
  return m
}

const pressed = (group: string) =>
  screen.getByRole('group', { name: group }).querySelector('[aria-pressed="true"]')?.textContent

afterEach(() => vi.restoreAllMocks())

describe('ExportMenu', () => {
  it('preselects PNG and the current theme, with an opaque background', () => {
    setup('dark')
    expect(pressed('Format')).toBe('PNG')
    expect(pressed('Theme')).toBe('Dark')
    expect((screen.getByRole('checkbox', { name: 'Transparent background' }) as HTMLInputElement).checked).toBe(false)
    expect(screen.getByRole('button', { name: 'Copy' })).toBeTruthy()
  })

  it('offers no copy for SVG', () => {
    setup()
    fireEvent.click(screen.getByRole('button', { name: 'SVG' }))
    expect(screen.queryByRole('button', { name: 'Copy' })).toBeNull()
  })

  it('downloads a transparent SVG without a background', async () => {
    setup()
    const blobs: Blob[] = []
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => (blobs.push(b as Blob), 'blob:x'))
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    fireEvent.click(screen.getByRole('button', { name: 'SVG' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Transparent background' }))
    fireEvent.click(screen.getByRole('button', { name: 'Download' }))
    await waitFor(() => expect(click).toHaveBeenCalled())
    expect(await blobs[0]!.text()).not.toContain('background-color')
  })

  it('downloads an SVG in the chosen theme and closes', async () => {
    const m = setup()
    const blobs: Blob[] = []
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => (blobs.push(b as Blob), 'blob:x'))
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe('d.svg')
    })
    fireEvent.click(screen.getByRole('button', { name: 'SVG' }))
    fireEvent.click(screen.getByRole('button', { name: 'Dark' }))
    fireEvent.click(screen.getByRole('button', { name: 'Download' }))
    await waitFor(() => expect(click).toHaveBeenCalled())
    expect(m.render).toHaveBeenCalledWith('dark')
    expect(await blobs[0]!.text()).toMatch(/^<\?xml[^]*background-color/)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Export image' })).toBeNull())
  })

  it('reports a failed export and stays open', async () => {
    const m = setup()
    vi.mocked(m.render).mockRejectedValueOnce(new Error('boom'))
    fireEvent.click(screen.getByRole('button', { name: 'SVG' }))
    fireEvent.click(screen.getByRole('button', { name: 'Download' }))
    await waitFor(() => expect(m.notify).toHaveBeenCalledWith('error', 'Diagram not exported: boom'))
    expect(screen.getByRole('dialog', { name: 'Export image' })).toBeTruthy()
  })

  it('shows no export button without a model', () => {
    render(<DiagramView rendered={{ svg: SVG, diagramType: 'flowchart-v2' }} theme="light" fence={1} />)
    expect(screen.queryByRole('button', { name: 'Export image' })).toBeNull()
  })

  it('closes the layout menu when it opens', () => {
    const layout: LayoutModel = { options: layoutOptions('flowchart-v2'), values: {}, directive: {}, busy: false, apply: vi.fn() }
    render(<DiagramView rendered={{ svg: SVG, diagramType: 'flowchart-v2' }} theme="light" fence={1} layout={layout} exportImage={model()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Layout options' }))
    fireEvent.click(screen.getByRole('button', { name: 'Export image' }))
    expect(screen.queryByRole('dialog', { name: 'Layout options' })).toBeNull()
    expect(screen.getByRole('dialog', { name: 'Export image' })).toBeTruthy()
  })
})
