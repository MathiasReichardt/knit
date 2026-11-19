import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { QueryClient } from '@tanstack/react-query'
import { connectEvents } from './sse'
import { api } from './client'
import { useActivity } from '../stores/activity'

class FakeEventSource {
  static all: FakeEventSource[] = []
  closed = false
  private listeners = new Map<string, ((e: unknown) => void)[]>()
  constructor() {
    FakeEventSource.all.push(this)
  }
  addEventListener(type: string, fn: (e: unknown) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), fn])
  }
  close() {
    this.closed = true
  }
  emit(type: string) {
    for (const fn of this.listeners.get(type) ?? []) fn({})
  }
}

const live = () => FakeEventSource.all.filter((s) => !s.closed)
const last = () => FakeEventSource.all[FakeEventSource.all.length - 1]

let hidden = false
const setHidden = (v: boolean) => {
  hidden = v
  document.dispatchEvent(new Event('visibilitychange'))
}

let invalidate: ReturnType<typeof vi.fn>
let client: QueryClient
let activity: ReturnType<typeof vi.spyOn>
let disconnect: () => void

const connect = () => {
  disconnect = connectEvents({ client })
}

beforeEach(() => {
  vi.useFakeTimers()
  FakeEventSource.all = []
  vi.stubGlobal('EventSource', FakeEventSource)
  hidden = false
  Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden })
  invalidate = vi.fn(() => Promise.resolve())
  client = { invalidateQueries: invalidate } as unknown as QueryClient
  activity = vi.spyOn(api, 'activity').mockResolvedValue([])
  useActivity.setState({ open: false })
})

afterEach(() => {
  disconnect()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const resynced = () =>
  invalidate.mock.calls.filter((c) => c[1]?.cancelRefetch === false).map((c) => JSON.stringify(c[0].queryKey))
const resyncs = () => resynced().filter((k) => k === '["item"]').length

describe('connectEvents resync', () => {
  it('does not refetch on the first open', () => {
    connect()
    last().emit('open')
    expect(resyncs()).toBe(0)
  })

  it('resyncs when the stream comes back after an error', () => {
    connect()
    last().emit('open')
    last().emit('error')
    vi.advanceTimersByTime(500)
    expect(live()).toHaveLength(1)
    last().emit('open')
    expect(resyncs()).toBe(1)
  })

  it('refetches what events can change, not queries no event touches', () => {
    connect()
    last().emit('open')
    last().emit('error')
    vi.advanceTimersByTime(500)
    last().emit('open')
    expect(resynced()).toEqual(expect.arrayContaining(['["items"]', '["item"]', '["context"]', '["kb"]', '["git"]']))
    expect(resynced()).not.toContain('["doctor","verify"]')
    expect(new Set(resynced()).size).toBe(resynced().length)
  })

  it('resyncs on a resync event from the server', () => {
    connect()
    last().emit('open')
    last().emit('resync')
    expect(resyncs()).toBe(1)
  })

  it('refetches after a failed first connect, once one succeeds', () => {
    connect()
    last().emit('error')
    vi.advanceTimersByTime(500)
    last().emit('open')
    expect(resyncs()).toBe(1)
  })

  it('reloads the activity buffer only while the drawer is open', () => {
    connect()
    last().emit('open')
    last().emit('error')
    vi.advanceTimersByTime(500)
    last().emit('open')
    expect(activity).not.toHaveBeenCalled()

    useActivity.setState({ open: true })
    last().emit('error')
    vi.advanceTimersByTime(500)
    last().emit('open')
    expect(activity).toHaveBeenCalledTimes(1)
  })
})

describe('connectEvents visibility', () => {
  it('closes the stream while hidden and resyncs when shown', () => {
    connect()
    last().emit('open')
    setHidden(true)
    expect(live()).toHaveLength(0)

    setHidden(false)
    expect(live()).toHaveLength(1)
    last().emit('open')
    expect(resyncs()).toBe(1)
  })

  it('opens no stream for a tab loaded in the background until it is shown', () => {
    hidden = true
    connect()
    expect(FakeEventSource.all).toHaveLength(0)

    setHidden(false)
    last().emit('open')
    expect(resyncs()).toBe(1)
  })

  it('drops a pending reconnect while hidden and connects on show', () => {
    connect()
    last().emit('error')
    setHidden(true)
    vi.advanceTimersByTime(60_000)
    expect(live()).toHaveLength(0)

    setHidden(false)
    expect(live()).toHaveLength(1)
  })

  it('ignores visibility changes after disconnect', () => {
    connect()
    disconnect()
    setHidden(true)
    setHidden(false)
    expect(live()).toHaveLength(0)
  })
})
