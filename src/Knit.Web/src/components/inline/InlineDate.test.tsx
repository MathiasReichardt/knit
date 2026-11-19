import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineDate } from './InlineDate'

const TODAY = '2026-09-15'

function setup(value: string | null = '2026-09-20', clearable = true) {
  const onChange = vi.fn()
  const user = userEvent.setup()
  render(
    <InlineDate value={value} label="due date" today={TODAY} clearable={clearable} onChange={onChange} />,
  )
  return { onChange, user }
}

const trigger = () => screen.getByRole('button', { name: 'Change due date' })

describe('InlineDate', () => {
  it('makes the value itself the control', () => {
    setup()
    expect(trigger().textContent).toContain('2026-09-20')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('opens on the month of the current value', async () => {
    const { user } = setup('2025-01-20')
    await user.click(trigger())
    expect(screen.getByRole('gridcell', { name: '2025-01-20' })).toBeTruthy()
  })

  it('opens on today when the field is empty', async () => {
    const { user } = setup(null)
    await user.click(trigger())
    expect(screen.getByRole('gridcell', { name: TODAY })).toBeTruthy()
  })

  it('picks a day and closes', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.click(screen.getByRole('gridcell', { name: '2026-09-22' }))
    expect(onChange).toHaveBeenCalledWith('2026-09-22')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('does not write when the day already set is re-picked', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.click(screen.getByRole('gridcell', { name: '2026-09-20' }))
    expect(onChange).not.toHaveBeenCalled()
  })

  it('marks the stored day selected', async () => {
    const { user } = setup('2026-09-20')
    await user.click(trigger())
    expect(screen.getByRole('gridcell', { name: '2026-09-20' }).getAttribute('aria-selected')).toBe('true')
  })

  it('moves a day with the arrows and commits with Enter', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.keyboard('{ArrowRight}{Enter}')
    expect(onChange).toHaveBeenCalledWith('2026-09-21')
  })

  it('moves a week with the vertical arrows', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenCalledWith('2026-09-27')
  })

  it('follows an arrow key across the month boundary', async () => {
    const { onChange, user } = setup('2026-09-30')
    await user.click(trigger())
    await user.keyboard('{ArrowRight}{Enter}')
    expect(onChange).toHaveBeenCalledWith('2026-10-01')
  })

  it('pages months without changing the value', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.click(screen.getByRole('button', { name: 'Next month' }))
    expect(screen.getByRole('gridcell', { name: '2026-10-15' })).toBeTruthy()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('offers Today', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.click(screen.getByRole('button', { name: 'Today' }))
    expect(onChange).toHaveBeenCalledWith(TODAY)
  })

  it('clears through the Clear row', async () => {
    const { onChange, user } = setup('2026-09-20')
    await user.click(trigger())
    await user.click(screen.getByRole('button', { name: 'Clear' }))
    expect(onChange).toHaveBeenCalledWith('')
  })

  it('offers no Clear when there is nothing to clear', async () => {
    const { user } = setup(null)
    await user.click(trigger())
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull()
  })

  it('offers no Clear when the field is not clearable', async () => {
    const { user } = setup('2026-09-20', false)
    await user.click(trigger())
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull()
  })

  it('closes on Escape without writing', async () => {
    const { onChange, user } = setup()
    await user.click(trigger())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('still opens on a malformed stored date', async () => {
    // Hand-edited frontmatter can hold anything; the picker falls back to today.
    const { user } = setup('soon')
    await user.click(trigger())
    expect(screen.getByRole('gridcell', { name: TODAY })).toBeTruthy()
  })
})
