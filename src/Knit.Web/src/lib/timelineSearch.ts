import { z } from 'zod'
import type { ItemType } from '../api/types'
import { boolish, stringArray, typeArray } from './navigatorSearch'
import { pickFilters, TIMELINE_FILTER_DEFAULTS, useTimelineFilters, type TimelineFilters } from '../stores/timelineFilters'
import { timelineViewDefaults, useTimelinePrefs, type TimelineView } from '../stores/timelinePrefs'

// The timeline URL carries the whole view, so a link reproduces it. A URL that
// names any view key is a complete spec — a key it leaves out means the
// default, not the receiver's remembered value. `depth` and `range` are always
// written, so a synced URL is never empty.
export const timelineSearchSchema = z.object({
  // Selection — not part of the view spec.
  id: z.string().optional(),
  ms: z.string().optional(),

  depth: z.enum(['project', 'epic', 'feature', 'all']).optional(),
  range: z.enum(['week', 'month', '3m', 'year', 'all', 'custom']).optional(),
  from: z.string().optional(),
  to: z.string().optional(),
  hideDone: boolish,
  hideTodos: boolish,
  hideDisconnected: boolish,
  milestones: boolish,
  lookBack: boolish,

  type: typeArray,
  assignee: z.string().optional(),
  mine: boolish,
  unassigned: boolish,
  minPriority: z.enum(['someday', 'normal', 'important', 'critical']).optional(),
  tag: stringArray,
  slate: z.string().optional(),
  noSlate: boolish,
  milestone: z.string().optional(),
  project: z.string().optional(),
  under: z.string().optional(),
  overdue: boolish,
  blocked: z.enum(['only', 'hide']).optional(),
})

export type TimelineSearch = z.infer<typeof timelineSearchSchema>

// The view keys a search sets, sorted; selection (`id` / `ms`) excluded.
function viewEntries(s: TimelineSearch): [string, unknown][] {
  return Object.entries(s)
    .filter(([k, v]) => k !== 'id' && k !== 'ms' && v !== undefined)
    .sort(([a], [b]) => a.localeCompare(b))
}

export interface TimelineSpec {
  view: TimelineView
  filters: TimelineFilters
}

/** The spec a URL names, or null when it names none (use what is remembered). */
export function searchToSpec(s: TimelineSearch): TimelineSpec | null {
  if (viewEntries(s).length === 0) return null
  const d = timelineViewDefaults()
  const view: TimelineView = {
    depth: s.depth ?? d.depth,
    range: s.range ?? d.range,
    customFrom: s.from ?? d.customFrom,
    customTo: s.to ?? d.customTo,
    hideDone: s.hideDone ?? d.hideDone,
    hideTodos: s.hideTodos ?? d.hideTodos,
    hideDisconnected: s.hideDisconnected ?? d.hideDisconnected,
    showMilestones: s.milestones ?? d.showMilestones,
    showLookBack: s.lookBack ?? d.showLookBack,
  }
  const f = TIMELINE_FILTER_DEFAULTS
  const filters: TimelineFilters = {
    types: (s.type as ItemType[] | undefined) ?? f.types,
    assignee: s.assignee || f.assignee,
    mine: s.mine ?? f.mine,
    unassigned: s.unassigned ?? f.unassigned,
    minPriority: s.minPriority ?? f.minPriority,
    tags: s.tag ?? f.tags,
    slate: s.slate || f.slate,
    noSlate: s.noSlate ?? f.noSlate,
    milestone: s.milestone || f.milestone,
    project: s.project || f.project,
    under: s.under || f.under,
    overdue: s.overdue ?? f.overdue,
    blocked: s.blocked ?? f.blocked,
  }
  return { view, filters }
}

/** The URL for a spec: depth + range always, everything else only off-default. */
export function specToSearch({ view: v, filters: f }: TimelineSpec): TimelineSearch {
  const d = timelineViewDefaults()
  const out: TimelineSearch = { depth: v.depth, range: v.range }
  if (v.range === 'custom') {
    out.from = v.customFrom
    out.to = v.customTo
  }
  if (v.hideDone !== d.hideDone) out.hideDone = v.hideDone
  if (v.hideTodos !== d.hideTodos) out.hideTodos = v.hideTodos
  if (v.hideDisconnected !== d.hideDisconnected) out.hideDisconnected = v.hideDisconnected
  if (v.showMilestones !== d.showMilestones) out.milestones = v.showMilestones
  if (v.showLookBack !== d.showLookBack) out.lookBack = v.showLookBack

  if (f.types.length) out.type = f.types
  if (f.assignee) out.assignee = f.assignee
  if (f.mine) out.mine = true
  if (f.unassigned) out.unassigned = true
  if (f.minPriority) out.minPriority = f.minPriority
  if (f.tags.length) out.tag = f.tags
  if (f.slate) out.slate = f.slate
  if (f.noSlate) out.noSlate = true
  if (f.milestone) out.milestone = f.milestone
  if (f.project) out.project = f.project
  if (f.under) out.under = f.under
  if (f.overdue) out.overdue = true
  if (f.blocked) out.blocked = f.blocked
  return out
}

/** Order-insensitive equality of the view part of two searches (selection ignored). */
export function sameSpec(a: TimelineSearch, b: TimelineSearch): boolean {
  return JSON.stringify(viewEntries(a)) === JSON.stringify(viewEntries(b))
}

/** The spec the stores hold right now. */
export function currentSpec(): TimelineSpec {
  const prefs = useTimelinePrefs.getState()
  const d = timelineViewDefaults()
  const view = Object.fromEntries(
    Object.keys(d).map((k) => [k, (prefs as unknown as Record<string, unknown>)[k]]),
  ) as unknown as TimelineView
  return { view, filters: pickFilters(useTimelineFilters.getState()) }
}
