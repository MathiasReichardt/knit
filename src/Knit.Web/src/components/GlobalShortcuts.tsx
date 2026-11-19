import { useEffect } from 'react'
import { useDialogs } from '../stores/dialogs'
import { useEditIntent } from '../stores/editIntent'

/**
 * Global keybinds: `n` opens the new-item dialog, `e` asks the active
 * screen's editor to enter edit mode (selected item / KB entry / design).
 * The `g x` navigator and `?` overlay live in their own components.
 */
export function GlobalShortcuts() {
  const openNewItem = useDialogs((s) => s.openNewItem)
  const newItemOpen = useDialogs((s) => s.newItemOpen)
  const requestEdit = useEditIntent((s) => s.request)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented) return
      if (newItemOpen) return
      const target = e.target as HTMLElement | null
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) return
      if (e.ctrlKey || e.metaKey || e.altKey) return
      if (e.key === 'n') {
        e.preventDefault()
        openNewItem()
      } else if (e.key === 'e') {
        e.preventDefault()
        requestEdit()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [openNewItem, newItemOpen, requestEdit])
  return null
}
