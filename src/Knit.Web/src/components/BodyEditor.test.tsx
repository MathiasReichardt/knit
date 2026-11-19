import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EditorView } from '@codemirror/view'
import { BodyEditor } from './BodyEditor'
import { useOutlinePrefs } from '../stores/outlinePrefs'

const BODY = '## Goals\n\ntext\n\n## Risks\n\ntext\n'

function setup(onCancel = vi.fn(), minHeadings?: number) {
  const { container } = render(
    <BodyEditor initialValue={BODY} onSave={() => {}} onCancel={onCancel} outline={{ docRef: 'design:web', minHeadings }} />,
  )
  const view = EditorView.findFromDOM(container.querySelector<HTMLElement>('.cm-editor')!)!
  return { view, onCancel }
}

const row = (name: string) => screen.getByRole('button', { name })
const cursorLine = (view: EditorView) => view.state.doc.lineAt(view.state.selection.main.head).number
const press = (target: Element, init: KeyboardEventInit) =>
  act(() => {
    fireEvent.keyDown(target, init)
  })

beforeEach(() => {
  useOutlinePrefs.setState({ pinned: false })
  // jsdom has no layout; CodeMirror measures through these.
  const rect = () => new DOMRect(0, 0, 0, 0)
  Range.prototype.getBoundingClientRect = rect
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} }))
  if (typeof CSS === 'undefined' || !CSS.escape) {
    vi.stubGlobal('CSS', { escape: (s: string) => s.replace(/[^\w-]/g, (c) => `\\${c}`) })
  }
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('BodyEditor outline', () => {
  it('follows the edits', async () => {
    const { view } = setup()
    expect(row('Goals')).toBeTruthy()
    act(() => view.dispatch({ changes: { from: view.state.doc.length, insert: '\n## Scope\n' } }))
    await waitFor(() => expect(row('Scope')).toBeTruthy())
  })

  it('Ctrl+Shift+O opens it; ↓ puts the cursor on the heading; Esc returns to the editor there', () => {
    const { view, onCancel } = setup()
    press(view.contentDOM, { key: 'O', code: 'KeyO', ctrlKey: true, shiftKey: true })
    expect(document.activeElement).toBe(row('Goals'))
    press(row('Goals'), { key: 'ArrowDown' })
    expect(document.activeElement).toBe(row('Risks'))
    expect(cursorLine(view)).toBe(5)
    press(row('Risks'), { key: 'Escape' })
    expect(view.contentDOM.contains(document.activeElement)).toBe(true)
    expect(cursorLine(view)).toBe(5)
    expect(onCancel).not.toHaveBeenCalled()
  })

  it('waits for `minHeadings`', async () => {
    const { view } = setup(vi.fn(), 3)
    expect(screen.queryByRole('navigation', { name: 'Outline' })).toBeNull()
    act(() => view.dispatch({ changes: { from: view.state.doc.length, insert: '\n## Scope\n' } }))
    await waitFor(() => expect(row('Scope')).toBeTruthy())
  })

  it('Alt+O opens it too', () => {
    const { view } = setup()
    press(view.contentDOM, { key: 'ø', code: 'KeyO', altKey: true })
    expect(document.activeElement).toBe(row('Goals'))
  })

  it('a row click hands the cursor back to the editor on the heading', () => {
    const { view, onCancel } = setup()
    fireEvent.mouseDown(row('Risks'))
    fireEvent.click(row('Risks'), { detail: 1 })
    expect(cursorLine(view)).toBe(5)
    expect(view.contentDOM.contains(document.activeElement)).toBe(true)
    expect(onCancel).not.toHaveBeenCalled()
  })

  it('a row click flashes the heading line; ↓ does not', async () => {
    const { view } = setup()
    const flashed = () => Array.from(view.contentDOM.querySelectorAll('.knit-flash')).map((l) => l.textContent)
    fireEvent.click(row('Risks'), { detail: 1 })
    await waitFor(() => expect(flashed()).toEqual(['## Risks']))
    await waitFor(() => expect(flashed()).toEqual([]), { timeout: 2500 })
    press(view.contentDOM, { key: 'O', code: 'KeyO', ctrlKey: true, shiftKey: true })
    press(document.activeElement!, { key: 'ArrowDown' })
    await new Promise((r) => setTimeout(r, 50))
    expect(flashed()).toEqual([])
  })
})
