import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EditableHeading } from './EditableHeading'

function setup(value = 'Ship the thing', editable = true) {
  const onCommit = vi.fn()
  const user = userEvent.setup()
  render(<EditableHeading value={value} editable={editable} onCommit={onCommit} />)
  return { onCommit, user }
}

describe('EditableHeading', () => {
  it('keeps the title as the heading name', () => {
    setup()
    // Navigating by heading must read the title, not "Edit title" — so the
    // trigger takes its name from its content.
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Ship the thing')
    expect(screen.getByRole('button', { name: 'Ship the thing' })).toBeTruthy()
  })

  it('keeps the button out of the heading element itself', () => {
    setup()
    // A <button> takes phrasing content, so the h1 wraps it, not the reverse.
    const heading = screen.getByRole('heading', { level: 1 })
    expect(heading.tagName).toBe('H1')
    expect(heading.querySelector('button')).not.toBeNull()
  })

  it('commits a new title', async () => {
    const { onCommit, user } = setup()
    await user.click(screen.getByRole('button', { name: 'Ship the thing' }))
    await user.clear(screen.getByRole('textbox', { name: 'title' }))
    await user.type(screen.getByRole('textbox', { name: 'title' }), 'Ship it{Enter}')
    expect(onCommit).toHaveBeenCalledWith('Ship it')
  })

  it('refuses an empty title', async () => {
    const { onCommit, user } = setup()
    await user.click(screen.getByRole('button', { name: 'Ship the thing' }))
    await user.clear(screen.getByRole('textbox', { name: 'title' }))
    await user.keyboard('{Enter}')
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('offers no affordance when not editable', () => {
    setup('An ADR title', false)
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('An ADR title')
    expect(screen.queryByRole('button')).toBeNull()
  })
})
