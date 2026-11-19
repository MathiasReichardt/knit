import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineChips } from './InlineChips'

const VOCAB = ['api', 'api-gateway', 'perf', 'ux']

// `null` means "no vocabulary at all" — an explicit `undefined` would fall back
// to the default parameter.
function setup(values: string[] = ['perf'], suggestions: string[] | null = VOCAB) {
  const onChange = vi.fn()
  const user = userEvent.setup()
  render(
    <InlineChips
      values={values}
      label="tag"
      suggestions={suggestions ?? undefined}
      onChange={onChange}
    />,
  )
  return { onChange, user }
}

const addButton = () => screen.getByRole('button', { name: 'Add tag' })
const input = () => screen.getByRole('combobox', { name: 'Add tag' })

describe('InlineChips', () => {
  it('gives every value its own remove', async () => {
    const { onChange, user } = setup(['perf', 'ux'])
    await user.click(screen.getByRole('button', { name: 'Remove ux' }))
    expect(onChange).toHaveBeenCalledWith(['perf'])
  })

  it('adds what was typed', async () => {
    const { onChange, user } = setup([])
    await user.click(addButton())
    await user.type(input(), 'brand-new{Enter}')
    expect(onChange).toHaveBeenCalledWith(['brand-new'])
  })

  it('offers the whole vocabulary before anything is typed', async () => {
    const { user } = setup([])
    await user.click(addButton())
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(VOCAB)
  })

  it('filters the suggestions as you type, prefix matches first', async () => {
    const { user } = setup([])
    await user.click(addButton())
    await user.type(input(), 'api')
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['api', 'api-gateway'])
  })

  it('does not offer a tag the entry already carries', async () => {
    const { user } = setup(['perf'])
    await user.click(addButton())
    expect(screen.getAllByRole('option').map((o) => o.textContent)).not.toContain('perf')
  })

  it('takes what was typed, not the first suggestion, when nothing is highlighted', async () => {
    // Typing a new tag that merely resembles an existing one must not be
    // silently swapped for it.
    const { onChange, user } = setup([])
    await user.type(await openInput(user), 'api-v2{Enter}')
    expect(onChange).toHaveBeenCalledWith(['api-v2'])
  })

  it('takes the highlighted suggestion once an arrow key has moved to it', async () => {
    const { onChange, user } = setup([])
    await user.type(await openInput(user), 'api')
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenCalledWith(['api-gateway'])
  })

  it('exposes the highlight through aria-activedescendant', async () => {
    const { user } = setup([])
    await user.click(addButton())
    await user.keyboard('{ArrowDown}')
    expect(input().getAttribute('aria-activedescendant'))
      .toBe(screen.getByRole('option', { name: 'api' }).id)
  })

  it('adds a suggestion on click', async () => {
    const { onChange, user } = setup([])
    await user.click(addButton())
    await user.click(screen.getByRole('option', { name: 'ux' }))
    expect(onChange).toHaveBeenCalledWith(['ux'])
  })

  it('drops the highlight on the first Escape and abandons the add on the second', async () => {
    const { onChange, user } = setup([])
    await user.type(await openInput(user), 'api')
    await user.keyboard('{ArrowDown}')
    await user.keyboard('{Escape}')
    expect(input().getAttribute('aria-activedescendant')).toBeNull()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('ignores a duplicate in different case', async () => {
    const { onChange, user } = setup(['perf'])
    await user.type(await openInput(user), 'PERF{Enter}')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('works as a plain input with no vocabulary', async () => {
    const { onChange, user } = setup([], null)
    await user.click(addButton())
    const plain = screen.getByRole('textbox', { name: 'Add tag' })
    await user.type(plain, 'solo{Enter}')
    expect(onChange).toHaveBeenCalledWith(['solo'])
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  async function openInput(user: ReturnType<typeof userEvent.setup>) {
    await user.click(addButton())
    return input()
  }
})
