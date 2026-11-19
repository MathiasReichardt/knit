import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineText } from './InlineText'

function setup(value: string | null = 'alice', required = false) {
  const onCommit = vi.fn()
  const user = userEvent.setup()
  render(<InlineText value={value} label="assignee" required={required} onCommit={onCommit} />)
  return { onCommit, user }
}

const trigger = () => screen.getByRole('button', { name: 'Edit assignee' })
const input = () => screen.getByRole('textbox', { name: 'assignee' })

describe('InlineText', () => {
  it('makes the value itself the control', () => {
    setup()
    expect(trigger().textContent).toContain('alice')
    expect(screen.queryByRole('textbox')).toBeNull()
  })

  it('shows the empty placeholder when unset', () => {
    setup(null)
    expect(trigger().textContent).toContain('—')
  })

  it('opens on click, seeded with the current value', async () => {
    const { user } = setup()
    await user.click(trigger())
    expect((input() as HTMLInputElement).value).toBe('alice')
  })

  it('opens on Enter from the keyboard', async () => {
    const { user } = setup()
    await user.tab()
    await user.keyboard('{Enter}')
    expect(screen.queryByRole('textbox')).not.toBeNull()
  })

  it('commits on Enter and hands focus back to the value', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.clear(input())
    await user.type(input(), 'bob{Enter}')
    expect(onCommit).toHaveBeenCalledWith('bob')
    // Committing with the keyboard must not drop the user at the top of the
    // document — focus returns to the field.
    await vi.waitFor(() => expect(document.activeElement).toBe(trigger()))
  })

  it('commits on blur', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.clear(input())
    await user.type(input(), 'bob')
    await user.tab()
    expect(onCommit).toHaveBeenCalledWith('bob')
  })

  it('discards the edit on Escape and restores the original', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.clear(input())
    await user.type(input(), 'bob{Escape}')
    // The blur Escape causes must not save — escaping out of an edit is how
    // you get out of it.
    expect(onCommit).not.toHaveBeenCalled()
    expect(trigger().textContent).toContain('alice')
  })

  it('does not write an unchanged value', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.keyboard('{Enter}')
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('trims before committing', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.clear(input())
    await user.type(input(), '  bob  {Enter}')
    expect(onCommit).toHaveBeenCalledWith('bob')
  })

  it('treats an emptied optional field as a clear', async () => {
    const { onCommit, user } = setup()
    await user.click(trigger())
    await user.clear(input())
    await user.keyboard('{Enter}')
    expect(onCommit).toHaveBeenCalledWith('')
  })

  it('refuses to empty a required field and keeps the editor open', async () => {
    const { onCommit, user } = setup('Some title', true)
    await user.click(trigger())
    await user.clear(input())
    await user.keyboard('{Enter}')
    expect(onCommit).not.toHaveBeenCalled()
    expect(input().getAttribute('aria-invalid')).toBe('true')
  })

  it('clears the invalid state once typing resumes', async () => {
    const { onCommit, user } = setup('Some title', true)
    await user.click(trigger())
    await user.clear(input())
    await user.keyboard('{Enter}')
    expect(input().getAttribute('aria-invalid')).toBe('true')
    await user.type(input(), 'Another title{Enter}')
    expect(onCommit).toHaveBeenCalledWith('Another title')
  })
})

describe('InlineText with suggestions', () => {
  const VOCAB = ['bob', 'Bob', 'alice', 'robert']

  function suggest(value: string | null = null) {
    const onCommit = vi.fn()
    const user = userEvent.setup()
    render(<InlineText value={value} label="assignee" suggestions={VOCAB} onCommit={onCommit} />)
    return { onCommit, user }
  }
  const combo = () => screen.getByRole('combobox', { name: 'assignee' })
  const options = () => screen.queryAllByRole('option').map((o) => o.textContent)

  it('offers every handle when opened empty', async () => {
    const { user } = suggest()
    await user.click(trigger())
    expect(options()).toEqual(VOCAB)
  })

  it('takes what was typed when nothing is highlighted', async () => {
    const { onCommit, user } = suggest()
    await user.click(trigger())
    await user.type(combo(), 'bo{Enter}')
    expect(onCommit).toHaveBeenCalledWith('bo')
  })

  it('takes the highlighted suggestion after an arrow key', async () => {
    const { onCommit, user } = suggest()
    await user.click(trigger())
    await user.type(combo(), 'b')
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')
    expect(onCommit).toHaveBeenCalledWith('Bob')
  })

  it('commits a clicked suggestion', async () => {
    const { onCommit, user } = suggest()
    await user.click(trigger())
    await user.click(screen.getByRole('option', { name: 'alice' }))
    expect(onCommit).toHaveBeenCalledWith('alice')
  })

  it('Esc drops the highlight first, then reverts', async () => {
    const { onCommit, user } = suggest('alice')
    await user.click(trigger())
    await user.clear(combo())
    await user.keyboard('{ArrowDown}{Escape}')
    expect(screen.queryByRole('combobox')).not.toBeNull()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(onCommit).not.toHaveBeenCalled()
  })
})
