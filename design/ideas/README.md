# Ideas — parked, not planned

Things worth doing eventually, none of them M1 work. Each one was raised by a
real gap and then deliberately not taken; the entry says what it is and what
holds it back. Nothing here is a commitment — an idea earns a plan task by
someone deciding the open question, not by aging.

Ideas too large for an entry here get their own file in this folder:

- [hub.md](hub.md) — the M2 hub: server, REST API, web UI for non-git users;
  waits on the client-model decision.
- [agent-steering.md](agent-steering.md) — driving coding agents from the web
  UI, one run per item.
- [catch-up-diff.md](catch-up-diff.md) — what changed since a commit / date,
  as a filter and a field-level diff; waits on allowing read-only git calls.
- [csv-backlog-migration.md](csv-backlog-migration.md) — an agent prompt doc
  that imports a vendor CSV export through the public CLI.
- [mermaid-visual-editing.md](mermaid-visual-editing.md) — editing diagram
  labels, styles and edges in the SPA, held back by mermaid's parsers.
- [spa-git-integration.md](spa-git-integration.md) — opt-in commit / pull /
  push of `.knit/` from the SPA.
- [status-report-automation.md](status-report-automation.md) — regenerating
  `project-status.md` without a manual run; CI job versus `knit web`.
- [test-sandbox-container.md](test-sandbox-container.md) — a disposable
  container for agent scratch repos and a real seed-script run.
- [worktree-drift-and-reftable.md](worktree-drift-and-reftable.md) — catching
  hand edits to a redirected worktree's own `.knit/`, and full reftable support;
  both wait on allowing read-only git calls.
Ideas too small for an entry live as one-liners in [backlog.md](backlog.md).

---

## Lifecycle history on items

`Item` has no closed-at / closed-by field, so a reopened-then-reclosed item is
byte-indistinguishable from one closed once. The prior question is whether items
should carry lifecycle *history* at all: today they carry current truth and git
carries the history, which is what "items structure work, not truth" argues for.
If it comes back: `closed_at` / `closed_by` written by `done` / `dismiss`,
cleared on reopen, surfaced in `show --json`, `verify` flagging a closed item
missing them. A reopen count or an audit trail is a bigger call again.

## Current slate

Nothing auto-applies a slate to newly added items — every item is slated by hand.
Cheapest gain of the parked set (one `set` per add) against the priciest design:
it is implicit state that silently changes what a write produces. Unsettled:
where the current slate lives (config? per-checkout? per-agent?) and how `add`
opts out.

## Search ranking and presentation

`LiveScanSearcher` ranks on match quality alone, so container titles — the broad
noun by construction — outrank the work beneath them, and `Frequency` counts
body + all comments, a document-size proxy that favors them again. The ranking
rule can't be settled while "container or leaf?" depends on intent the query
doesn't carry.

Sketch, if it comes back: cap `Frequency` and demote closed items instead of the
alpha tiebreaker (no design call needed); let an ancestor outrank its subtree
only when it out-qualities every descendant that also hit; show the ancestor
chain on the hit line, since most of the pain is a leaf-first list losing the
map.

Worth weighing first, because it reframes the fix: promote search to its own view
rendering hits *in the tree*, where the ordering question largely dissolves.

## `edit --remove-section`

`--value ""` clears a section's content but keeps the heading. Fine as it stands
— an empty heading is inert, the sections in question are our own template
scaffolding, and a section the user authored is theirs to delete in the file. A
remove verb would mostly serve tidiness.

## Whole-body stdin for `edit`

`edit --set-section` writes one h2 at a time, which covered every case the
dogfooding sim hit. Piping a whole body in would be the natural companion, but
nothing has needed it yet.

## Bulk re-slate — won't do

Moving items between slates is one `set` per item; rolling a sprint has no single
command. Recorded here for completeness, but this one is declined rather than
parked: the atomic `set` already composes, since every mutator emits `--json` and
`list --slate old --json` piped into a loop of `set <id> slate=new` *is* the bulk
operation. A dedicated verb buys one shorter line and owes a partial-failure
policy (does item 7 failing roll back 1–6?), an arg shape and a confirmation
prompt. Sprint rolling is monthly, not daily.

## Timeline rows grouped by hierarchy

*From the Phase 35 discussion (2026-09-16).*

Rows sort by date, so a parent and its children land arbitrarily far apart and
containment has to be re-established by other means (the breadcrumb, and 35.4's
selection-scoped highlight). Grouping rows by hierarchy instead — parent row,
children indented beneath, a vertical spine — would make containment permanent
and remove the need for either.

Not taken: date ordering is what makes the view a timeline rather than the tree
with an x-axis, so grouping can only be a *mode* you switch to, not a
replacement. That means a sort control, a second layout pass, and a second
answer to every question the canvas already answers (what the axis window
means, where the undated tray goes, how the at-risk overlay reads). The open
question is whether anyone reaches for it once 35.4 makes the relation visible
on demand — if selection-scoped highlighting is enough, the mode is a large
build serving a habit nobody formed.

## Timeline filters that match inside the rollup

*From the timeline-filters discussion (2026-09-28).*

The timeline rolls up first and filters second, and a filter tests only the
bar's own item. At Epics depth, `assignee=bob` shows the epics bob owns, not
the epics bob has work in: his task under alice's epic is folded into it and
leaves with it. The filter tooltips say so; Depth All is the workaround.

Sketch: keep a bar when its item *or anything folded into it* matches — only
for per-item attributes (assignee, mine, unassigned, tags, priority, overdue,
blocked); type, scope, milestone and project stay as they are. Risk keeps
reading the whole subtree, since hiding a deadline threat is dishonest. A bar
kept only for its children needs a mark ("2 of 7 match"), or it reads as bob's.
`knit timeline` has the same filters and must agree.

Open: what a partly-matching bar spans — its whole subtree (today's rule, but
it draws alice's dates under a bob filter) or only the matching children
(honest, but a bar then means different things under different filters).

## Item aliases

*From a discussion (2026-09-24).*

Random ids are hard to recognize or remember, and the ones people use most
are the ones they'd most like to name. Renaming the id itself is out: other
branches and clones still hold the old id, `relink` has no mapping to repair
them, and a self-chosen id can collide across branches (see
[decisions.md](../decisions.md) > Storage). An alias sits on top of the id
instead, the way a git tag names a SHA.

- **Shape:** frontmatter `alias: auth`, addressed as `@auth` wherever an id is
  accepted. The sigil keeps it out of prefix resolution (`AUTH…` NanoIDs).
  Shared with the team, not local.
- **Output:** human output shows the full id plus `@alias` when one is set;
  skills tell agents to report aliased items to users by alias.
- **Stored refs:** prose refs may use it (`item:@auth`). Link-table targets
  stay paths, since the path is what markdown viewers follow.
- **Lifecycle:** the alias stays when the item closes. `alias rename` and
  `alias free` rewrite every use, so a freed alias can be reused.
- **Collisions:** two branches can claim the same alias. `verify` reports the
  duplicate, and the resolver refuses to pick one.
- **Items only.** KB entries, designs and projects are addressed by slug, which
  is already readable, and `kb rename` already renames it and rewrites refs.
  An alias there would be a second name for the same thing, and a global
  `@name` would undo the bucket-required KB ref rule.

Open: a rename or free rewrites only this checkout, so a branch that still
refers to the old alias breaks on merge, and `relink` can't repair it without
an alias history. Is that acceptable because aliases are rare and deliberate,
or does `verify` need a tombstone (`old → id`) to give a "did you mean"?

## One addressing scheme

*From the item-alias discussion (2026-09-24).*

Knit has two address styles. Items and comments use NanoIDs: collision-free
under parallel creation, unreadable. KB entries, designs and projects use
slugs: readable, but two branches can create the same `doc:arch`, and nothing
catches it before the merge. The split was deliberate (NanoIDs for churny work,
slugs for named documents), but it leaves users and
agents with two sets of rules for addressing, renaming and collisions.

Open: is there one scheme that is concurrency-safe and still readable? For
example, a random id plus a readable handle everywhere (items' `<id>-<slug>`
generalized, with the handle being the slug or an [alias](#item-aliases)),
or slugs everywhere with collisions detected at merge. Settle this before
aliases ship, since aliases would add a third style.

## Mermaid links to a diagram or node

*From the mermaid section-links review (2026-10-20).*

Mermaid links land on a section heading. A link to one diagram
(`design:x#customers-map/mermaid-1`) or one node (`…/node/customer`), with a
flash on arrival, was designed and cut. It would also move pointers onto the
section address, making a copied pointer minus its hints a valid link. Held
back by the fragility, not the value:

- `/mermaid-<n>` within a section shifts silently when a diagram is added
  above it; verify can't tell.
- Without a C# mermaid parser, the node check is a text match of the id: it
  passes on a label word.
- Badge placement and flashing differ per diagram type, and a heading whose
  anchor reads `mermaid-<n>` clashes with the fallback.

Open: is landing on the heading right above the diagram ever not enough?
Revisit when split diagrams make it a real annoyance.

## Design folders

*From a discussion (2026-11-13).*

Active designs are flat `<slug>-design.md` files at the `designs/` root. The
only subfolder is `completed/<slug>/`, and `verify` flags any other one as
stray. Once a repo has many designs, people will want to group them.

- **Folders for grouping only, slug stays the address.** `design:<slug>` keeps
  resolving with no path in it, so moving a doc between folders breaks nothing.
  That means slugs must stay unique across folders, and verify reports
  duplicates. Every scan that touches designs has to learn the shape: `design
  list`, the ref resolver, the stray scan, archive, the file watcher, and the
  SPA designs tree.
- **Group by link instead.** The plan's top item already points at its design
  through `design:`, and that item sits under a project. Grouping `design list` and the SPA by that chain costs
  no new on-disk shape and no move churn.

Open: is link-based grouping enough? Folders only earn their place if designs
need a grouping that doesn't follow the item tree. Path-based refs are out,
because they break on every move (see [One addressing scheme](#one-addressing-scheme)).
