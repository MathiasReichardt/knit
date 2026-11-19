import type { QueryClient, QueryKey } from '@tanstack/react-query'
import type { ActivityEntry, EventKind, KnitEvent } from './types'
import { api, qk } from './client'
import { statusQk } from './status'
import { repoQk } from './repo'
import { projectsQk } from './projects'
import { conventionsQk } from './conventions'
import { configQk } from './config'
import { useActivity } from '../stores/activity'
import { useConnection } from '../stores/connection'

let dragSuppressed = 0

/**
 * Suppress SSE invalidations while a drag is in progress. Increment on
 * drag start, decrement on drop / cancel. The hook surfaces in DnD code
 * paths (1c.4); for 1c.1 we only need the API skeleton.
 */
export function suppressDragRefetch(): () => void {
  dragSuppressed++
  return () => {
    dragSuppressed = Math.max(0, dragSuppressed - 1)
  }
}

interface ConnectOpts {
  client: QueryClient
  url?: string
}

export function connectEvents({ client, url = '/api/events' }: ConnectOpts): () => void {
  let es: EventSource | null = null
  let backoffMs = 500
  const maxBackoff = 30_000
  let closed = false
  let reconnectTimer: number | null = null
  // Events are only seen while a stream is open, and the server keeps no
  // replay. Any later open (a reconnect, or a tab shown again) resyncs.
  let resync = false

  const clearTimer = () => {
    if (reconnectTimer !== null) {
      window.clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
  }

  const open = () => {
    es = new EventSource(url)
    es.addEventListener('open', () => {
      backoffMs = 500
      useConnection.getState().reportOpen()
      if (resync) resyncAll(client)
      resync = true
    })

    // Live entries ride the shared events stream — no extra request. They
    // accumulate (deduped, capped) so the collapsed bar can show a count;
    // the buffer GET only fires when the drawer is actually opened.
    es.addEventListener('activity', (raw: MessageEvent<string>) => {
      let entry: ActivityEntry
      try {
        entry = JSON.parse(raw.data) as ActivityEntry
      } catch {
        return
      }
      useActivity.getState().push(entry)
    })

    const handle = (raw: MessageEvent<string>) => {
      if (dragSuppressed > 0) return
      let evt: KnitEvent
      try {
        evt = JSON.parse(raw.data) as KnitEvent
      } catch {
        return
      }
      invalidate(client, evt)
    }
    es.addEventListener('created', handle)
    es.addEventListener('changed', handle)
    es.addEventListener('deleted', handle)
    // The server's watcher lost events: it can't say what changed.
    es.addEventListener('resync', () => resyncAll(client))

    es.addEventListener('error', () => {
      if (closed) return
      useConnection.getState().reportError()
      resync = true
      es?.close()
      es = null
      const wait = backoffMs
      backoffMs = Math.min(maxBackoff, backoffMs * 2)
      reconnectTimer = window.setTimeout(open, wait)
    })
  }

  // Force an immediate reconnect (the banner's "Check now"): drop the
  // pending backoff timer, reset backoff, and reopen now.
  const reconnectNow = () => {
    if (closed || document.hidden) return
    clearTimer()
    useConnection.getState().setChecking(true)
    es?.close()
    es = null
    backoffMs = 500
    open()
  }

  // Each open stream holds one of the browser's ~6 HTTP/1.1 connections per
  // origin, shared by every tab, so a hidden tab gives its stream back.
  const onVisibility = () => {
    if (closed) return
    if (document.hidden) {
      clearTimer()
      es?.close()
      es = null
      resync = true
    } else if (es === null && reconnectTimer === null) {
      backoffMs = 500
      open()
    }
  }

  useConnection.getState().setReconnectNow(reconnectNow)
  document.addEventListener('visibilitychange', onVisibility)
  // A tab opened in the background loaded its data without a stream.
  if (document.hidden) resync = true
  else open()

  return () => {
    closed = true
    document.removeEventListener('visibilitychange', onVisibility)
    clearTimer()
    es?.close()
    es = null
    useConnection.getState().setReconnectNow(null)
  }
}

// A file a body embeds (an SVG drawn inline) is read through the file view.
const FILES: QueryKey = ['file']

// The queries each event kind can make stale.
const STALE_BY: Record<EventKind, readonly QueryKey[]> = {
  item: [
    qk.items,
    qk.tree,
    qk.timeline,
    statusQk.all,
    // Health carries the repo-wide overdue / due-soon counts for the
    // timeline nav dot — item edits can change them.
    qk.health,
    // The landing projects row is a rollup over items, so an item edit can
    // move its counts / health even when no project doc changed.
    projectsQk.landing,
    // A file beside an item (<id>-<name>.svg) raises the item's event.
    FILES,
  ],
  // `tree_sort` orders the tree and `sort=tree` when the viewer hasn't picked one.
  config: [qk.identity, repoQk.name, qk.health, configQk.get(), qk.tree, qk.items],
  health: [qk.health],
  // The knit repo's git HEAD moved (checkout, rebase) — the header's branch.
  git: [repoQk.git],
  kb: [['kb'], FILES],
  design: [['designs'], FILES],
  slates: [['slates']],
  // Doc-folder changes under .knit/projects/** (readme, roadmap, milestone
  // sub-docs, free-form files). The item side of a project refreshes via
  // the 'item' kind above; this only covers the folder/milestone surface.
  projects: [['projects'], FILES],
  conventions: [conventionsQk.get()],
}

function invalidate(client: QueryClient, evt: KnitEvent) {
  const keys = [...STALE_BY[evt.kind]]
  if (evt.kind === 'item') keys.push(qk.item(evt.id), qk.context(evt.id))
  for (const queryKey of keys) void client.invalidateQueries({ queryKey })
}

// Refetch whatever any missed event could have changed (every item, not just
// one), plus the activity entries missed while no stream was open. Queries no
// event touches (verify, mounts, version) keep their own staleness.
// `cancelRefetch: false` joins react-query's own refetch on focus instead of
// restarting it.
function resyncAll(client: QueryClient) {
  const keys = [...new Set(Object.values(STALE_BY).flat()), ['item'], ['context']]
  for (const queryKey of keys) void client.invalidateQueries({ queryKey }, { cancelRefetch: false })
  if (useActivity.getState().open)
    api.activity().then(
      (e) => useActivity.getState().hydrate(e),
      () => {},
    )
}
