---
name: knit-projects
description: Project-management workflow for Knit — organizing work above the individual item. Covers the project ⊃ milestone ⊃ slate ⊃ item planning hierarchy, the project doc folder + roadmap, the milestone lifecycle (planned → reached/dropped), and linking items to milestones. Load when planning or structuring a project's roadmap and milestones; for executing a single item use the knit-work skill, for raw command flags the knit-cli skill.
---

# knit-projects

Planning work **above** the individual item: projects, milestones, and the
roadmap that ties them together. This is the *how do I shape the plan* skill —
the `knit-work` skill owns *how do I execute one item*, and `knit-cli` owns *how the
commands behave*.

For the shared vocabulary and the doc-folder model this skill assumes, see
[`knit`](../knit/SKILL.md) skill > Conventions (Glossary + Doc folders). This skill
does not re-define those — it points at them and adds the planning workflow.

> **Run `knit conventions` once per session before the first write, and again after the context was compacted; they override the Knit skills' guidance, no other skill's.**

## The planning hierarchy

Work is organized on a **four-level containment axis** — three grouping
layers around the items they hold:

```
project  ⊃  milestone  ⊃  slate  ⊃  item (epic / feature / bug / task / todo)
```

- **project** — a body of work with its own doc folder (roadmap, milestones,
  free-form docs). The tracker side is an ordinary **item** of type `project`
  whose `project:` frontmatter field names its doc folder.
- **milestone** — a delivery/roadmap boundary a set of items aims at. A
  planning grouping, **not an item** — it lives as a sub-doc in the project's
  folder and items point at it via a `Milestone` link (one per item), which
  their descendants inherit.
- **slate** — a hand-picked "work on this next" selection (see
  [`knit-cli`](../knit-cli/SKILL.md) skill > Slates). Deliberately not a sprint.
- **item** — the tracked unit of work. `project / epic / feature / bug / task /
  todo` are item **types** on a value axis, not layers of this hierarchy.

**`todo` is an item type, not a layer.** The three grouping layers sit *around*
the items; `todo` is itself one of the item **types** — a non-code leaf (the
PM-side checklist item, peer to `task` in shape) that lives under a `project`
(or another `todo`). Don't mistake it for a fifth grouping layer. Todos show
up in `knit list` / `knit tree` / `knit timeline` like any other item —
filter them out with `--type` (or `timeline --hide-todos`) when you want a
dev-only view.

**milestone vs epic.** An *epic* is an item — worked and closed. A *milestone*
is a boundary items aim at, and is never worked or closed. When "roughly a
release" needs to be first-class and linkable, that's a milestone. See
[`knit`](../knit/SKILL.md) skill > Conventions (epic vs milestone).

## The project doc folder

A project's folder behaves like a KB entry: **entries + folder entries**. See
[`knit`](../knit/SKILL.md) skill > Conventions > Doc folders for the shared model.
In a project it holds:

- `readme.md` — the project entry point (always present).
- `roadmap.md` — optional narrative that links out to the milestones.
- `milestones/<slug>.md` — one sub-doc per milestone.
- free-form `.md` files and folders — browsable and searchable, **inert to the
  link machinery** (drop a file, it just shows up).

The folder is authored content; the *tracker* side is the `project` item that
points at it via `project: <slug>`. The two halves **must coexist** — `verify`
warns on either orphan (a folder with no item, or a `project` item with no
folder).

## Creating a project

A project is two halves — a tracker item and its doc folder — but you create
both in one action:

```bash
knit project new relaunch --title "Site relaunch" --roadmap \
  --goal "Replace the 2019 site; done when the old one is switched off."
#   → scaffolds projects/relaunch/ (readme.md, + roadmap.md)
#   → mints a `project` item linked via project=relaunch
```

- `--item <id>` links an **existing** `project` item instead of minting one.
- `--no-item` scaffolds only the folder (an orphan — `verify` warns until you
  link an item with `--item`/`knit set <id> project=<slug>`). Prefer the default.

Don't hand-assemble the halves with a separate `add project` + `knit set` — the
one-shot `project new` derives the join so the two can't drift.

Every scaffold (readme, roadmap, milestone) ships with a prompt line under each
section saying what belongs there — replace it, don't leave it. `--goal` fills the
readme's `## Goal` at create time, which is the one section worth stating before
any work exists. The `project` item and the items under it follow
[`knit`](../knit/SKILL.md) skill > Conventions > Writing items.

**Removing a project** deletes the item + doc folder but **re-roots** its direct
children (clears their `parent` so they surface at root) rather than cascading
the subtree — a root-level item is a valid, first-class state, re-homed later by
setting a new parent. Back-references pointing at the project are left dangling
by default (run `knit verify`), or scrubbed on request. See
[`knit-cli`](../knit-cli/SKILL.md) skill > Projects & doc folders (`knit project
delete`).

Raw flags: [`knit-cli`](../knit-cli/SKILL.md) > Projects & doc folders.

## The roadmap

The roadmap is **narrative** — the cross-cutting half of the plan. Its job is
what the milestone docs individually cannot say: why the sequence is what it is,
themes and dependencies spanning several milestones, the risks and bets, and what
is deliberately in *no* milestone. Per-milestone detail belongs in
`milestones/<slug>.md`.

It is also the **structural spine**: milestones nest under the roadmap node in
the projects tree, and their order is the `rank:` field (see below) — not the
order they appear in the prose. So don't maintain a hand-written milestone index
here; it only drifts. Linking a milestone *from* the narrative is still normal
and useful — with an ordinary relative markdown link:

```markdown
The rewrite lands before anything user-visible: [Beta cut](milestones/beta.md)
carries the schema change, so [Go live](milestones/go-live.md) can't move ahead
of it without a second migration.
```

Because the roadmap and the `milestones/` folder are **siblings** in the same
project folder, the link is just `milestones/<slug>.md`. Author the roadmap by
hand (it's a doc); Knit does not generate or maintain its prose.

Add a roadmap *after* creating a project with `knit project roadmap <slug>` — a
standalone scaffold that never touches the readme. `project new --roadmap` is for
scaffolding it up front.

**A roadmap is required before you can create milestones.** `knit project
milestone new` refuses until the project has a `roadmap.md` — the roadmap is the
milestone spine, so milestones can't exist without it. Scaffold it
up front with `project new --roadmap`, or add it first with `knit project roadmap
<slug>`.

## The milestone lifecycle

Milestones move through three states (frontmatter `state:`):

- **planned** — declared, not yet delivered. The default for a new milestone.
- **reached** — delivered.
- **dropped** — abandoned / de-scoped.

Create one, point items at it, and advance its state as work lands:

```bash
knit project roadmap relaunch                      # required first (or `project new --roadmap`)
knit project milestone new relaunch beta --target 2026-09-01
knit link <item-id> --milestone relaunch/beta      # one milestone per item
knit project milestone show beta                   # the milestone + its backlinks
knit project milestone set beta --state reached    # planned → reached when delivered
```

- **One milestone per item** — the link write path replaces any existing
  `Milestone` row. Address bare `<slug>` within the item's own project, or
  `<project>/<slug>` to disambiguate across projects.
- **Link the top item; inheritance carries it down.** An item's milestone is
  its own link, else the nearest ancestor's (read-time, nothing written). A
  descendant's own link overrides and re-roots its subtree. Never link every
  task by hand — link the epic or feature that aims at the milestone.
- **`show` is the backlinks surface** — every item carrying a `Milestone` row
  targeting the sub-doc (own links only, not the inheriting descendants).
- **Advance `state:` with `milestone set`** — `--state reached` when the
  boundary is delivered, `--state dropped` when de-scoped; `--target` / `--title`
  edit the rest in place. Fix a slug with `milestone rename` (re-points the
  back-links); retire one with `milestone delete` (leaves them dangling by
  default). The item states are what the milestone *aims at*; the milestone's own
  `state:` is a separate planning signal you set by hand.
- **Reorganizing? `milestone move` — never delete + re-link.**
  `knit project milestone move beta --to relaunch-2` moves the sub-doc to
  another project and re-points every item back-link; add
  `--rename <new-slug>` when the slug is already taken there (a collision
  without it is an error). Items are **re-pointed, not reassigned**: they stay
  in their own project, so some may end up serving another project's
  milestone. That is allowed — the command reports how many, and
  `knit verify` lists them as informational, not as a defect.
- Filter any item view by milestone: `knit list --milestone relaunch/beta`
  (also on `tree`, `timeline`, `status`) — matches inherited links too, so the
  whole subtree shows; narrow with `--type` / `--exclude-type` or use `tree`.
- **`state:` drives how the timeline reads the milestone** — a dated `planned`
  one whose target has passed is flagged `⚠ target passed` and pulls the agenda
  window back to it; `reached` reads as history; `dropped` is hidden unless
  `--include-done`. Advancing `state:` is what keeps the agenda honest.
- **Milestones have a manual order** — a `rank:` frontmatter field, assigned on
  create (newest appended last). `knit project milestone list` shows milestones
  in that order. There is no CLI reorder command — the rank is server-managed.

Raw flags: [`knit-cli`](../knit-cli/SKILL.md) skill > Projects & doc folders.

## Planning cadence

A light loop, not a ceremony:

1. **Shape** — capture the work as items (`knit-work` skill, `knit add`); promote a
   too-coarse feature to an epic when it needs several features to finish.
2. **Group** — declare milestones for the delivery boundaries that matter; link
   the top item of each piece of work that aims at one (`knit link --milestone`).
3. **Narrate** — if the sequence needs explaining, write it in `roadmap.md`
   with links to the milestones.
4. **Track** — `knit timeline` and `knit list --milestone <m>` show what a
   milestone still needs; `knit project milestone show <m>` shows what points at
   it. Advance the milestone `state:` as work lands.
   To *work* a milestone or project, group its items into a slate —
   `knit set <id> slate=<name>` on the containers, children inherit — and
   drive it with `knit next --slate <name>`; `next` has no `--milestone`.
5. **Report** — `knit status --project <slug>` (or `--milestone <m>`) rolls the
   scope up: completion %, per-assignee load, and the blocked + overdue lists.
   Use it to close out a cycle instead of reading the tree by hand.

Milestones and the roadmap are **planning** artifacts — keep them terse and
current, and let the items carry the actual work.

## The health signal

A project item can carry `health:` — `on-track` · `at-risk` · `off-track`. It
is the human judgement call that the item data can't make for you: a project
with no overdue work can still be off-track, and one full of red can be
recovering.

```bash
knit set <project-item> health=at-risk
knit project list                      # health column, per project
knit list --health at-risk --health off-track
```

- **Leave it unset until someone judges it.** Unset means "no signal", not
  `on-track`; setting `on-track` is a statement, not a default.
- Re-assess it on the planning cadence above, not per commit — a stale
  `on-track` is worse than no signal. `knit status --project <slug>` is the
  input for that call: it echoes the current signal next to the numbers that
  should inform it.
- Mechanics (project-only, how unset renders and filters):
  [`knit-cli`](../knit-cli/SKILL.md) skill > Fields.

## Stakeholders who don't use Knit

`knit status report` writes `project-status.md`, a page that renders on the
forge and in a plain checkout. Regenerate it on the planning cadence and link
it from the repo's README, so stakeholders have one place to look.

- **Keep an existing page current; never create one unasked.** Run `knit
  status report` when a knit command says the page is stale, and after a
  significant change: a milestone reached, dropped or retargeted, a project's
  `health` changed, a project or epic closed, or a PR opened for tracked
  work. Include the page in that change. A repo without the page has opted
  out.
- Location, overwrite guard, redirect: [`knit-cli`](../knit-cli/SKILL.md) skill > Status.

## Where the mechanics live

- **Raw commands + flags** → [`knit-cli`](../knit-cli/SKILL.md) skill > Projects &
  doc folders (the `knit project` family and milestone links).
- **The shared doc-folder model** (entries vs folder entries, rename/delete,
  indexing, "nothing vanishes") → [`knit`](../knit/SKILL.md) skill > Conventions >
  Doc folders — one home, shared with KB and designs.
- **Executing the items** under a milestone → the [`knit-work`](../knit-work/SKILL.md) skill.
- **Recording lasting project knowledge** (decisions, feature docs) →
  [`knit-kb`](../knit-kb/SKILL.md) skill. The project folder is for *planning*
  artifacts; durable truth still lives in the KB.

## Hit friction?

CLI behavior surprising or contradicting this skill? Log it via the feedback
loop — see [`knit`](../knit/SKILL.md) skill > Feedback loop. No project content in
the entry.
