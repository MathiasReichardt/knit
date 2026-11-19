import { create } from 'zustand'

export type ToastKind = 'info' | 'success' | 'error'

export interface Toast {
  id: number
  kind: ToastKind
  message: string
  /** A command the toast offers to copy, in a popover — the way out of what it reports. */
  command?: string
}

interface ToastsState {
  toasts: Toast[]
  push: (kind: ToastKind, message: string, options?: { command?: string }) => number
  dismiss: (id: number) => void
}

let nextId = 1

export const useToasts = create<ToastsState>((set) => ({
  toasts: [],
  push: (kind, message, options) => {
    const id = nextId++
    const command = options?.command
    set((s) => ({ toasts: [...s.toasts, { id, kind, message, ...(command ? { command } : {}) }] }))
    // A toast carrying a command stays until dismissed: it would vanish while
    // its popover is open and the command half-copied.
    if (!command)
      window.setTimeout(() => {
        set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
      }, kind === 'error' ? 6000 : 3000)
    return id
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}))
