export type ItemType = 'project' | 'epic' | 'feature' | 'bug' | 'task' | 'todo'
export type ItemState = 'draft' | 'todo' | 'in-progress' | 'review' | 'done' | 'dismissed'
export type Priority = 'someday' | 'normal' | 'important' | 'critical'
// Project-only signal; null on an item means "no signal" (distinct from on-track).
// Named ItemHealth to avoid colliding with the repo-level `Health` interface below.
export type ItemHealth = 'on-track' | 'at-risk' | 'off-track'

export interface ItemSummary {
  id: string
  shortId: string
  title: string
  type: ItemType
  state: ItemState
  assignee: string | null
  parent: string | null
  rank: string | null
  effort: string | null
  due: string | null
  priority: Priority
  created: string
  blocked: boolean
  blockedReason: string | null
  hasChildren: boolean
  tags: string[]
  filePath: string
  // Populated only by the landing feed (/api/items/landing); omitted
  // from tree/list/ready payloads.
  modified?: string | null
  // Own slate: value only — inherited slates are a server-side filter concern.
  slate?: string | null
  // Project-only signals, omitted when null. `health` badges the
  // project node; `project` is the doc-folder slug that links the item to its
  // /api/projects folder in the Projects nav.
  health?: ItemHealth | null
  project?: string | null
  // The blocked ancestor holding this item; omitted when nothing does.
  heldBy?: HeldBy
}

// The nearest open ancestor carrying a block: it holds the item and its
// subtree (not ready) without writing anything to them.
export interface HeldBy {
  id: string
  shortId: string
  title: string
}

export interface BodySection {
  key: string
  markdown: string
}

export interface LinkRow {
  rel:
    | 'Parent'
    | 'Blocks'
    | 'Blocked by'
    | 'Related'
    | 'Subtasks'
    | 'KB ref'
    | 'Milestone'
    | 'Branch'
    | 'Repo'
    | 'PR'
    | 'Commit'
  targetText: string
  targetPath: string
  targetItemId: string | null
  notes: string | null
}

export interface Comment {
  id: string
  author: string
  created: string
  body: string
}

export interface Item {
  id: string
  shortId: string
  title: string
  type: ItemType
  state: ItemState
  assignee: string | null
  parent: string | null
  rank: string | null
  effort: string | null
  due: string | null
  priority: Priority
  created: string
  modified: string
  // Blocked ⇔ a free-text reason or at least one `Blocked by` link.
  blocked: boolean
  // The free-text reason only; each `Blocked by` link is a reason of its own.
  blockedReason: string | null
  summary: string | null
  feature: string[]
  agentSkills: string[]
  tags: string[]
  design: string | null
  body: string
  sections: BodySection[]
  links: LinkRow[]
  comments: Comment[]
  filePath: string
  slate: string | null
  // Project-only: health signal + doc-folder slug pointer.
  health: ItemHealth | null
  project: string | null
  // Only on a response to a call that closed the item: open items still
  // blocked by it.
  blockedDependents?: BlockedDependent[]
  // The blocked ancestor holding this item; omitted when nothing does.
  heldBy?: HeldBy
}

// An open item still blocked by the item just closed. `remaining` is what
// else blocks it; null ⇔ the closed item was its only impediment.
export interface BlockedDependent {
  id: string
  shortId: string
  title: string
  remaining: string | null
}

export interface TreeNode {
  item: ItemSummary
  children: TreeNode[]
  match: boolean
}

export interface KbRef {
  targetText: string
  targetPath: string
  notes: string | null
  inheritedFrom: string | null
}

export interface Blocker {
  id: string
  shortId: string
  title: string
  state: ItemState
}

// One source of a block: the item's own, or the ancestor holding it. `note` +
// `blockedBy` are the parts of the `reason` summary.
export interface BlockedStatus {
  reason: string
  inheritedFrom: string | null
  note: string | null
  blockedBy: { id: string | null; title: string; notes: string | null }[]
}

export interface AgentSkillRef {
  skill: string
  inheritedFrom: string | null
}

export interface ExternalRef {
  rel: 'Branch' | 'Repo' | 'PR' | 'Commit'
  target: string
  url: string | null
  notes: string | null
  inheritedFrom: string | null
}

export interface DesignRef {
  slug: string
  path: string
  inheritedFrom: string | null
}

/** `checked` is null for a plain bullet, true/false for a task box. */
export interface AcceptanceCriterion {
  text: string
  checked: boolean | null
}

export interface ItemContext {
  id: string
  parentChain: ItemSummary[]
  agentSkills: AgentSkillRef[]
  kbRefs: KbRef[]
  externalRefs: ExternalRef[]
  acceptance: AcceptanceCriterion[]
  codePointers: string[]
  blockers: Blocker[]
  // Own block first, then the blocked ancestor holding the item.
  blocked: BlockedStatus[]
  design: DesignRef | null
}

export interface Identity {
  handle: string | null
  display: string
}

export interface Health {
  rootPath: string
  identity: Identity
  itemCount: number
  version: string
  // `due_soon_days` from config.json (default 3) — the shared window that
  // classifies a dated item as "due soon" for amber emphasis.
  dueSoonDays: number
  // Repo-wide open-item due counts (never filtered) — feed the timeline
  // nav-rail attention dot.
  overdueCount: number
  dueSoonCount: number
}

/**
 * One node of `GET /api/timeline`. `ghost: true` marks an out-of-filter
 * at-risk blocker kept so the graph never drops the at-risk signal —
 * render dimmed. Rollup anchors carry a `[spanStart, spanEnd]` due span
 * and the hidden descendants in `rolledUp`.
 */
export interface TimelineNode {
  id: string
  shortId: string
  title: string
  type: ItemType
  state: ItemState
  due: string | null
  priority: Priority
  effort: string | null
  assignee: string | null
  parent: string | null
  /** Live blocked-flag reason; null when not blocked. */
  blocked: string | null
  /** Qualified `project/slug` of the item's milestone link; null when none. */
  milestone: string | null
  /** Ancestor titles coarsest-first joined by ` ▸ ` — the parent breadcrumb
   * shown dim on the bar so containment reads without a click. Null
   * for a root. */
  parentPath: string | null
  atRisk: boolean
  impliedDeadline: string | null
  atRiskBlockers: string[]
  spanStart: string | null
  spanEnd: string | null
  subtreeAtRisk: boolean
  /** Descendants folded into this node — a depth artifact, so it drives
   * the rollup-anchor marker shape and nothing in the selection panel. */
  rolledUp: TimelineRolled[]
  /** Ancestor ids coarsest-first — `ancestorIds.includes(sel)` is "under the
   * selection", which survives folding in a way walking `parent` does not. */
  ancestorIds: string[]
  /** What has to happen for this node's date to hold — direct children
   * and open blockers, each carrying its own open counts. Depth-independent. */
  remaining: TimelineRemaining[]
  /** Open descendants of this node, all levels — the Remaining heading's total. */
  openBelow: number
  ghost: boolean
}

export interface TimelineRemaining {
  id: string
  shortId: string
  title: string
  type: ItemType
  state: ItemState
  due: string | null
  /** Open descendants of this row (excluding the row itself). */
  openBelow: number
  /** Of those, how many carry no due date — invisible on the canvas,
   * which is why the count says it out loud. */
  undatedBelow: number
  /** This row blocks the selected item rather than (only) sitting under it. */
  blocker: boolean
}

/** A descendant folded into a coarser timeline node. */
export interface TimelineRolled {
  id: string
  title: string
  type: ItemType
  due: string | null
}

/** Blocker edge: `from` blocks `to`. */
export interface TimelineEdge {
  from: string
  to: string
  atRisk: boolean
}

/**
 * A dated milestone for the overlay — a vertical guide + flag at
 * `target`. Only dated milestones are sent; the milestone filter narrows
 * the set to one.
 */
export interface TimelineMilestone {
  project: string
  slug: string
  title: string
  /** ISO target date (always present — undated milestones are omitted). */
  target: string
  /** planned | reached | dropped. */
  state: string
}

export interface Timeline {
  nodes: TimelineNode[]
  edges: TimelineEdge[]
  milestones: TimelineMilestone[]
}

export type EventKind = 'item' | 'kb' | 'design' | 'config' | 'health' | 'slates' | 'projects' | 'git' | 'conventions'
export type EventOp = 'created' | 'changed' | 'deleted'

export interface KnitEvent {
  kind: EventKind
  id: string
  op: EventOp
}

export type ActivityCategory = 'created' | 'state' | 'comment' | 'kb' | 'other'

/** One row of the activity feed (1c.7). Mirrors `ActivityEntryDto`. */
export interface ActivityEntry {
  seq: number
  ts: string
  category: ActivityCategory
  kind: 'item' | 'kb'
  id: string
  title: string
  actor: string | null
  fromState: string | null
  toState: string | null
  op: EventOp
}
