import { afterEach, describe, expect, it, vi } from 'vitest'
import { createElement } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { renderMarkdown } from './markdown'
import { HeadingSlugger } from './outline'
import { replaceUrlHash, sectionLinkAt } from './sectionLink'
import { activeHeadingId, holdHeading } from './editPlace'
import { navTarget, resolveLink } from './resolveLink'
import { OutlineRows } from '../components/OutlineRows'

afterEach(() => {
  vi.restoreAllMocks()
  window.history.replaceState(null, '', '/')
})

describe('copying a section link', () => {
  it('copies the same ref from the heading icon and the outline row', async () => {
    const host = document.createElement('div')
    host.innerHTML = renderMarkdown('## Goals\n\ntext', new HeadingSlugger(), undefined, { sectionLinks: true })
    const fromHeading = sectionLinkAt(host.querySelector('button'), 'design:web')

    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    render(
      createElement(OutlineRows, {
        items: [{ depth: 2, text: 'Goals', id: 'goals' }],
        docRef: 'design:web',
        activeHeadingId: null,
        focusId: null,
        onSelect: () => {},
        indent: () => 0,
      }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Copy link to section' }))

    expect(fromHeading).toBe('design:web#goals')
    expect(writeText).toHaveBeenCalledWith('design:web#goals')
  })

  it('gives nothing outside the button or without a doc ref', () => {
    const host = document.createElement('div')
    host.innerHTML = renderMarkdown('## Goals', new HeadingSlugger(), undefined, { sectionLinks: true })
    expect(sectionLinkAt(host.querySelector('h2'), 'design:web')).toBeNull()
    expect(sectionLinkAt(host.querySelector('button'), undefined)).toBeNull()
  })
})

describe('the URL fragment', () => {
  it('tracks the heading by replacing the entry, never pushing one', () => {
    const replace = vi.spyOn(window.history, 'replaceState')
    const push = vi.spyOn(window.history, 'pushState')
    const length = window.history.length

    replaceUrlHash('goals')
    expect(window.location.hash).toBe('#goals')
    replaceUrlHash('goals')
    replaceUrlHash(null)

    expect(window.location.hash).toBe('')
    expect(replace).toHaveBeenCalledTimes(2)
    expect(push).not.toHaveBeenCalled()
    expect(window.history.length).toBe(length)
  })
})

function headingsAt(tops: number[]) {
  const scroller = document.createElement('div')
  scroller.style.overflowY = 'auto'
  scroller.getBoundingClientRect = () => ({ top: 100 }) as DOMRect
  const container = document.createElement('div')
  tops.forEach((top, i) => {
    const h = document.createElement('h2')
    h.id = `h${i}`
    h.setAttribute('data-line', String(i * 10 + 1))
    h.getBoundingClientRect = () => ({ top }) as DOMRect
    container.appendChild(h)
  })
  scroller.appendChild(container)
  document.body.appendChild(scroller)
  return { scroller, container }
}

describe('the heading being read', () => {
  it('is the last one at or just below the scroller top', () => {
    const { scroller, container } = headingsAt([20, 110, 400])
    expect(activeHeadingId(container, scroller)).toBe('h1')
    scroller.remove()
  })

  it('is none above the first heading', () => {
    const { scroller, container } = headingsAt([300, 500])
    expect(activeHeadingId(container, scroller)).toBeNull()
    scroller.remove()
  })

  it('is restored on load by scrolling the heading to the scroller top', () => {
    const { scroller, container } = headingsAt([20, 400])
    scroller.scrollTop = 0
    const stop = holdHeading(container, container.querySelector<HTMLElement>('#h1')!, () => {})
    expect(scroller.scrollTop).toBe(300)
    stop()
    scroller.remove()
  })
})

describe('following a section ref into each body view', () => {
  it.each([
    ['design:web#goals', '/designs'],
    ['doc:guide#goals', '/kb'],
    ['item:G7H8K2M4P9Q1R5T3VX#goals', '/items'],
    ['project:p#goals', '/projects'],
    ['.knit/projects/p/roadmap.md#goals', '/projects'],
    ['.knit/projects/p/milestones/m1.md#goals', '/projects'],
  ])('%s', (ref, to) => {
    expect(navTarget(resolveLink('', ref))).toMatchObject({ to, hash: 'goals' })
  })
})
