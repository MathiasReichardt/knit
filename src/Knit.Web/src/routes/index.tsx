import { createFileRoute, Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState } from 'react'
import { FileText, BookOpen, BookOpenText, ListTodo, FolderKanban, ScrollText } from 'lucide-react'
import logoBright from '@logo/logo-bright.svg'
import logoDark from '@logo/logo-dark.svg'
import { api, qk } from '../api/client'
import { conventionsApi, conventionsQk, hasRepoConventions } from '../api/conventions'
import { designsApi, designsQk } from '../api/designs'
import { kbApi, kbQk } from '../api/kb'
import { projectsApi, projectsQk } from '../api/projects'
import { useStatusPage } from '../hooks/useStatusPage'
import { StaleDot } from '../components/StaleDot'
import { RowCarousel } from '../components/RowCarousel'
import { DesignCard, KbCard, ItemCard, ProjectCard } from '../components/LandingCards'
import { useLandingPrefs } from '../stores/landingPrefs'
import { useResolvedTheme } from '../stores/theme'
import { isArrowKey, isTyping } from '../lib/arrowNav'
import { gridNavStep, firstCell, type GridCursor } from '../lib/landingGridNav'

export const Route = createFileRoute('/')({
  component: Home,
})

function Home() {
  const mine = useLandingPrefs((s) => s.mine)
  const setMine = useLandingPrefs((s) => s.setMine)
  const resolved = useResolvedTheme()
  const health = useQuery({ queryKey: qk.health, queryFn: api.health })
  const conventions = useQuery({ queryKey: conventionsQk.get(), queryFn: conventionsApi.get })
  const statusPage = useStatusPage().data

  // 2-D card-grid keyboard nav. Rows report their card counts; arrow keys
  // step a shared cursor, which each row turns into DOM focus + carousel paging.
  const [counts, setCounts] = useState<number[]>([0, 0, 0, 0])
  const [cursor, setCursor] = useState<GridCursor | null>(null)
  const countsRef = useRef(counts)
  const cursorRef = useRef(cursor)
  useEffect(() => {
    countsRef.current = counts
    cursorRef.current = cursor
  })

  const setCount = useCallback((i: number, n: number) => {
    setCounts((c) => (c[i] === n ? c : c.map((v, j) => (j === i ? n : v))))
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (isTyping(e.target) || !isArrowKey(e.key)) return
      const c = countsRef.current
      if (c.every((n) => n === 0)) return
      const cur = cursorRef.current ?? firstCell(c)
      if (!cur) return
      e.preventDefault()
      setCursor(gridNavStep(c, cur, e.key))
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  return (
    <main className="mx-auto flex h-full w-full max-w-5xl flex-col gap-8 overflow-y-auto p-6">
      <div className="flex flex-col items-center gap-2 text-center">
        <img src={resolved === 'dark' ? logoDark : logoBright} alt="Knit" className="h-24 w-24" />
        <h1 className="text-2xl font-semibold text-zinc-900 dark:text-zinc-100">Knit</h1>
        <p className="text-sm text-zinc-500 dark:text-zinc-400">
          {health.isLoading && 'Connecting…'}
          {health.error && '(api unreachable)'}
          {health.data && `v${health.data.version}`}
        </p>
        <div className="flex flex-wrap items-center justify-center gap-x-4 gap-y-1">
          {/* No "add" link when there are none: most repos never need one. */}
          {hasRepoConventions(conventions.data) && (
            <Link
              to="/settings"
              search={{ tab: 'conventions' }}
              className="inline-flex items-center gap-1 text-xs text-zinc-500 hover:text-blue-600 dark:text-zinc-400"
            >
              <ScrollText className="h-3.5 w-3.5" aria-hidden />
              Read this repo's conventions
            </Link>
          )}
          {statusPage?.exists && (
            <Link
              to="/status-report"
              className="inline-flex items-center gap-1 text-xs text-zinc-500 hover:text-blue-600 dark:text-zinc-400"
            >
              <BookOpenText className="h-3.5 w-3.5" aria-hidden />
              Read the status report
              {statusPage.stale && <StaleDot generated={statusPage.generated} />}
            </Link>
          )}
        </div>
      </div>

      <div className="flex items-center gap-3">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
          Pick up where you left off
        </h2>
        <div className="ml-auto inline-flex overflow-hidden rounded-md border border-zinc-200 text-xs dark:border-zinc-700">
          <button
            type="button"
            onClick={() => setMine(true)}
            aria-pressed={mine}
            className={
              'px-3 py-1 ' +
              (mine
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800')
            }
          >
            My items
          </button>
          <button
            type="button"
            onClick={() => setMine(false)}
            aria-pressed={!mine}
            className={
              'px-3 py-1 ' +
              (!mine
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800')
            }
          >
            All
          </button>
        </div>
      </div>

      {/* Rows read top-down by altitude: project → item → design → KB. The
          projects row is always present — with no projects the endpoint
          returns an all-items stand-in, so `emptyText` never shows. */}
      <RowCarousel
        title="Projects"
        icon={FolderKanban}
        viewAll={{ to: '/projects' }}
        queryKey={projectsQk.landing}
        fetchPage={(limit) => projectsApi.landing(limit)}
        renderCard={(project) => <ProjectCard project={project} />}
        getKey={(project) => project.slug ?? 'all-items'}
        emptyText="No projects."
        rowIndex={0}
        onSlideCount={setCount}
        focusCol={cursor?.row === 0 ? cursor.col : null}
      />

      <RowCarousel
        title="Items"
        icon={ListTodo}
        viewAll={{ to: '/items', search: {} }}
        queryKey={qk.itemsLanding(mine)}
        fetchPage={(limit) => api.itemsLanding(limit, mine)}
        renderCard={(item) => <ItemCard item={item} />}
        getKey={(item) => item.id}
        resetToken={mine}
        emptyText={mine ? 'Nothing assigned to you in progress — start something from the tree.' : 'Nothing in progress.'}
        rowIndex={1}
        onSlideCount={setCount}
        focusCol={cursor?.row === 1 ? cursor.col : null}
      />

      <RowCarousel
        title="Designs"
        icon={FileText}
        viewAll={{ to: '/designs' }}
        queryKey={designsQk.landing}
        fetchPage={(limit) => designsApi.landing(limit)}
        renderCard={(design) => <DesignCard design={design} />}
        getKey={(design) => design.slug}
        emptyText="No open designs."
        rowIndex={2}
        onSlideCount={setCount}
        focusCol={cursor?.row === 2 ? cursor.col : null}
      />

      <RowCarousel
        title="Knowledge base"
        icon={BookOpen}
        viewAll={{ to: '/kb' }}
        queryKey={kbQk.recent}
        fetchPage={(limit) => kbApi.recent(limit)}
        renderCard={(entry) => <KbCard entry={entry} />}
        getKey={(entry) => entry.ref}
        emptyText="No KB entries yet."
        rowIndex={3}
        onSlideCount={setCount}
        focusCol={cursor?.row === 3 ? cursor.col : null}
      />
    </main>
  )
}
