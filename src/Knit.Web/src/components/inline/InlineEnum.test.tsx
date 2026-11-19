import { describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineEnum, type InlineEnumOption } from './InlineEnum'

const OPTIONS: InlineEnumOption<string>[] = [
  { value: 'todo', label: 'Todo' },
  { value: 'in-progress', label: 'In progress' },
  { value: 'done', label: 'Done' },
]

function setup(value = 'todo', options = OPTIONS, disabled = false) {
  const onChange = vi.fn()
  const user = userEvent.setup()
  render(
    <InlineEnum value={value} options={options} label="state" onChange={onChange} disabled={disabled} />,
  )
  return { onChange, user }
}

const trigger = () => screen.getByRole('button', { name: 'Change state' })
const option = (name: RegExp) => screen.getByRole('option', { name })

describe('InlineEnum', () => {
  it('makes the value itself the control', () => {
    setup()
    // The aria-label rides on the value, not on a pen beside it.
    expect(trigger().textContent).toContain('Todo')
    expect(trigger().getAttribute('aria-expanded')).toBe('false')
    expect(trigger().getAttribute('aria-haspopup')).toBe('listbox')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('sits in the tab order', async () => {
    const { user } = setup()
    await user.tab()
    expect(document.activeElement).toBe(trigger())
  })

  it('opens on click with the current option selected and focused', async () => {
    const { user } = setup('done')
    await user.click(trigger())
    expect(option(/Done/).getAttribute('aria-selected')).toBe('true')
    expect(screen.getByRole('listbox', { name: 'state' }).getAttribute('aria-activedescendant'))
      .toBe(option(/Done/).id)
  })

  it('opens from the keyboard on Enter, Space and either arrow', async () => {
    for (const key of ['{Enter}', ' ', '{ArrowDown}', '{ArrowUp}']) {
      const { user } = setup()
      await user.tab()
      await user.keyboard(key)
      expect(screen.queryByRole('listbox')).not.toBeNull()
      cleanup()
    }
  })

  it('arrows move option focus without committing', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(screen.getByRole('listbox').getAttribute('aria-activedescendant')).toBe(option(/Done/).id)
    // Still open, still nothing written — the whole point of activedescendant.
    expect(onChange).not.toHaveBeenCalled()
  })

  it('wraps at both ends', async () => {
    const { user } = setup()
    await user.click(trigger())
    await user.keyboard('{ArrowUp}')
    expect(screen.getByRole('listbox').getAttribute('aria-activedescendant')).toBe(option(/Done/).id)
  })

  it('commits the focused option on Enter and returns focus to the value', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenCalledWith('in-progress')
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(document.activeElement).toBe(trigger())
  })

  it('commits on a click', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.click(option(/Done/))
    expect(onChange).toHaveBeenCalledWith('done')
  })

  it('leaves the value alone on Escape', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.keyboard('{ArrowDown}{Escape}')
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(document.activeElement).toBe(trigger())
  })

  it('does not write when the current value is re-picked', async () => {
    const { onChange, user } = setup('todo')
    await user.click(trigger())
    await user.keyboard('{Enter}')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('commits when Tab leaves an open listbox', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.keyboard('{ArrowDown}')
    await user.keyboard('{Tab}')
    expect(onChange).toHaveBeenCalledWith('in-progress')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('closes on an outside click without writing', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.click(document.body)
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('renders a group heading once per group', async () => {
    const { user } = setup('a', [
      { value: 'a', label: 'a' },
      { value: 'b', label: 'b', group: 'archived' },
      { value: 'c', label: 'c', group: 'archived' },
    ])
    await user.click(trigger())
    expect(screen.getAllByText('archived')).toHaveLength(1)
  })

  it('cannot be opened when disabled', () => {
    setup('todo', OPTIONS, true)
    expect((trigger() as HTMLButtonElement).disabled).toBe(true)
  })
})
