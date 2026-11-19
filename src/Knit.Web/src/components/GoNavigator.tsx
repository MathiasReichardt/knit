import { useEffect, useRef } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useDialogs } from '../stores/dialogs'
import { useNavigator } from '../stores/navigator'

// `g t`/`g l` both land on /items but pick the view mode; the rest are
// plain route hops. `g r` opens the jump-to-ref box (handled separately).
const ROUTES: Record<string, string> = {
  o: '/',
  b: '/kb',
  d: '/designs',
  // `a` as in agenda — `t` is taken by the tree view.
  a: '/timeline',
  h: '/doctor',
}

/**
 * Two-key `g <x>` navigation. `g r` opens the jump-to-ref box instead of
 * navigating to a fixed route.
 */
export function GoNavigator() {
  const navigate = useNavigate()
  const openJump = useDialogs((s) => s.openJump)
  const setView = useNavigator((s) => s.setView)
  const armed = useRef(false)
  const armTimer = useRef<number | null>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (isTyping(e.target)) return
      if (e.key === 'g' && !e.ctrlKey && !e.metaKey && !e.altKey) {
        armed.current = true
        if (armTimer.current) window.clearTimeout(armTimer.current)
        armTimer.current = window.setTimeout(() => (armed.current = false), 1200)
        return
      }
      if (!armed.current) return
      armed.current = false
      if (armTimer.current) window.clearTimeout(armTimer.current)
      const key = e.key.toLowerCase()
      if (key === 'r') {
        e.preventDefault()
        openJump()
        return
      }
      if (key === 't' || key === 'l') {
        e.preventDefault()
        setView(key === 't' ? 'tree' : 'list')
        void navigate({ to: '/items' })
        return
      }
      const target = ROUTES[key]
      if (target) {
        e.preventDefault()
        void navigate({ to: target })
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [navigate, openJump, setView])

  return null
}

function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || target.isContentEditable
}
