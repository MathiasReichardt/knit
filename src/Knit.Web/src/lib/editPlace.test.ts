import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderMarkdown } from './markdown'
import { alignLine, blockForLine, editPlaceAt, holdLine, landingReady, uniqueWord } from './editPlace'

function view(body: string): HTMLElement {
  const root = document.createElement('div')
  root.innerHTML = renderMarkdown(body, undefined, undefined, { firstLine: 1 })
  document.body.replaceChildren(root)
  return root
}

const select = (node: Node, start: number, end: number) => {
  const range = document.createRange()
  range.setStart(node, start)
  range.setEnd(node, end)
  window.getSelection()!.removeAllRanges()
  window.getSelection()!.addRange(range)
}

afterEach(() => {
  window.getSelection()?.removeAllRanges()
  vi.restoreAllMocks()
})

describe('uniqueWord', () => {
  const body = 'alpha beta\ngamma alpha\nbetamax beta'
  it('finds a word occurring once in the range', () => {
    expect(uniqueWord(body, 'gamma', 1, 4)).toEqual({ line: 2, column: 0 })
  })
  it('matches whole words only', () => {
    expect(uniqueWord(body, 'beta', 3, 4)).toEqual({ line: 3, column: 8 })
  })
  it('gives up on a repeated word', () => {
    expect(uniqueWord(body, 'alpha', 1, 4)).toBeUndefined()
  })
})

describe('editPlaceAt', () => {
  const BODY = 'First para\nwraps here\n\n- one\n- two\n\n## Next\n\nLast'

  it('maps a click to its block line', () => {
    const root = view(BODY)
    const li = root.querySelectorAll('li')[1]!
    expect(editPlaceAt(root, li.firstChild, BODY)?.line).toBe(5)
  })

  it('refines to the selected word line within a paragraph', () => {
    const root = view(BODY)
    const text = root.querySelector('p')!.firstChild!
    // jsdom has no layout, so no Range.getBoundingClientRect.
    Range.prototype.getBoundingClientRect = () => ({ top: 42 }) as DOMRect
    select(text, 11, 16) // "wraps"
    expect(editPlaceAt(root, text, BODY)).toEqual({ line: 2, column: 0, top: 42 })
  })

  it('falls back to the nearest block above a target outside any block', () => {
    const root = view(BODY)
    const loose = document.createElement('span')
    root.querySelector('h2')!.after(loose)
    expect(editPlaceAt(root, loose, BODY)?.line).toBe(7)
  })

  it('has no place without a mapped block', () => {
    const root = document.createElement('div')
    root.innerHTML = '<p>empty</p>'
    expect(editPlaceAt(root, root.firstChild, 'empty')).toBeUndefined()
  })
})

describe('landing', () => {
  it('finds the cursor block in a text that grew above it', () => {
    const root = view('Added\n\nlines\n\n## Target\n\nText under it')
    expect(blockForLine(root, 7)?.textContent).toBe('Text under it')
    expect(blockForLine(root, 6)?.tagName).toBe('H2')
  })

  it('waits for the saved text, ignoring line endings and trailing space', () => {
    expect(landingReady({ line: 1, top: 0, text: 'a\r\nb\n' }, 'a\nb')).toBe(true)
    expect(landingReady({ line: 1, top: 0, text: 'new' }, 'old')).toBe(false)
    expect(landingReady({ line: 1, top: 0 }, 'old')).toBe(true)
  })

  it('scrolls the block to the cursor height, re-aligning while a diagram renders', () => {
    const scroller = document.createElement('div')
    scroller.style.overflowY = 'auto'
    const root = view('Para\n\n## Target')
    scroller.append(root)
    document.body.replaceChildren(scroller)
    let top = 500
    vi.spyOn(root.querySelector('h2')!, 'getBoundingClientRect').mockImplementation(() => ({ top: top - scroller.scrollTop }) as DOMRect)
    let resized = () => {}
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(cb: () => void) {
          resized = cb
        }
        observe() {}
        disconnect() {
          resized = () => {}
        }
      },
    )
    const done = vi.fn()
    const stop = holdLine(root, { line: 3, top: 100 }, done)
    expect(scroller.scrollTop).toBe(400)
    top = 700 // a diagram above rendered and pushed the block down
    resized()
    expect(scroller.scrollTop).toBe(600)
    window.dispatchEvent(new Event('wheel'))
    expect(done).toHaveBeenCalledOnce()
    top = 900
    resized()
    expect(scroller.scrollTop).toBe(600)
    stop()
    vi.unstubAllGlobals()
  })

  it('leaves the scroll alone without a block', () => {
    const root = document.createElement('div')
    alignLine(root, { line: 1, top: 0 })
    expect(document.documentElement.scrollTop).toBe(0)
  })
})
