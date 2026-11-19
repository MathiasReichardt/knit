import { useEffect, useRef, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useNavigator } from '../stores/navigator'
import {
  type NavigatorSearch,
  URL_STATE_KEYS,
  filterPart,
  isSpec,
  searchEqual,
  searchToState,
  stateToSearch,
} from '../lib/navigatorSearch'
import { changedKeys, createHistoryPolicy } from '../lib/urlHistory'

interface Props {
  search: NavigatorSearch
  ready: boolean
}

/**
 * Bidirectional bridge between the URL search params and the navigator store.
 * A URL that names any filter key is the complete view (a key it leaves out
 * means the default); a bare `/items` shows what the repo remembers and gets
 * that written into it.
 *
 * - On mount: hydrate the store from the URL before children read state, so
 *   the first query fires already filtered (no unfiltered → filtered flash).
 * - On store change: debounce-write back to the URL. A filter change is a
 *   history step; typing in the query box is one step per burst
 *   (`createHistoryPolicy`).
 * - On back/forward: rebuild the store from the URL.
 *
 * Nothing is written until the filters are bound to the repo — before that the
 * store holds defaults, not the repo's filters.
 */
export function NavigatorUrlSync({ search, ready }: Props) {
  // useState initializer runs synchronously during render, before any
  // sibling's useSyncExternalStore subscription reads the snapshot.
  useState(() => {
    if (isSpec(search)) {
      useNavigator.setState(searchToState(search))
      // Remembered per-repo filters are applied once /api/root resolves; the
      // URL's view has to survive that, or a pasted link would be overwritten
      // by what was last looked at.
      useNavigator.getState().noteUrlKeys([...URL_STATE_KEYS])
    }
    return null
  })

  const navigate = useNavigate()
  const writeTimer = useRef<number | undefined>(undefined)
  const lastWritten = useRef<NavigatorSearch>(filterPart(search))
  const pendingPush = useRef(false)
  const [policy] = useState(() => createHistoryPolicy(['query']))

  // Store → URL.
  useEffect(() => {
    if (!ready) return
    const unsubscribe = useNavigator.subscribe((state) => {
      const next = stateToSearch(state)
      if (searchEqual(next, lastWritten.current)) return
      if (policy.shouldPush(changedKeys(lastWritten.current, next))) pendingPush.current = true
      lastWritten.current = next
      window.clearTimeout(writeTimer.current)
      writeTimer.current = window.setTimeout(() => {
        const push = pendingPush.current
        pendingPush.current = false
        // Preserve the selected-item `id` — it lives in the URL but not in
        // the navigator store, so a naive write would drop it.
        void navigate({
          to: '.',
          search: (prev: NavigatorSearch) =>
            ({ ...next, ...(prev.id ? { id: prev.id } : {}) }) as never,
          replace: !push,
        })
      }, 250)
    })
    return () => {
      unsubscribe()
      window.clearTimeout(writeTimer.current)
    }
  }, [navigate, ready, policy])

  // URL → store (back/forward, a pasted link, a bare /items from the rail).
  useEffect(() => {
    if (!ready) return
    const current = stateToSearch(useNavigator.getState())
    if (!isSpec(search)) {
      window.clearTimeout(writeTimer.current)
      pendingPush.current = false
      lastWritten.current = current
      void navigate({
        to: '.',
        search: (prev: NavigatorSearch) =>
          ({ ...current, ...(prev.id ? { id: prev.id } : {}) }) as never,
        replace: true,
      })
      return
    }
    // Compare in canonical form: a hand-written or older URL may spell the
    // same view differently, and a mismatch here would push a duplicate step.
    const target = searchToState(search)
    const wanted = stateToSearch({ ...useNavigator.getState(), ...target })
    // Our own write echoing back — not a navigation, so the burst goes on.
    if (searchEqual(wanted, lastWritten.current)) return
    window.clearTimeout(writeTimer.current)
    pendingPush.current = false
    policy.reset()
    lastWritten.current = wanted
    if (!searchEqual(current, wanted)) useNavigator.setState(target)
  }, [search, ready, navigate, policy])

  return null
}
