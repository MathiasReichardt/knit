import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, ArrowUpRight } from 'lucide-react'
import type { Paged } from '../api/paging'
import type { IconCmp } from '../lib/glyphs'

const VISIBLE = 5
const CAP = 20
// Tailwind arbitrary basis: 5 cards + 4 gaps (gap-3 = 0.75rem) fill the row.
const CARD_BASIS = 'basis-[calc((100%_-_3rem)/5)]'

interface ViewAll {
  to: string
  search?: Record<string, unknown>
}

interface RowCarouselProps<T> {
  title: string
  icon: IconCmp
  viewAll: ViewAll
  queryKey: readonly unknown[]
  fetchPage: (limit: number) => Promise<Paged<T>>
  renderCard: (item: T) => ReactNode
  getKey: (item: T) => string
  emptyText: string
  /** Extra header control (the Items row's `me` toggle). */
  headerExtra?: ReactNode
  /** Changing this resets the window to the start (e.g. the `me` toggle flips). */
  resetToken?: unknown
  /** This row's index in the landing grid (keyboard nav). */
  rowIndex?: number
  /** Reports the number of focusable cards (incl. the trailing View-all card). */
  onSlideCount?: (rowIndex: number, count: number) => void
  /** The card column the grid cursor rests on in this row, or null. */
  focusCol?: number | null
}

// One landing row: a window of ≤5 cards over a single capped fetch (≤20 cards).
// The ← → buttons slide the window one card at a time so the user keeps
// visual context. When more cards exist beyond the cap (`hasMore`), a trailing
// "View all" card closes the row — signalling "this is the end of the peek, but
// there's more in the full view."
export function RowCarousel<T>({
  title,
  icon: Icon,
  viewAll,
  queryKey,
  fetchPage,
  renderCard,
  getKey,
  emptyText,
  headerExtra,
  resetToken,
  rowIndex,
  onSlideCount,
  focusCol = null,
}: RowCarouselProps<T>) {
  // keepPreviousData: on a feed switch (the `me` toggle changes the query key)
  // hold the old cards in place until the new ones arrive, so the row doesn't
  // collapse to the loading placeholder and bump every row below it.
  // staleTime: the landing page refreshes on arrival / window-focus (TanStack
  // defaults); 10s just dedupes rapid back-and-forth nav. Deliberately NOT
  // SSE-live — a recency-sorted grid would reorder under the cursor.
  const q = useQuery({
    queryKey,
    queryFn: () => fetchPage(CAP),
    placeholderData: keepPreviousData,
    staleTime: 10_000,
  })
  const rows = q.data?.rows ?? []
  const hasMore = !!q.data?.hasMore
  const total = q.data?.total ?? 0
  const slideCount = rows.length + (hasMore ? 1 : 0)
  const maxStart = Math.max(0, slideCount - VISIBLE)

  const [start, setStart] = useState(0)
  // Adjust state during render (not in an effect): reset on a feed switch, clamp
  // if the data shrank, and page the window when the keyboard cursor steps past
  // an edge so the focused card stays visible.
  const [prevToken, setPrevToken] = useState(resetToken)
  const [prevFocus, setPrevFocus] = useState<number | null>(focusCol)
  let desired = start
  if (prevToken !== resetToken) {
    desired = 0
  } else if (focusCol != null && focusCol !== prevFocus) {
    if (focusCol < start) desired = focusCol
    else if (focusCol > start + VISIBLE - 1) desired = focusCol - VISIBLE + 1
  }
  desired = Math.max(0, Math.min(desired, maxStart))
  if (prevToken !== resetToken) setPrevToken(resetToken)
  if (focusCol !== prevFocus) setPrevFocus(focusCol)
  if (desired !== start) setStart(desired)

  // Report the focusable card count up to the landing grid.
  useEffect(() => {
    if (rowIndex != null) onSlideCount?.(rowIndex, slideCount)
  }, [rowIndex, slideCount, onSlideCount])

  // Smooth-scroll the window by whole cards. DOM sync only — no setState here.
  const vpRef = useRef<HTMLDivElement>(null)
  const sectionRef = useRef<HTMLElement>(null)
  useEffect(() => {
    const vp = vpRef.current
    if (!vp || vp.children.length === 0) return
    const a = vp.children[0] as HTMLElement
    const b = vp.children[1] as HTMLElement | undefined
    const step = b ? b.offsetLeft - a.offsetLeft : a.offsetWidth
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    vp.scrollTo({ left: start * step, behavior: reduce ? 'auto' : 'smooth' })
  }, [start, slideCount])

  // Paint the persistent cursor ring (a class, so it survives window blur) and
  // move DOM focus so Enter activates the card's link. preventScroll keeps the
  // carousel's horizontal position under our scrollTo; the section scroll only
  // nudges the page vertically to reveal the active row.
  useEffect(() => {
    const cards = vpRef.current?.querySelectorAll<HTMLElement>('[data-landing-card]')
    cards?.forEach((el, i) => el.classList.toggle('knit-card-cursor', i === focusCol))
    if (focusCol == null) return
    cards?.[focusCol]?.focus({ preventScroll: true })
    sectionRef.current?.scrollIntoView({ block: 'nearest' })
  }, [focusCol, slideCount])

  // Restore DOM focus to the cursor card when the window regains focus, so Enter
  // keeps working after an alt-tab (the ring persists on its own via the class).
  useEffect(() => {
    if (focusCol == null) return
    const refocus = () => {
      const cards = vpRef.current?.querySelectorAll<HTMLElement>('[data-landing-card]')
      cards?.[focusCol]?.focus({ preventScroll: true })
    }
    window.addEventListener('focus', refocus)
    return () => window.removeEventListener('focus', refocus)
  }, [focusCol])

  const canPrev = start > 0
  const canNext = start < maxStart

  return (
    <section ref={sectionRef} className="flex flex-col gap-2">
      <div className="flex min-h-6 items-center gap-2">
        <Icon className="h-4 w-4 text-zinc-500 dark:text-zinc-400" aria-hidden />
        <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-200">{title}</h2>
        {total > 0 && (
          <span className="rounded-full bg-zinc-100 px-1.5 py-0.5 text-xs tabular-nums text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400">
            {total}
          </span>
        )}
        {headerExtra}
        <div className="ml-auto flex items-center gap-1">
          {maxStart > 0 && (
            <>
              <button
                type="button"
                onClick={() => setStart((s) => Math.max(0, s - 1))}
                disabled={!canPrev}
                title="Previous"
                aria-label={`Previous ${title} card`}
                className="rounded p-1 text-zinc-500 hover:bg-zinc-100 disabled:opacity-30 dark:text-zinc-400 dark:hover:bg-zinc-800"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={() => setStart((s) => Math.min(maxStart, s + 1))}
                disabled={!canNext}
                title="More"
                aria-label={`More ${title} cards`}
                className="rounded p-1 text-zinc-500 hover:bg-zinc-100 disabled:opacity-30 dark:text-zinc-400 dark:hover:bg-zinc-800"
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </>
          )}
          <Link
            to={viewAll.to as never}
            search={viewAll.search as never}
            className="ml-1 inline-flex items-center gap-0.5 text-xs text-blue-600 hover:underline dark:text-blue-400"
          >
            View all
            <ArrowUpRight className="h-3.5 w-3.5" aria-hidden />
          </Link>
        </div>
      </div>

      {q.isLoading ? (
        <CardSkeletons />
      ) : q.error ? (
        <div className="flex h-32 items-center text-sm text-red-600">Failed to load.</div>
      ) : rows.length === 0 ? (
        <div className="flex h-32 items-center text-sm text-zinc-400 dark:text-zinc-500">{emptyText}</div>
      ) : (
        <div
          ref={vpRef}
          className={
            // py-1 gives the hover lift (-translate-y) room so its top edge
            // isn't clipped by overflow-hidden.
            'flex h-32 gap-3 overflow-hidden py-1 transition-opacity ' +
            // Dim the prior cards while a feed switch (My/All) refetches; the
            // window stays in place via keepPreviousData, so this reads as a
            // soft "refreshing" rather than a collapse.
            (q.isPlaceholderData ? 'opacity-60' : 'opacity-100')
          }
        >
          {rows.map((row) => (
            <div key={getKey(row)} className={'h-full min-w-0 shrink-0 grow-0 ' + CARD_BASIS}>
              {renderCard(row)}
            </div>
          ))}
          {hasMore && (
            <div className={'h-full shrink-0 grow-0 ' + CARD_BASIS}>
              <ViewAllCard to={viewAll.to} search={viewAll.search} />
            </div>
          )}
        </div>
      )}
    </section>
  )
}

// Cold-load placeholder: VISIBLE pulsing cards at the exact card geometry, so
// the row holds its shape (no shift) while the first fetch is in flight.
function CardSkeletons() {
  return (
    <div className="flex h-32 gap-3 overflow-hidden py-1" aria-hidden>
      {Array.from({ length: VISIBLE }, (_, i) => (
        <div key={i} className={'h-full shrink-0 grow-0 ' + CARD_BASIS}>
          <div className="h-full w-full animate-pulse rounded-lg border border-zinc-200 bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-800/50" />
        </div>
      ))}
    </div>
  )
}

// Trailing card at the end of a capped row — same target as the header link.
function ViewAllCard({ to, search }: ViewAll) {
  return (
    <Link
      to={to as never}
      search={search as never}
      data-landing-card=""
      className="group flex h-full w-full flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-zinc-300 bg-zinc-50 p-3 text-center text-sm text-zinc-500 transition hover:border-zinc-400 hover:text-zinc-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500 dark:border-zinc-700 dark:bg-zinc-900/50 dark:text-zinc-400 dark:hover:text-zinc-200"
    >
      <ArrowUpRight className="h-5 w-5" aria-hidden />
      View all
    </Link>
  )
}
