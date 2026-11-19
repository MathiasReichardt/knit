import { useEffect, useState } from 'react'
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

export type Theme = 'system' | 'light' | 'dark'

interface ThemeState {
  theme: Theme
  setTheme: (t: Theme) => void
}

export const useTheme = create<ThemeState>()(
  persist(
    (set) => ({
      theme: 'system',
      setTheme: (theme) => set({ theme }),
    }),
    {
      name: 'knit.theme',
      storage: createJSONStorage(() => localStorage),
      partialize: (s) => ({ theme: s.theme }),
    },
  ),
)

export function resolveTheme(theme: Theme): 'light' | 'dark' {
  if (theme === 'system') {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  }
  return theme
}

export function applyThemeClass(theme: Theme): void {
  const resolved = resolveTheme(theme)
  document.documentElement.classList.toggle('dark', resolved === 'dark')
  // Native widgets (date fields and their calendar popup, select dropdowns,
  // scrollbars) paint from `color-scheme`, not from our `.dark` class — set it
  // here rather than in CSS so it can't be missed by a stale stylesheet.
  document.documentElement.style.colorScheme = resolved
}

export function useThemeSync(): void {
  const theme = useTheme((s) => s.theme)
  useEffect(() => {
    applyThemeClass(theme)
    if (theme !== 'system') return
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const listener = () => applyThemeClass('system')
    mq.addEventListener('change', listener)
    return () => mq.removeEventListener('change', listener)
  }, [theme])
}

export function useResolvedTheme(): 'light' | 'dark' {
  const theme = useTheme((s) => s.theme)
  const [resolved, setResolved] = useState(() => resolveTheme(theme))

  useEffect(() => {
    // Resync on theme change; the listener below also drives this on OS
    // scheme changes, so the resolved value must live in state + effect.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setResolved(resolveTheme(theme))
    if (theme !== 'system') return
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const listener = () => setResolved(resolveTheme('system'))
    mq.addEventListener('change', listener)
    return () => mq.removeEventListener('change', listener)
  }, [theme])

  return resolved
}
