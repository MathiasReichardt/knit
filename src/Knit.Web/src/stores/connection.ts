import { create } from 'zustand'

export type ConnectionStatus = 'open' | 'disconnected'

// A dropped SSE stream fires one `error` before the reconnect attempt.
// Suppress that first error; only surface "disconnected" once the retry
// also fails (second error), so brief blips don't flash a banner.
export const DISCONNECT_AFTER_ERRORS = 2

interface ConnectionState {
  status: ConnectionStatus
  /** True while a forced "Check now" reconnect is in flight. */
  checking: boolean
  /** Consecutive `error`s since the last successful `open`. */
  errorsSinceOpen: number
  /** Force an immediate reconnect; registered by the SSE connector. */
  reconnectNow: (() => void) | null
  reportOpen: () => void
  reportError: () => void
  setChecking: (v: boolean) => void
  setReconnectNow: (fn: (() => void) | null) => void
}

export const useConnection = create<ConnectionState>((set) => ({
  status: 'open',
  checking: false,
  errorsSinceOpen: 0,
  reconnectNow: null,
  reportOpen: () => set({ status: 'open', checking: false, errorsSinceOpen: 0 }),
  reportError: () =>
    set((s) => {
      const errorsSinceOpen = s.errorsSinceOpen + 1
      return {
        errorsSinceOpen,
        checking: false,
        status: errorsSinceOpen >= DISCONNECT_AFTER_ERRORS ? 'disconnected' : s.status,
      }
    }),
  setChecking: (checking) => set({ checking }),
  setReconnectNow: (reconnectNow) => set({ reconnectNow }),
}))
