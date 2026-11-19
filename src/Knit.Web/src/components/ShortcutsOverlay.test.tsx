import { afterEach, describe, expect, it } from 'vitest'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { ShortcutsOverlay } from './ShortcutsOverlay'
import { useDialogs } from '../stores/dialogs'

const keysOf = (group: string, description: string) => {
  const dd = within(screen.getByRole('region', { name: group })).getByText(description)
  return Array.from(dd.previousElementSibling!.querySelectorAll('kbd')).map((k) => k.textContent)
}

afterEach(() => {
  useDialogs.setState({ shortcutsOpen: false })
})

describe('ShortcutsOverlay', () => {
  it('`?` opens the sheet in four groups', () => {
    render(<ShortcutsOverlay />)
    act(() => {
      fireEvent.keyDown(window, { key: '?' })
    })
    const titles = screen.getAllByRole('region').map((r) => r.getAttribute('aria-label'))
    expect(titles).toEqual(['Global', 'Navigation', 'Outline', 'Editor'])
  })

  it('lists the outline keys apart from `g o`', () => {
    useDialogs.setState({ shortcutsOpen: true })
    render(<ShortcutsOverlay />)
    expect(keysOf('Outline', 'Open the outline of the text shown')).toEqual(['o'])
    expect(keysOf('Outline', 'Open the outline in the editor')).toEqual(['Ctrl+Shift+O', 'Alt+O'])
    expect(keysOf('Navigation', 'Overview (landing page)')).toEqual(['g', 'o'])
    expect(keysOf('Editor', 'Save')).toEqual(['Ctrl+S'])
  })
})
