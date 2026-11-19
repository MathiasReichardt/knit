import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { Link2 } from 'lucide-react'
import { DocOutline, type OutlineTail } from './DocOutline'
import { CollapsibleSection } from './CollapsibleSection'
import { renderMarkdown } from '../lib/markdown'
import { HeadingSlugger } from '../lib/outline'
import { useOutlinePrefs } from '../stores/outlinePrefs'
import { useSectionOpen } from '../stores/sectionOpen'

const BODY = '## Goals\n\ntext\n\n### Scope\n\ntext\n\n## Risks\n\ntext\n'

function Doc({ body = BODY, enabled = true, minHeadings }: { body?: string; enabled?: boolean; minHeadings?: number }) {
  return (
    <>
      <button type="button">elsewhere</button>
      <input aria-label="typing" />
      <DocOutline body={body} docRef="design:web" enabled={enabled} minHeadings={minHeadings}>
        <div dangerouslySetInnerHTML={{ __html: renderMarkdown(body, new HeadingSlugger()) }} />
      </DocOutline>
    </>
  )
}

const rail = () => screen.getByRole('button', { name: 'Outline (o)' })
const row = (name: string) => screen.getByRole('button', { name })
const press = (key: string, target: Element = document.activeElement ?? document.body) =>
  act(() => {
    fireEvent.keyDown(target, { key })
  })

// The test body renders without source lines, so a jump scrolls the heading itself.
let scrolledTo: string[] = []

beforeEach(() => {
  useOutlinePrefs.setState({ pinned: false })
  scrolledTo = []
  Element.prototype.scrollIntoView = function (this: Element) {
    if (/^(H[23]|SECTION)$/.test(this.tagName)) scrolledTo.push(this.id)
  }
  if (typeof CSS === 'undefined' || !CSS.escape) {
    vi.stubGlobal('CSS', { escape: (s: string) => s.replace(/[^\w-]/g, (c) => `\\${c}`) })
  }
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('DocOutline', () => {
  it('lists the h2/h3 headings beside the body', () => {
    render(<Doc />)
    expect(rail().getAttribute('aria-expanded')).toBe('false')
    expect(['Goals', 'Scope', 'Risks'].map((n) => row(n).dataset.outlineId)).toEqual(['goals', 'scope', 'risks'])
  })

  it('`o` opens the outline on the first heading', () => {
    render(<Doc />)
    press('o', document.body)
    expect(document.activeElement).toBe(row('Goals'))
    expect(rail().getAttribute('aria-expanded')).toBe('true')
  })

  it('↑/↓ and Home / End jump straight to the section', () => {
    render(<Doc />)
    press('o', document.body)
    press('ArrowDown')
    expect(document.activeElement).toBe(row('Scope'))
    expect(row('Scope').getAttribute('aria-current')).toBe('location')
    expect(scrolledTo).toEqual(['scope'])
    press('End')
    expect(document.activeElement).toBe(row('Risks'))
    expect(row('Risks').getAttribute('aria-current')).toBe('location')
    press('Home')
    press('ArrowUp')
    expect(document.activeElement).toBe(row('Goals'))
    expect(scrolledTo).toEqual(['scope', 'risks', 'goals', 'goals'])
  })

  it('a row click jumps to its section', () => {
    render(<Doc />)
    fireEvent.click(row('Risks'))
    expect(scrolledTo).toEqual(['risks'])
    expect(row('Risks').getAttribute('aria-current')).toBe('location')
  })

  it('`c` copies the focused section link', () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    render(<Doc />)
    press('o', document.body)
    press('ArrowDown')
    press('c')
    expect(writeText).toHaveBeenCalledWith('design:web#scope')
  })

  it('Esc closes and returns focus where it came from', () => {
    render(<Doc />)
    const before = screen.getByRole('button', { name: 'elsewhere' })
    before.focus()
    press('o', before)
    expect(document.activeElement).toBe(row('Goals'))
    press('Escape')
    expect(document.activeElement).toBe(before)
    expect(rail().getAttribute('aria-expanded')).toBe('false')
  })

  it('`o` is text while typing', () => {
    render(<Doc />)
    const input = screen.getByRole('textbox', { name: 'typing' })
    input.focus()
    press('o', input)
    expect(document.activeElement).toBe(input)
  })

  it('the pin persists; a narrow pane keeps the overlay', () => {
    render(<Doc />)
    fireEvent.click(screen.getByRole('button', { name: /^Pin outline/ }))
    expect(useOutlinePrefs.getState().pinned).toBe(true)
    // jsdom lays out nothing, so the pane is 0 px wide: still the rail + overlay.
    expect(rail()).toBeTruthy()
    expect(screen.getByRole('button', { name: /too narrow/ })).toBeTruthy()
  })

  it('after a mouse pick the overlay stays while hovered and closes on leave', () => {
    vi.useFakeTimers()
    try {
      render(<Doc />)
      const box = rail().parentElement!
      fireEvent.mouseEnter(box)
      act(() => vi.advanceTimersByTime(100))
      expect(rail().getAttribute('aria-expanded')).toBe('true')
      fireEvent.click(row('Risks'), { detail: 1 })
      expect(scrolledTo).toEqual(['risks'])
      expect(rail().getAttribute('aria-expanded')).toBe('true')
      fireEvent.mouseLeave(box)
      act(() => vi.advanceTimersByTime(500))
      expect(rail().getAttribute('aria-expanded')).toBe('false')
    } finally {
      vi.useRealTimers()
    }
  })

  it('after a mouse pick ↑/↓ go on from the clicked row', () => {
    vi.useFakeTimers()
    try {
      render(<Doc />)
      const box = rail().parentElement!
      fireEvent.mouseEnter(box)
      act(() => vi.advanceTimersByTime(100))
      fireEvent.click(row('Goals'), { detail: 1 })
      expect(document.activeElement).toBe(row('Goals'))
      press('ArrowDown')
      expect(document.activeElement).toBe(row('Scope'))
      expect(scrolledTo).toEqual(['goals', 'scope'])
      fireEvent.mouseLeave(box)
      act(() => vi.advanceTimersByTime(500))
      expect(document.activeElement).not.toBe(row('Scope'))
    } finally {
      vi.useRealTimers()
    }
  })

  it('a pin in a wide pane stays open when focus leaves', () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1000)
    render(<Doc />)
    fireEvent.click(screen.getByRole('button', { name: /^Pin outline/ }))
    expect(screen.queryByRole('button', { name: 'Outline (o)' })).toBeNull()
    const outside = screen.getByRole('button', { name: 'elsewhere' })
    act(() => outside.focus())
    fireEvent.pointerDown(outside)
    expect(screen.getByRole('navigation', { name: 'Outline' }).className).not.toContain('invisible')
  })

  it('hides below the heading threshold and while editing', () => {
    const { rerender } = render(<Doc minHeadings={4} />)
    expect(screen.queryByRole('navigation', { name: 'Outline' })).toBeNull()
    rerender(<Doc enabled={false} />)
    expect(screen.queryByRole('navigation', { name: 'Outline' })).toBeNull()
    rerender(<Doc />)
    expect(screen.getByRole('navigation', { name: 'Outline' })).toBeTruthy()
  })
})

describe('DocOutline tail', () => {
  const tail: OutlineTail[] = [
    { id: 'links', text: 'Links', icon: Link2, reveal: () => useSectionOpen.getState().setOpen('test.links', true) },
  ]
  function Item({ body = BODY }: { body?: string }) {
    return (
      <DocOutline body={body} docRef="item:x" minHeadings={3} tail={tail}>
        <div dangerouslySetInnerHTML={{ __html: renderMarkdown(body, new HeadingSlugger()) }} />
        <CollapsibleSection icon={Link2} id="links" label="Links (1)" persistKey="test.links">
          <p>the links</p>
        </CollapsibleSection>
      </DocOutline>
    )
  }
  const rowIds = () => screen.getAllByRole('button').flatMap((b) => (b.dataset.outlineId ? [b.dataset.outlineId] : []))

  it('lists the sections after the headings and counts them', () => {
    const { rerender } = render(<Item />)
    expect(rowIds()).toEqual(['goals', 'scope', 'risks', 'links'])
    expect(within(row('Links').parentElement!).queryByRole('button', { name: 'Copy link to section' })).toBeNull()
    rerender(<Item body={'## Goals\n\n## Risks\n'} />)
    expect(rowIds()).toEqual(['goals', 'risks', 'links'])
    rerender(<Item body={'## Goals\n'} />)
    expect(screen.queryByRole('navigation', { name: 'Outline' })).toBeNull()
  })

  it('a jump to a collapsed section opens it', () => {
    useSectionOpen.setState({ byKey: { 'test.links': false } })
    render(<Item />)
    expect(screen.queryByText('the links')).toBeNull()
    fireEvent.click(row('Links'), { detail: 1 })
    expect(screen.getByText('the links')).toBeTruthy()
    expect(scrolledTo).toEqual(['links'])
  })
})
