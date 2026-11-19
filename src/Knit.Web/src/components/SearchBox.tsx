import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useRouter } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { BookOpen, FileText, FolderKanban, Loader2, Search, Square } from 'lucide-react'
import { api, type SearchHit } from '../api/client'
import { designsApi } from '../api/designs'
import { useJumpIntent } from '../stores/jumpIntent'
import { useItemReveal } from '../stores/itemReveal'
import { highlightSegments } from '../lib/searchHighlight'
import { isNewTabKey, searchHitTarget } from '../lib/searchTarget'
import { RowLink } from './RowLink'

// Below this the search box shows a hint instead of querying — a 1-char
// substring is all noise. Mirrors the server/CLI floor.
const MIN_QUERY = 2

type Kind = 'item' | 'kb' | 'design' | 'project'

const KIND_LABEL: Record<Kind, string> = {
  item: 'Items',
  kb: 'KB',
  design: 'Designs',
  project: 'Projects',
}

const KIND_ICON: Record<Kind, typeof Square> = {
  item: Square,
  kb: BookOpen,
  design: FileText,
  project: FolderKanban,
}

const KIND_ORDER: Kind[] = ['item', 'kb', 'design', 'project']

export function SearchBox() {
  const inputRef = useRef<HTMLInputElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const navigate = useNavigate()
  const router = useRouter()
  const requestJump = useJumpIntent((s) => s.request)
  const requestReveal = useItemReveal((s) => s.request)

  // 150ms debounce so the user can keep typing without firing N requests.
  const debounced = useDebounced(q.trim(), 150)

  const queryEnabled = debounced.length >= MIN_QUERY && open
  const search = useQuery({
    queryKey: ['search', debounced],
    queryFn: () => api.search(debounced, 'all'),
    enabled: queryEnabled,
    staleTime: 5_000,
  })

  const hits = useMemo(() => search.data?.hits ?? [], [search.data])

  const grouped = useMemo(() => groupByKind(hits), [hits])

  // The popover renders hits grouped by kind, so the keyboard highlight — and
  // what Enter picks — must index this grouped order, not the raw server order
  // (a title hit of another kind can otherwise rank first in `hits` and get
  // picked instead of the visually-highlighted row).
  const flatIndex = useMemo(() => {
    const out: SearchHit[] = []
    for (const k of KIND_ORDER) {
      out.push(...grouped[k])
    }
    return out
  }, [grouped])

  // Cmd/Ctrl+K or `/` (when not typing) focuses the box.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey
      if ((mod && e.key.toLowerCase() === 'k') || (e.key === '/' && !isTyping(e.target))) {
        e.preventDefault()
        inputRef.current?.focus()
        inputRef.current?.select()
        setOpen(true)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Click-outside dismisses the popover.
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const inInput = inputRef.current?.contains(e.target as Node)
      const inPop = popoverRef.current?.contains(e.target as Node)
      if (!inInput && !inPop) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  // Reset the keyboard-navigable highlight to the top when the result set
  // changes. Not derivable — highlight moves independently via arrow keys.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setHighlight(0)
  }, [hits])

  const onPick = (hit: SearchHit) => {
    // Keep the query text so the box still shows what was searched; just close
    // the popover and drop focus. Re-focusing reopens the same results.
    setOpen(false)
    inputRef.current?.blur()
    if (hit.kind === 'item') {
      requestReveal(hit.id)
      void navigate({ to: '/items', search: { id: hit.id } })
      // Carry the matched region + query so the details pane can jump to it and
      // highlight the word. A title-only match is a no-op there — the
      // entity opens title-first.
      requestJump({ kind: 'item', targetId: hit.id, field: hit.field, bodyOffset: hit.bodyOffset, query: debounced })
    } else if (hit.kind === 'design') {
      // hit.id is the design slug; jump-to-occurrence works the same as items.
      // Probe `completed` so a completed doc opens on the right tree tab (and is
      // revealed there), not just in the right-hand pane.
      requestJump({ kind: 'design', targetId: hit.id, field: hit.field, bodyOffset: hit.bodyOffset, query: debounced })
      void designsApi.get(hit.id).then(
        (doc) => navigate({ to: '/designs', search: { tab: doc.completed ? 'completed' : 'active', slug: hit.id } as never }),
        () => navigate({ to: '/designs', search: { slug: hit.id } as never }),
      )
    } else {
      const target = searchHitTarget(hit)
      if (!target) return
      void navigate(target as never)
      if (hit.kind === 'kb')
        requestJump({ kind: 'kb', targetId: hit.id, field: hit.field, bodyOffset: hit.bodyOffset, query: debounced })
    }
  }

  // The popover stays open, so several hits can go to tabs in a row.
  const openInNewTab = (hit: SearchHit) => {
    const target = searchHitTarget(hit)
    if (target) window.open(router.buildLocation(target as never).href, '_blank', 'noopener')
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') {
      if (q) setQ('')
      else { setOpen(false); inputRef.current?.blur() }
      return
    }
    if (!open) return
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setHighlight((h) => Math.min(h + 1, Math.max(flatIndex.length - 1, 0)))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setHighlight((h) => Math.max(h - 1, 0))
    } else if (e.key === 'Enter') {
      if (flatIndex[highlight]) {
        e.preventDefault()
        if (isNewTabKey(e)) openInNewTab(flatIndex[highlight]!)
        else onPick(flatIndex[highlight]!)
      }
    }
  }

  return (
    <div className="relative flex-1 max-w-md">
      <div className="relative">
        <Search
          className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-400"
          aria-hidden
        />
        <input
          ref={inputRef}
          type="search"
          value={q}
          onChange={(e) => { setQ(e.target.value); setOpen(true) }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder="Search (Ctrl/Cmd+K)"
          aria-label="Global search"
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls="search-results"
          className="w-full h-8 rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 pl-7 pr-3 text-sm text-zinc-800 dark:text-zinc-200 placeholder:text-zinc-400 dark:placeholder:text-zinc-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        {search.isLoading && (
          <Loader2
            className="absolute right-2 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-400 animate-spin"
            aria-hidden
          />
        )}
      </div>
      {open && q.trim().length > 0 && (
        <div
          ref={popoverRef}
          id="search-results"
          role="listbox"
          className="absolute left-0 right-0 mt-1 max-h-[60vh] overflow-y-auto rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-lg z-30"
        >
          {q.trim().length < MIN_QUERY && (
            <div className="px-3 py-3 text-xs text-zinc-500">Type at least {MIN_QUERY} characters.</div>
          )}
          {q.trim().length >= MIN_QUERY && search.isLoading && (
            <div className="px-3 py-3 text-xs text-zinc-500">Searching…</div>
          )}
          {q.trim().length >= MIN_QUERY && !search.isLoading && hits.length === 0 && (
            <div className="px-3 py-3 text-xs text-zinc-500">No matches.</div>
          )}
          {hits.length > 0 && (
            <>
              {KIND_ORDER.map((k) => {
                const first = grouped[k][0]
                if (!first) return null
                return (
                  <KindGroup
                    key={k}
                    kind={k}
                    hits={grouped[k]}
                    query={debounced}
                    startIndex={flatIndex.indexOf(first)}
                    highlight={highlight}
                    onPick={onPick}
                  />
                )
              })}
              {search.data?.clipped && (
                <div className="px-3 py-1.5 text-[10px] text-zinc-500 bg-zinc-50 dark:bg-zinc-900/50 border-t border-zinc-100 dark:border-zinc-900">
                  Showing the first {hits.length} matches — refine your search to see more.
                </div>
              )}
              <div className="px-3 py-1.5 text-[10px] text-zinc-500 border-t border-zinc-100 dark:border-zinc-900">
                <Key>Enter</Key> open · <Key>Ctrl+Enter</Key> new tab
              </div>
            </>
          )}
        </div>
      )}
    </div>
  )
}

function KindGroup({
  kind,
  hits,
  query,
  startIndex,
  highlight,
  onPick,
}: {
  kind: Kind
  hits: SearchHit[]
  query: string
  startIndex: number
  highlight: number
  onPick: (h: SearchHit) => void
}) {
  if (hits.length === 0) return null
  const Icon = KIND_ICON[kind]
  return (
    <div className="border-b border-zinc-100 dark:border-zinc-900 last:border-0">
      <div className="px-3 py-1 text-[10px] uppercase tracking-wide text-zinc-500 bg-zinc-50 dark:bg-zinc-900/50">
        {KIND_LABEL[kind]}
      </div>
      {hits.map((h, i) => {
        const flat = startIndex + i
        const active = flat === highlight
        const target = searchHitTarget(h)
        return (
          <RowLink
            key={`${kind}:${h.id}`}
            to={target?.to ?? '/'}
            search={target?.search}
            disabled={!target}
            role="option"
            aria-selected={active}
            onSelect={() => onPick(h)}
            className={
              'group w-full text-left px-3 py-1.5 flex items-start gap-2 text-xs cursor-pointer ' +
              (active
                ? 'bg-blue-50 dark:bg-blue-950/40'
                : 'hover:bg-zinc-50 dark:hover:bg-zinc-900/60')
            }
            title={`Open ${h.title}`}
          >
            <Icon className="h-3.5 w-3.5 shrink-0 mt-0.5 text-zinc-500" aria-hidden />
            <div className="flex-1 min-w-0">
              {/* For a title hit the snippet IS the title (windowed) — render it
                  as the label so the match is highlighted and the title isn't
                  repeated below. The server snippet is plain text; we
                  highlight it client-side with the same matcher as the scanner,
                  so no divergence and no dangerouslySetInnerHTML. */}
              {h.field === 'title' && h.snippet ? (
                <div className="font-medium text-zinc-800 dark:text-zinc-200 truncate">
                  <HighlightedText text={h.snippet} query={query} />
                </div>
              ) : (
                <div className="font-medium text-zinc-800 dark:text-zinc-200 truncate">{h.title}</div>
              )}
              {h.snippet && h.field !== 'title' && (
                <div className="text-zinc-500 dark:text-zinc-400 truncate">
                  <HighlightedText text={h.snippet} query={query} />
                </div>
              )}
              <div className="font-mono text-[10px] text-zinc-400 truncate">{h.id}</div>
            </div>
          </RowLink>
        )
      })}
    </div>
  )
}

function Key({ children }: { children: React.ReactNode }) {
  return <kbd className="font-mono text-zinc-600 dark:text-zinc-300">{children}</kbd>
}

// Render plain snippet text with the query's matches wrapped in <mark>. Uses
// the same matcher (`highlightSegments` → `rangesInText`) as the detail-pane
// jump highlight, so the result list can never diverge from what matched.
function HighlightedText({ text, query }: { text: string; query: string }) {
  const segments = useMemo(() => highlightSegments(text, query), [text, query])
  return (
    <>
      {segments.map((s, i) =>
        s.mark ? (
          <mark key={i} className="bg-amber-200 dark:bg-amber-500/40 text-inherit rounded-[2px]">
            {s.text}
          </mark>
        ) : (
          <span key={i}>{s.text}</span>
        ),
      )}
    </>
  )
}

function groupByKind(hits: SearchHit[]): Record<Kind, SearchHit[]> {
  const out: Record<Kind, SearchHit[]> = { item: [], kb: [], design: [], project: [] }
  for (const h of hits) out[h.kind].push(h)
  return out
}

function useDebounced<T>(value: T, delayMs: number): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = window.setTimeout(() => setV(value), delayMs)
    return () => window.clearTimeout(t)
  }, [value, delayMs])
  return v
}

function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || target.isContentEditable
}
