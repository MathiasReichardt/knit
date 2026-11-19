# Timeline

`knit timeline` is the "what's due — and what's blocking it" lens: an
agenda of dated items grouped by day (short windows) or week, each line
carrying id, title, priority glyph, effort, assignee, and due proximity
(`⚠ due in 2d` / `⚠ 3d overdue`, window = `due_soon_days`).

```bash
knit timeline                          # next month, overdue included
knit timeline --range 3m --slate q1    # a slate's quarter
knit timeline --milestone relaunch/beta # only items aiming at a milestone
knit timeline --project relaunch       # only items in a project's tree
knit timeline --depth epic             # epics only, children rolled up
knit timeline --from 2026-07-01 --to 2026-09-30   # explicit window
```

- **At-risk is the point.** A blocker's *implied deadline* is the
  earliest due among the items it (transitively) blocks — its own due
  excluded. An open blocker is flagged **at-risk** when it's undated or
  due *after* that implied deadline (same-day is in time). A blocker
  with no dated downstream is never at-risk, and done/dismissed items
  exit the analysis entirely. Endangered items are annotated
  `⚠ blocked by <id> (undated|due <date>)`.
- **A dated milestone target is an implied deadline** for its items —
  every item whose effective milestone (own link or inherited from an
  ancestor) is dated takes the target as its implied deadline (undated
  items in a dated milestone go at-risk, tasks under a linked epic too),
  and it min-propagates to the item's blockers like any downstream due.
- **Unassigned at-risk reads differently** (`unassigned!`) — an at-risk
  blocker nobody owns is the actionable crisis.
- **Filters never hide risk.** At-risk computes over the full graph;
  with `--slate` / `--min-priority` / `--under` / `--milestone` /
  `--project`, an out-of-filter blocker still annotates the endangered
  item, and `--json` keeps it as a `ghost: true` node so the graph stays
  complete.
- `--milestone <slug | project/slug>` narrows to items aiming at a
  milestone (same resolver as list/tree, inherited links match); `--json`
  nodes carry the item's effective milestone as a `milestone` ref field.
- `--project <slug>` narrows to items **in a project's tree** — the doc-folder
  slug, containment (nearest project-type ancestor points at the folder), not a
  link. Distinct from `--milestone` (a link). Unknown slug errors.
- **Node context fields (`--json`).** Every node carries a `parent_path`
  — ancestor titles coarsest-first (`A ▸ B`) — and its folded descendants
  as `rolled_up` **objects** (`id`, `title`, `type`, `due`), so a rollup
  anchor's hidden work stays visible without re-querying.
- **Dated milestones ride along.** Undated ones have no axis position and
  never appear. In the agenda a milestone leads its own date group as
  `◆ <project>/<slug> <title>`, with `⚠ target passed` when an unreached
  target is behind us and an `N open` count of the items still in
  it (own link or inherited). `dropped` milestones are hidden unless `--include-done` (they are
  the roadmap's `dismissed`). Both `--milestone` and `--project` narrow
  the overlay. `--json` carries the same set as a top-level `milestones`
  array (`project`, `slug`, `title`, `target`, `state`) — **unclipped by
  the window**, unlike the agenda, because the SPA sizes its own axis
  from it.
- **Window is future-focused**: `--range week|month|3m|year|all`
  (default month) starting today — extended back automatically when
  items are overdue, or a `planned` milestone's target has passed.
  `--include-done` extends it back over finished work too, so a task
  completed last month actually shows; expect a much wider window on a
  long-running repo. `--from`/`--to` set an explicit window instead
  (mutually exclusive with `--range`, and never auto-extended). Undated
  items land in a trailing `(undated)` section, not dropped. A repo with
  a roadmap but no dated items still renders.
- `--depth project|epic|feature|all` rolls hidden descendants into the
  nearest shown ancestor: its entry spans their `[earliest, latest]` due
  and warns `⚠ subtree at risk` when a hidden child is endangered.
  `project` is the coarsest level — a `project`-typed item's epics/tasks
  roll into it. When a chain has **no** ancestor of the shown rank, the
  item rolls into its **chain root** (topmost ancestor) rather than
  surfacing as a scattered leaf; only a **parentless** item shows at its
  own level (so a coarse view stays coarse, and nothing vanishes). A shown
  container with no due of its own still **spans its subtree** — it renders
  as a bracket over its children instead of a broken date-less bar.
- `--hide-disconnected` drops chain roots too fine for `--depth` — items
  whose parent chain never reaches the shown rank (shown by default). A
  clean "real `<rank>` only" view; no effect at `--depth all`. Render
  filter (a hidden at-risk blocker still ghosts back).
- `--hide-todos` drops `todo`-typed items (shown by default here — the
  timeline is deadline triage, unlike the dev view's default-hide). It's
  a render filter, not a graph filter: a hidden todo blocking dated work
  still resurfaces as a `ghost: true` at-risk blocker.
- `--json` emits the full schedule graph (`nodes` + blocker `edges` +
  the resolved window) instead of the agenda — no ASCII parsing.
