import { useCallback, useEffect, useRef, useState } from 'react'
import type React from 'react'
import { arrowNavStep, isArrowKey, isTyping, type NavRow } from '../lib/arrowNav'

interface Options<T> {
  /** Visible rows in render order (node rows interleaved with their children). */
  rows: NavRow<T>[]
  /**
   * The node key the surface currently has selected (URL / store driven). The
   * cursor snaps to it on an *external* selection change (click, jump-to-ref,
   * search) but is left alone when the keyboard moved it.
   */
  selectedKey: string | null
  /** Map a row to the surface's own selection model. */
  onActivate: (row: NavRow<T>) => void
  /** Expand/collapse the row with this key. */
  onToggle: (key: string) => void
}

interface Result {
  onKeyDown: (e: React.KeyboardEvent) => void
}

/**
 * Headless keyboard navigation shared by every tree/list control. Owns the
 * cursor as the single source of truth: keyboard moves write it directly,
 * and external selection changes snap it (guarded so a keyboard move, where
 * the cursor leads and `selectedKey` lags a render, isn't bounced back).
 */
export function useArrowNav<T>({
  rows,
  selectedKey,
  onActivate,
  onToggle,
}: Options<T>): Result {
  const [cursorKey, setCursorKey] = useState<string | null>(null)

  // Latest values mirrored into refs so the effects and handler read fresh
  // state without re-subscribing. Synced in an effect (declared first) so the
  // reads below see the value committed this render.
  const cursorRef = useRef<string | null>(cursorKey)
  const rowsRef = useRef(rows)
  useEffect(() => {
    cursorRef.current = cursorKey
    rowsRef.current = rows
  })

  // Key of the row we last activated ourselves, so the selection echo it
  // produces (a render later, via the URL) doesn't trigger a re-snap.
  const selfRef = useRef<string | null>(null)
  const prevSelRef = useRef<string | null>(selectedKey)

  const activate = useCallback(
    (key: string) => {
      const row = rowsRef.current.find((r) => r.key === key)
      if (!row) return
      setCursorKey(key)
      selfRef.current = row.key
      onActivate(row)
    },
    [onActivate],
  )

  // Snap the cursor on an *external* selection change only. Gated on an actual
  // `selectedKey` change (not row churn) so a keyboard move — where the cursor
  // already leads and the URL lags — isn't snapped back.
  useEffect(() => {
    const changed = prevSelRef.current !== selectedKey
    prevSelRef.current = selectedKey
    if (!changed) return
    if (selectedKey == null) {
      selfRef.current = null
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCursorKey(null)
      return
    }
    if (selfRef.current === selectedKey) {
      selfRef.current = null
      return
    }
    selfRef.current = null
    if (cursorRef.current === selectedKey) return
    setCursorKey(selectedKey)
  }, [selectedKey])

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return
      if (isTyping(e.target)) return
      if (!isArrowKey(e.key)) return
      if (rowsRef.current.length === 0) return
      e.preventDefault()
      const action = arrowNavStep(rowsRef.current, cursorRef.current ?? selectedKey, e.key)
      if (action.type === 'activate') activate(action.key)
      else if (action.type === 'toggle') onToggle(action.key)
    },
    [activate, onToggle, selectedKey],
  )

  return { onKeyDown }
}
