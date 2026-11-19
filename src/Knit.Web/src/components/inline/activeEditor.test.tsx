import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineDate } from './InlineDate'
import { InlineEnum } from './InlineEnum'
import { InlineText } from './InlineText'
import { claimEditor, releaseEditor, resetActiveEditor } from './activeEditor'

beforeEach(() => resetActiveEditor())

describe('activeEditor', () => {
  it('closes the previous claimant when a new one arrives', () => {
    const a = { close: vi.fn() }
    const b = { close: vi.fn() }
    claimEditor(a)
    claimEditor(b)
    expect(a.close).toHaveBeenCalledOnce()
    expect(b.close).not.toHaveBeenCalled()
  })

  it('does not close a handle re-claiming its own slot', () => {
    const a = { close: vi.fn() }
    claimEditor(a)
    claimEditor(a)
    expect(a.close).not.toHaveBeenCalled()
  })

  it('ignores a release from a handle that no longer holds the slot', () => {
    const a = { close: vi.fn() }
    const b = { close: vi.fn() }
    claimEditor(a)
    claimEditor(b)
    releaseEditor(a)
    // b still holds it, so the next claim must still displace b.
    const c = { close: vi.fn() }
    claimEditor(c)
    expect(b.close).toHaveBeenCalledOnce()
  })
})

// The reported bug: clicking straight from one text field to another left both
// inputs live, and Esc then only closed whichever had focus.
describe('only one inline editor at a time', () => {
  function twoTextFields() {
    const assignee = vi.fn()
    const effort = vi.fn()
    const user = userEvent.setup()
    render(
      <>
        <InlineText value="alice" label="assignee" onCommit={assignee} />
        <InlineText value="3d" label="effort" onCommit={effort} />
      </>,
    )
    return { assignee, effort, user }
  }

  const editAssignee = () => screen.getByRole('button', { name: 'Edit assignee' })
  const editEffort = () => screen.getByRole('button', { name: 'Edit effort' })
  const openLabels = () =>
    screen.queryAllByRole('textbox').map((t) => t.getAttribute('aria-label'))

  it('leaves only the second field editing', async () => {
    const { user } = twoTextFields()
    await user.click(editAssignee())
    await user.click(editEffort())
    expect(openLabels()).toEqual(['effort'])
  })

  it('commits the displaced field, because being displaced is a blur', async () => {
    const { assignee, user } = twoTextFields()
    await user.click(editAssignee())
    await user.type(screen.getByRole('textbox', { name: 'assignee' }), 'X')
    await user.click(editEffort())
    expect(assignee).toHaveBeenCalledWith('aliceX')
  })

  it('does not steal focus back to the displaced field', async () => {
    const { user } = twoTextFields()
    await user.click(editAssignee())
    await user.click(editEffort())
    // The old close-always-refocus pulled focus back a frame later, which is
    // what let the second editor end up unfocused or never open at all.
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'effort' })),
    )
  })

  it('leaves nothing editing after Esc', async () => {
    const { user } = twoTextFields()
    await user.click(editAssignee())
    await user.click(editEffort())
    await user.keyboard('{Escape}')
    expect(openLabels()).toEqual([])
  })

  it('closes a text editor when a listbox opens', async () => {
    const user = userEvent.setup()
    render(
      <>
        <InlineText value="alice" label="assignee" onCommit={vi.fn()} />
        <InlineEnum
          value="todo"
          options={[{ value: 'todo', label: 'Todo' }, { value: 'done', label: 'Done' }]}
          label="state"
          onChange={vi.fn()}
        />
      </>,
    )
    await user.click(screen.getByRole('button', { name: 'Edit assignee' }))
    await user.click(screen.getByRole('button', { name: 'Change state' }))
    expect(screen.queryAllByRole('textbox')).toHaveLength(0)
    expect(screen.queryByRole('listbox')).not.toBeNull()
  })

  it('closes a listbox when a text editor opens', async () => {
    const user = userEvent.setup()
    render(
      <>
        <InlineEnum
          value="todo"
          options={[{ value: 'todo', label: 'Todo' }, { value: 'done', label: 'Done' }]}
          label="state"
          onChange={vi.fn()}
        />
        <InlineText value="alice" label="assignee" onCommit={vi.fn()} />
      </>,
    )
    await user.click(screen.getByRole('button', { name: 'Change state' }))
    await user.click(screen.getByRole('button', { name: 'Edit assignee' }))
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(screen.queryAllByRole('textbox')).toHaveLength(1)
  })

  it('closes a calendar when another field opens', async () => {
    const user = userEvent.setup()
    render(
      <>
        <InlineDate value="2026-09-20" label="due date" today="2026-09-15" onChange={vi.fn()} />
        <InlineText value="alice" label="assignee" onCommit={vi.fn()} />
      </>,
    )
    await user.click(screen.getByRole('button', { name: 'Change due date' }))
    await user.click(screen.getByRole('button', { name: 'Edit assignee' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
