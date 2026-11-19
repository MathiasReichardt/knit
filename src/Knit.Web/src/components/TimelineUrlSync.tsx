import { useEffect, useRef, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useTimelineFilters } from '../stores/timelineFilters'
import { useTimelinePrefs } from '../stores/timelinePrefs'
import {
  currentSpec,
  sameSpec,
  searchToSpec,
  specToSearch,
  type TimelineSearch,
  type TimelineSpec,
} from '../lib/timelineSearch'
import { changedKeys, createHistoryPolicy, withoutHistory } from '../lib/urlHistory'

function apply(spec: TimelineSpec) {
  useTimelinePrefs.setState(spec.view)
  useTimelineFilters.getState().applyLink(spec.filters)
}

/**
 * Bridge between the timeline URL and its two stores (view prefs + filters),
 * the timeline's counterpart to `NavigatorUrlSync`. A URL naming a view wins
 * over what is remembered; a bare `/timeline` gets the remembered view written
 * into it. Nothing is written back until the filters are bound to the repo —
 * before that the store holds defaults, not the repo's filters.
 */
export function TimelineUrlSync({ search, ready }: { search: TimelineSearch; ready: boolean }) {
  useState(() => {
    const spec = searchToSpec(search)
    if (spec) apply(spec)
    return null
  })

  const navigate = useNavigate()
  const writeTimer = useRef<number | undefined>(undefined)
  const lastWritten = useRef<TimelineSearch>(search)
  const pendingPush = useRef(false)
  const [policy] = useState(() => createHistoryPolicy())

  // Stores → URL. Selection (`id` / `ms`) is URL-only, so it is carried over.
  // Every view or filter change is a history step.
  useEffect(() => {
    if (!ready) return
    const write = () => {
      const next = specToSearch(currentSpec())
      if (sameSpec(next, lastWritten.current)) return
      const changed = changedKeys(lastWritten.current, next).filter((k) => k !== 'id' && k !== 'ms')
      if (policy.shouldPush(changed)) pendingPush.current = true
      lastWritten.current = next
      window.clearTimeout(writeTimer.current)
      writeTimer.current = window.setTimeout(() => {
        const push = pendingPush.current
        pendingPush.current = false
        void navigate({
          to: '.',
          search: (prev: TimelineSearch) => ({ ...next, id: prev.id, ms: prev.ms }) as never,
          replace: !push,
        })
      }, 250)
    }
    const unsubPrefs = useTimelinePrefs.subscribe(write)
    const unsubFilters = useTimelineFilters.subscribe(write)
    return () => {
      unsubPrefs()
      unsubFilters()
      window.clearTimeout(writeTimer.current)
    }
  }, [navigate, ready, policy])

  // URL → stores (back/forward, a pasted link, a bare /timeline from the rail).
  useEffect(() => {
    if (!ready) return
    const spec = searchToSpec(search)
    const current = specToSearch(currentSpec())
    if (!spec) {
      window.clearTimeout(writeTimer.current)
      pendingPush.current = false
      lastWritten.current = current
      void navigate({
        to: '.',
        search: (prev: TimelineSearch) => ({ ...current, id: prev.id, ms: prev.ms }) as never,
        replace: true,
      })
      return
    }
    // Canonical form: a URL spelling a default out loud must not read as a
    // change and push a duplicate step.
    const wanted = specToSearch(spec)
    if (!sameSpec(wanted, current)) {
      // `apply` sets two stores; the write between them sees a half-applied
      // view, which must not become a history step of its own.
      withoutHistory(() => apply(spec))
      window.clearTimeout(writeTimer.current)
      pendingPush.current = false
    }
    lastWritten.current = wanted
  }, [search, ready, navigate])

  return null
}
