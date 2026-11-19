import { ApiError, apiWrite } from './http'

/**
 * GET /api/status — the rollup behind the summary strip, shared with
 * `knit status`. `percent` is done over non-dismissed. `health` is only
 * present when the scope resolves to exactly one project.
 */
export interface StatusSummary {
  scope: string
  total: number
  done: number
  inProgress: number
  review: number
  todo: number
  dismissed: number
  blocked: number
  overdue: number
  unassigned: number
  percent: number
  health: 'on-track' | 'at-risk' | 'off-track' | null
}

/**
 * Scope params only. State filters are deliberately absent: a completion
 * percentage over a set that already dropped done items is always 0%, so the
 * strip reports the *scope* you're looking at, not the state filter you
 * applied on top of it.
 */
export interface StatusScope {
  project?: string
  slate?: string
  milestone?: string
  under?: string
}

function buildQuery(s: StatusScope): string {
  const p = new URLSearchParams()
  if (s.project) p.set('project', s.project)
  if (s.slate) p.set('slate', s.slate)
  if (s.milestone) p.set('milestone', s.milestone)
  if (s.under) p.set('under', s.under)
  const qs = p.toString()
  return qs ? `?${qs}` : ''
}

export const statusApi = {
  get: async (scope: StatusScope = {}): Promise<StatusSummary> => {
    const r = await fetch(`/api/status${buildQuery(scope)}`)
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as StatusSummary
  },
}

export const statusQk = {
  // Keyed under 'status' so the SSE item invalidation refreshes every scope.
  all: ['status'] as const,
  scoped: (s: StatusScope) => ['status', s] as const,
}

/** POST /api/status/report — what the menu's "Write status report" wrote. */
export interface StatusReportWritten {
  /** Relative to the knit root, the configured `status_report.path`. */
  path: string
  written: boolean
}

/** The SPA never forces: replacing a hand-written file is the user's call, on the command line. */
export const forceReportCommand = 'knit status report --force'

export interface ReportToast {
  kind: 'success' | 'error'
  message: string
  /** A command the toast offers in a popover; set only when the target lacks the generated marker. */
  command?: string
}

export function reportToast(outcome: StatusReportWritten | unknown): ReportToast {
  if (outcome && typeof outcome === 'object' && 'path' in outcome && 'written' in outcome)
    return { kind: 'success', message: `Wrote ${(outcome as StatusReportWritten).path}` }
  if (outcome instanceof ApiError && outcome.problem?.code === 'not_generated')
    return { kind: 'error', message: outcome.message, command: forceReportCommand }
  return { kind: 'error', message: `Status report not written: ${outcome instanceof Error ? outcome.message : String(outcome)}` }
}

export const statusReportApi = {
  write: async (): Promise<StatusReportWritten> => {
    const r = await apiWrite('/api/status/report', 'POST')
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as StatusReportWritten
  },
}

/**
 * GET /api/status/report — the page knit wrote, if any. `exists` is false for a
 * missing page and for a file knit didn't write; the stale dot and the view's
 * entry points show only when it is true.
 */
export interface StatusPage {
  /** Relative to the knit root. */
  path: string
  exists: boolean
  /** The page's `Generated` date; null when a hand edit removed it. */
  generated: string | null
  stale: boolean
}

export const statusPageApi = {
  get: async (): Promise<StatusPage> => {
    const r = await fetch('/api/status/report')
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
    return (await r.json()) as StatusPage
  },
}

export const statusPageQk = {
  // Not under 'status': the page lives outside .knit/, so no item event says it changed.
  get: ['status-page'] as const,
}

/** The page's folder, relative to the knit root: its links are relative to it. */
export function pageFolder(path: string): string {
  const i = path.lastIndexOf('/')
  return i < 0 ? '' : path.slice(0, i)
}
