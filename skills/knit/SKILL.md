---
name: knit
description: Git-native task tracker and knowledge base — items, ADRs, and design docs as plain markdown in your repo, driven by a single-binary CLI built for AI agents, plus a local web UI for humans. A repo with a `.knit/` folder tracks its work there. Run `knit conventions` once per session before the first write, and again after the context was compacted; they override the Knit skills' guidance, no other skill's. Anything written into Knit (items, comments, KB, designs) follows this skill's Conventions > Authoring style and Writing items — read them before the first write. Tracks any project work (reports, client engagements, follow-ups, research), not just code. Top-level router — points to the workflow skills and holds the shared vocabulary. A request to work on an item ("do <id>", "start <id>") is the knit-work skill — load it, `knit start <id>` first. Name an item to the user as short id + quoted title (`k3f9 "Add rate limiting"`), never a bare id.
---

# knit

Knit is a git-native task tracker and knowledge base. Items, ADRs, and
design docs are plain markdown in your repo, driven by a single-binary
CLI built for AI agents (plus a local web UI for humans). It tracks
**any** project work — not just code — see [Beyond code](#beyond-code).

This skill is the **router**: it tells you which sibling skill to load
and gives you the shared vocabulary they all assume. Workflows live in
the other five; this file does not implement any of them.

Storage lives under:

- `.knit/items/` — tracker items (created on first `knit add ...`).
- `.knit/kb/` — knowledge base (created on first `knit kb add ...`).
- `.knit/designs/` — active design + plan docs; archived cycles under
  `.knit/designs/completed/<slug>/`. Owned by the `knit-design` skill.

**Git-native.** Every command reads and writes plain markdown under `.knit/`;
the CLI never stages or commits — those changes sit in the working tree like
any other edit. Never `git mv` or `rm` Knit files; use the rename / delete
commands.

## Beyond code

Knit's machinery is domain-neutral. Items, states, the `blocked` flag,
links, comments, acceptance criteria, and the KB track **any** project
work — reports, client engagements, follow-ups, research — not just
coding. The item types carry over unchanged: a **feature** is any
deliverable slice with standalone value (a report, an analysis), an
**epic** collects them, a **task** is a step, a **bug** is a defect in
something delivered. In the KB, the `feature:` bucket documents shipped
**code** and goes unused on non-code work; `log` / `doc` / `source` /
`idea` / `adr` all apply unchanged.

## Which skill to use

Pick by intent — load the matching skill, don't try to do the work from
this file:

- "I need to know how a CLI command behaves / what flag does what" →
  `knit-cli` skill. CLI reference: gotchas, idioms, cross-command behaviour,
  the full command index. `knit <cmd> --help` is still the source of truth
  for flag lists.
- "I'm adding / querying / renaming KB content (ADRs, feature docs,
  logs, sources)" → `knit-kb` skill.
- "I'm running a design cycle (new feature design + plan, archiving a
  completed cycle)" → `knit-design` skill.
- "I'm executing tracked work (pick next item, start, finish; drive a
  branch to completion)" → `knit-work` skill. Owns the
  **finish-what-you-started** pick-next ordering rule behind `next`.
- "I'm planning above the item — projects, milestones, a roadmap; how
  the grouping layers fit" → `knit-projects` skill. Owns the
  **project ⊃ milestone ⊃ slate ⊃ item** planning hierarchy, the
  project doc folder + roadmap, and the milestone lifecycle.

Load only the skills the task needs. Load the `knit-cli` core when
running commands; its reference files on demand.

## Where does this go?

The section above routes by *what you're doing*. This one routes by
*what you have in hand* — a piece of information looking for a home.
Find the row, then load the named skill for the mechanics.

| I have… | Goes to | Skill |
|---|---|---|
| A deliverable slice with standalone value (a product feature, a report, an analysis) | item: **feature** | `knit-design` skill or `knit add` |
| Several features (or related bugfixes) that cohere into a whole > parts | item: **epic** | `knit-design` or `knit add` |
| A defect in something delivered or in progress (code, a document, an output) | item: **bug** | `knit-work` skill |
| A step of work with no standalone value (may have subtasks) | item: **task** | `knit-work` |
| A decision that binds later work and was a real choice (the ADR test) | **ADR** (`adr:`), `proposed` while designing | `knit-kb` skill |
| A design decision that fails the ADR test | design doc `## Design Decisions` | `knit-design` |
| An external doc I'll want to point at later | **source** (`source:`) | `knit-kb` |
| How a shipped feature works (outlives any one item) | **feature doc** (`feature:`) | `knit-kb` |
| A cross-cutting living doc (architecture, glossary) | **doc** (`doc:`) | `knit-kb` |
| A dated record of something that happened | **log** (`log:`) | `knit-kb` |
| A rough thought, too raw to track | **idea** (`idea:`) | `knit-kb` |
| A task-local gotcha / question for the reviewer | **comment** | `knit-work` |
| A body of work with its own roadmap + milestone docs | item: **project** (+ doc folder) | `knit-projects` skill |
| A delivery / release boundary a set of items aims at | **milestone** (project sub-doc) | `knit-projects` |
| A hand-picked "work on this next" selection | **slate** | `knit-cli` skill > Slates |
| A non-code to-do under a project (register X, book Y) | item: **todo** | `knit-projects` |

**feature vs task is a value call, not decomposition.** The deciding
question is *does this deliver standalone value?* A **feature** is a
self-contained slice with standalone value — a product feature (new or
extending an existing one), a report, an analysis — that could ship on
its own. A **task** is a step with
no standalone value. Tasks nest — subtasks are intentional structure
for bigger work, and having them never makes a task a feature. An
**epic** collects features (or related bugfixes) into a whole greater
than its parts (often ~a release); a single feature that turns out too
coarse — needing several smaller features to complete — is itself a
signal to promote it to an epic. Epics are optional; tasks should not
be top-level.

## Creating items — a design is optional

Most items are created directly, and that's fine. Run
`knit add <type> "<title>"` any time — mid-discussion with the user, an
agent opening items to fulfill a request, or a plain *"add a feature
that does X"*. You do **not** need a design doc first, and you don't
have to route through the `knit-design` skill to open a feature or epic.

Reach for `knit-design` when a feature or topic is **large,
cross-cutting, or spans milestones** and benefits from thinking recorded
before the work — it scaffolds the item subtree for you. For small or
one-off work, skip straight to `knit add`.

**ADR vs design `## Design Decisions`.** The one rule for when an ADR
is written; the other skills point here.

- **The ADR test applies wherever a decision is written** — design, item
  body, comment ([`knit-kb`](../knit-kb/SKILL.md) skill > Capture a
  decision). Most decisions fail it and stay where they are written.
- **While designing:** a passing decision becomes a `proposed` ADR
  (`--origin design:<slug>`) once the user confirms; `## Design Decisions`
  keeps a one-line pointer to it.
- **Outside a design:** a passing decision becomes an ADR (`--origin
  item:<id>`) once the user confirms, set straight to `accepted`. In
  `auto` it stays `proposed`, with no confirmation; its origin also names
  the design when the item sits under one.
- **`proposed` is edited in place** until swept; `accepted` is never
  edited, only superseded.
- **Sweep at every close the user confirms** — `knit done` on any item,
  `knit dismiss`, archiving or dropping a design: `knit kb list --bucket
  adr --status proposed --json`, keep those whose `origin` names the
  closed item, any item in its subtree, or its design; with the user, set
  each `accepted` or `rejected`. A dismiss or drop rejects them.

## Orientation

Knit may be installed globally; not every repo uses it. Don't run
discovery commands unless the user's request is actually about Knit
content.

**If `.knit/` exists in the working repo:**

- Mention if the current user has work in progress —
  `knit list --state in-progress --mine --json`. If non-empty, point
  at the `knit-work` skill.
- `knit design list --json` surfaces active design cycles; non-empty
  → the `knit-design` skill may be relevant.
- Otherwise stand back. Let the user's request route you to the right
  workflow skill.

**The checkout's `.knit/` may only be a pointer.** `knit root` redirects a
checkout to another repo's knit state — a git worktree to its main checkout,
or a project repo to a shared knit repo. The path lives in the uncommitted
`config.local.json` (it is a fact about this machine); reads *and* writes then
go to the target, so an agent in a worktree edits the main checkout's
state. Commands don't announce it — `knit root --json` shows where and why
([`knit-cli`](../knit-cli/SKILL.md) skill), and `knit status` states it.
The path is local, the **requirement is committed**: a project on a shared root
declares `requires.knit_root` in `config.json`, so a fresh clone refuses until
`knit config setup` fills in its own path, instead of silently using an empty
`.knit/`. A shared root must not carry a `handle` in its `config.json` — the
repo layer beats each person's global one, so everyone would write as that
person. Several agents on one state is supported; several *people* writing a
shared root at once is not — one writer at a time
([`knit-cli`](../knit-cli/SKILL.md) > Concurrent writers).

**If `.knit/` does not exist:**

- Do nothing proactive. Don't suggest `knit init` unsolicited.
- If the user signals Knit intent ("track this", "design this
  feature", "start a KB"), then offer `knit init` and confirm
  before running it.

**Talking to the user: every item mention is short id + quoted title**,
`k3f9 "Add rate limiting"`. An id alone tells the reader nothing and hides a
wrong pick.

- It applies every time: repeat mentions, lists, table rows, blockers
  (`blocked by 7m2q "Add rate limiting"`), "next up", progress and stop
  reports.
- CLI output already pairs the two. When relaying or summarizing it, keep the
  title and don't cut it down to the id. With `--json`, take `title` from the
  same record.
- Writing into the tracker (bodies, comments) cites by ref instead; see
  Conventions > Authoring style.

## Most-used commands

Read with `--json`: the surface built for agents — full ids, stable snake_case
keys. Flags: `knit <cmd> --help` (run it, don't infer flags from skill text).
Gotchas and the full index: [`knit-cli`](../knit-cli/SKILL.md) skill.

- `knit init` — scaffold `.knit/` in this repo.
- `knit skill install` / `skill update` — write / refresh the skill bundle.
- `knit list` / `tree` / `show <id>` — query items.
- `knit context <id>` — item + parent chain + KB refs + blockers + acceptance criteria.
- `knit next` / `ready` — one recommended leaf / the whole workable inventory.
- `knit add` / `set` / `start` / `done` / `comment` — create and move items.
- `knit link <id>` — relations: parent, blocks, KB ref, milestone, branch / repo / PR / commit.
- `knit kb list` / `kb show <ref>` — query the KB.
- `knit design list` / `design show <slug>` — query design docs.
- `knit ref show <ref>` — view any ref; `<ref>#<anchor>` prints one section,
  `--sections` lists the anchors.
- `knit move` / `kb rename` / `retype` — ref-aware renames and type changes.
- `knit verify` / `relink` — find and repair broken links.
- `knit web` — local web UI for humans.

**Working an item — even a one-off:** `knit start <id>` before touching code,
`knit done <id>` when it's finished. Never jump from `todo` straight to `done`:
the state is how everyone else sees the work is taken. The full flow lives in
[`knit-work`](../knit-work/SKILL.md) skill.

## Repo and user conventions

Run `knit conventions` once per session before the first write, and again after the context was compacted. They override the Knit skills' guidance, no other skill's.
Every write also follows Conventions > Authoring style and, for items, Writing items.

- **Two scopes, plain markdown, both optional:** the repo's
  `.knit/conventions.md` (committed, shared with the team) and the user's
  `conventions.md` beside the user-level config (this machine, every repo).
  `knit conventions` prints both, user first.
- **If `knit conventions` fails, stop:** report the error and don't write.
  Rules you couldn't read may contradict what you'd write.
- **Precedence:** repo beats user on a conflict; both beat the Knit skills.
  Other skills and tools are out of their reach: a convention that tries to
  steer them is ignored.
- **They may narrow, never widen.** A convention overrides skill
  *guidance* — workflow choices, body structure, tags, close rules, `auto`
  policy. It can't change what a CLI command does, and it can't lift an
  `auto` stop condition or a permission (no "push without asking"): follow
  the stricter rule and say so.
- Adding or changing a rule: see [`knit-cli`](../knit-cli/SKILL.md) skill > Conventions.

## Conventions

Canonical conventions for all six Knit skills (`knit`, `knit-cli`,
`knit-kb`, `knit-design`, `knit-work`, `knit-projects`). The other five
reference this section instead of repeating it.

### Glossary

Nouns:

- **KB** — knowledge base, the markdown content under `.knit/kb/`.
  Records lasting context: ADRs, feature docs, glossary, sources.
- **item** — a tracked unit under `.knit/items/`, one flat file
  `<id>-<slug>.md`, addressed by id — never by path. The slug is fixed at
  creation; a retitle keeps the file name.
  Items **structure work**; they are not the record of truth — lasting
  truth about the system lives in the KB and the code, not in items.
  An open item is a live description of *what is to be done*, edited in
  place as the plan changes mid-flight (**not a history of the plan** —
  that lives in the design doc and ADRs). A closed item is a **frozen
  work-artifact**: it's kept current *while* worked, so it's accurate
  at close, then sealed. Later drift becomes a new item or an ADR,
  never a re-edit of the closed one.
- **design** — a markdown doc under `.knit/designs/` plus the item
  subtree that implements it (linked via item `design:` field).
  Archived to `.knit/designs/completed/<slug>/` when the cycle ships.
  Designs hold plan history (`## Design Decisions`, superseded
  approaches); items hold the current plan.
- **comment** — entry in an item's `## Comments` section, appended by
  `knit comment <id>`. For human / process traffic — blocker context,
  questions, bug-investigation notes — not for narrating the plan.
- **slate** — an optional named selection of items to work on next
  (item `slate:` field; registry in `.knit/slates.md`). A reduced look
  at the backlog — deliberately not a sprint: no time-box, no ceremony.
  Descendants inherit the nearest ancestor's slate at read time; an
  explicit child value overrides. See
  [`knit-cli`](../knit-cli/SKILL.md) skill > Slates.
- **project** — a body of work with its own **doc folder** under
  `.knit/projects/<slug>/` (roadmap, milestone sub-docs, free-form
  docs). The tracker side is an ordinary **item** of type `project`
  whose `project:` field names the folder. The folder follows the
  shared doc-folder model (see [Doc folders](#doc-folders-kb-designs-projects)).
  Planning workflow lives in [`knit-projects`](../knit-projects/SKILL.md) skill.
- **milestone** — a delivery / release boundary a set of items aims at:
  a sub-doc `.knit/projects/<project>/milestones/<slug>.md` items point
  at via a `Milestone` link (one per item). Descendants inherit the
  nearest ancestor's milestone at read time; an own link overrides. A
  planning grouping, **not an item** — never worked or closed; it moves
  `planned → reached / dropped`. See the epic vs milestone note below and
  [`knit-projects`](../knit-projects/SKILL.md).

Item types (the type signals *intent* on a value axis, and tree position
follows from it; the parent/type combos below are enforced by
`knit retype` / `knit add`):

- **project** — top altitude, **root-only** (no parent, no sub-projects).
  A body of work with an optional doc folder (see the `project` noun
  above). Groups epics / features / bugs / tasks / todos beneath it.
- **epic** — high-level: captures a coarse use case or requirement, and
  collects the features (or related bugfixes) that realize it into a
  whole greater than its parts; often ~a release / milestone; bigger and
  longer-lived than a feature. **Optional** — a repo may keep features /
  bugs at top level. A feature that turns out too coarse (needs several
  smaller features to finish) is a signal to promote it here.
- **feature** — a self-contained, deliverable slice with standalone value —
  a product feature (new, or extending an existing one), a report, an
  analysis; could ship on its own. Not "anything with sub-work."
- **bug** — a defect in something **already delivered**; a structural
  peer of feature (the fix ships value), usually a sibling of features
  under an epic. Its parent can only be `project` / `epic` / the repo
  root — **never a feature**. A defect you hit *inside* a feature you're
  still building isn't a bug item: nothing shipped yet, so fixing it has
  no standalone value. File it as a **task under that feature** (or just
  fix it, if it's within the task you're on). Promote to a `bug` only
  once the feature is done and the defect outlives it.
- **task** — a step of work with no standalone value. **Tasks nest
  arbitrarily deep** — subtasks are intentional structure for bigger
  work; having them never makes a task a feature. Should not be a
  top-level item.
- **todo** — a non-code leaf, peer to `task` in shape but on the
  **project** axis: the PM-side checklist item (register a trademark,
  book a venue). Parent is restricted to `project` or another `todo`;
  it can't sit at root, and code items never nest under it. Todos are
  **shown by default** everywhere — `knit timeline --hide-todos` (and the
  matching timeline toggle in the web UI) is the only opt-out, and it's a
  render filter, not a graph filter. See
  [`knit-projects`](../knit-projects/SKILL.md).

**Altitude of detail by type.** Epics and features are framed in *user /
business value, use cases, and outcomes* — not the solution. The *how*
(technical or otherwise) belongs in **tasks**. A **bug** describes the incident
(symptoms, repro); it may hint at a cause (technical or otherwise)
only when a reasonable guess or concrete info is already at hand.

**epic vs milestone.** An epic is an *item* — worked and closed like any
other. A **milestone** is a planning grouping, not an item: a release /
delivery boundary a set of features (often an epic's worth) aims at.
Designs express milestones in their `## Milestones` section; a project's
milestone sub-doc (`.knit/projects/<project>/milestones/<slug>.md`) makes
one first-class and linkable — items point at it via a `Milestone`
relation (`knit link --milestone`). "This epic ~ a release" is a
resemblance, not an identity — when the boundary itself must be
first-class and linkable, that's a milestone, not an epic. Planning
workflow: [`knit-projects`](../knit-projects/SKILL.md).

**tags vs the grouping axes.** `tags:` are free-form, cross-cutting
labels (`api`, `security`, `wip`) — the ad-hoc lens that cuts *across*
the structural axes. Keep them distinct: the **parent hierarchy** is
structural containment, a **milestone** is a roadmap/delivery grouping,
and a **slate** (curated work selection) is a hand-picked working set.
A tag is none of those — reach for it when a label spans items that
don't share a parent, milestone, or slate. Set with `knit set <id>
tags=…`; filter with `--tag` / `--no-tag`. Not inherited down the tree.

Item states (the `state:` frontmatter field):

- **draft** — created but not yet specified or approved. Excluded from
  `next` / `ready` / `pick`; counted everywhere else, including overdue
  and at-risk. `knit approve <id>` promotes it to `todo`.
- **todo** — not started; default for new items. `knit reopen <id>`
  reverts any other state back to `todo`.
- **in-progress** — actively being worked on. `knit start` sets this
  and auto-assigns to the current user.
- **review** — implemented, a final review pending (a human or an
  agent). Any type. A human sets it to mark work that still needs a
  review; an agent submits its finished work here whenever the user
  decides `done`. `knit submit <id>` sets it from `in-progress`. The
  reviewer runs `knit done`, or `knit start` to send it back with the
  findings in a comment. Excluded from `next` / `ready` / `pick`: it
  waits on the reviewer, not the implementer. Find it with
  `--state review`.
- **done** — closed (completed). Does not cascade: closing a parent
  leaves its children untouched.
- **dismissed** — closed (abandoned without completing). Treated like
  `done` for open/closed filters; subtree of a dismissed parent is
  hidden by default. `knit dismiss <id>` sets this.

Blocked is **orthogonal** to state: an item in any state can be blocked.
An item is blocked while it has **any impediment** — a `Blocked by` row
(another item holds it up) or the free-text `blocked: "<reason>"` (anything
else). Each is its own reason; neither overwrites the other. Set with
`knit block <id> --by <other>` or `--reason "..."`; both together put the
reason on that row's Notes, so it goes with the link. `knit unlink` one
blocker resolves that impediment only; `knit unblock <id>` resolves all.
Blocked items aren't ready; `Blocked by` rows also feed the dependency
graph (timeline, at-risk propagation). A block **holds the subtree**: an
open item under a blocked open ancestor isn't ready either, so block a
feature to hold its tasks. Nothing is written to them; unblocking the
ancestor releases them.

### Ref format

`<bucket>:<slug>` identifies any artifact. A KB ref always names its bucket —
slugs are unique per bucket, not repo-wide, so a bare slug is refused (the
error suggests the full ref). Only items take a bare id. Buckets:

- `item:<id>` — tracker item. Full 18-char id when written into a document;
  a prefix turns ambiguous as the repo grows.
- `adr:<NNNN-slug>` — numbered architectural decision record.
- `doc:<slug>` — cross-cutting living doc (architecture, glossary, …).
- `feature:<slug>` — per-feature living doc. Grow-to-folder: a
  `<slug>.md` file may be split into `<slug>/readme.md` plus sub-docs
  addressed as `feature:<slug>/<sub>`.
- `log:<YYYY-MM-DD-slug>` — dated entry (retro, incident).
- `idea:<slug>` — pre-tracker idea.
- `source:<id>` — external reference (RFC, spec, vendor doc).
- `design:<slug>` — design doc under `.knit/designs/`. Slug is
  filename-derived (`<slug>-design.md`); resolves the active doc, then
  the `completed/<slug>/` archive. Use it to cross-reference, jump to,
  or `ref show` a design from any document. **Not** a KB bucket — it
  lives outside `.knit/kb/` and has no `kb` subcommands. An item is
  associated with its design via the `design:` frontmatter field, not a
  `## Links` row.
- `project:<slug>` — project doc folder under `.knit/projects/`; resolves
  to its `readme.md`. Same address space as `design:`, not a KB bucket.

Slugs may contain `/` for the grow-to-folder case (`feature:`, `doc:`
and `source:` only).

**Share links.** For someone outside the tracker, `knit url <ref>` gives the
doc's file on the forge's default branch (`<repo_name>:<path>` without an
origin). Use a ref in tracker text, never a share link.

**Sections.** `<ref>#<anchor>` names an h2/h3 section wherever a ref goes
(`design:auth#sessions`); it spans the heading's subsections. A doc without a
ref (milestone, non-readme project doc) uses its `.knit/…` path:
`.knit/projects/<slug>/milestones/<m>.md#scope`.

- Always the full ref, also within the same doc — one copyable, greppable form.
- Take the anchor from `knit ref show <ref> --sections` or a link copied in
  `knit web`; never build it — the slug rule isn't GitHub's.
- Link to unique headings: a repeated heading's anchor gets `-2`, which shifts
  when a same-named heading is added above.
- Renaming a doc keeps the `#…`. After renaming a heading, grep the old ref and
  update the referrers in the same change.

### Doc folders (KB, designs, projects)

Three areas store authored markdown the same way, so the model is
defined **once here** and the area skills add only their specifics:
KB entries (`knit-kb`), design cycles (`knit-design`), and project
workspaces (`knit-projects`).

**Entries vs folder entries.** An entry is either a **flat file**
(`<slug>.md`) or, when it grows sub-docs, a **folder** (`<slug>/` with a
`readme.md` entry point plus nested files). The folder form is addressed
by the same slug; sub-docs get a nested slug (`<slug>/<sub>`). Which
areas grow to folders:

- **KB** — only the `feature:`, `doc:` and `source:` buckets (`knit kb
  split` converts a flat entry to folder form). `adr` / `log` / `idea`
  stay flat.
- **designs** — an active `<slug>-design.md`; on archive the whole cycle
  moves to `completed/<slug>/`.
- **projects** — always folder-form: `readme.md`, optional `roadmap.md`,
  `milestones/<slug>.md`, and free-form `.md` files / subfolders that are
  browsable + searchable but **inert to the link machinery**.

**Renames and deletes are tooling-managed.** Never `git mv` or `rm` a
doc file or folder — incoming links break silently. Use the area's
rename/delete command; each rewrites (or, on delete, strips) the
incoming refs and moves the whole folder for folder-form entries. See
[`knit-cli`](../knit-cli/SKILL.md) skill > Renames are tooling-only and the
per-area skills for the exact commands.

**Indexing is derived.** The on-disk files are authoritative; the
indexes over them are derived, rebuilt on write (`knit kb index`
regenerates the KB's).

**Nothing vanishes.** Any authored file Knit can't load or address (bad
id, a folder in `items/`, missing `readme.md`, wrong suffix, unknown bucket,
malformed frontmatter) is surfaced by `knit verify` and the web doctor
view with a path + reason — never silently skipped.

### Link table (`## Links`)

Every typed relation — item↔item, item↔KB, KB↔KB — lives in a body
`## Links` markdown table on the source file.

Rel vocabulary (case-insensitive parse, canonical case write):

- `Parent` — at most one per file.
- `Blocks` — this file blocks the target.
- `Blocked by` — target blocks this file.
- `Related` — non-directional reference.
- `Subtasks` — child items (derived; usually written by tooling).
- `KB ref` — link from an item to a KB entry.
- `Milestone` — the delivery boundary this item aims at.
- `Branch` / `Repo` / `PR` / `Commit` — work that lives outside the repo:
  a git branch, a repository (a declared mount, a directory, or a git URL),
  the full URL of a pull / merge request, and a commit (SHA or URL). Several of each are
  legitimate; a branch is stored as written, since Knit never shells
  out to git.

Target is a relative markdown link from the source file — except for the
three external rels, whose target is a name or a URL. Notes are
one-line free-form prose. No `## Links` header ⇒ no links — Knit
never writes a header-only table.

### Reserved h2 sections

These h2 headings carry tool-specific meaning. Do not rename,
duplicate, or freely append prose under them — write free-form notes
under any other heading.

**This list is exhaustive.** Every other heading a template seeds
(`## Description`, `## Goal`, `## Notes`, `## Steps to Reproduce`,
`## Expected`, `## Actual`) is prose Knit stores but never parses —
rename or drop those freely. The reserved ones are matched
case-insensitively but on the **whole** heading text, so
`## Acceptance criteria` is honored and `## Acceptance` is not: a
near-miss doesn't warn, it silently extracts nothing. This matters when
writing markdown directly; `knit edit --set-section` targets the
heading for you.

- `## Links` — typed relations table; Knit writes it, hand-edit
  triggers lint warnings. Add rows via `knit link` / `knit block` /
  `knit add --parent` / `knit link --kb-ref` (item→KB).
- `## Comments` — appended by `knit comment <id>`; entries have a
  fixed `` #### `<id>` · <author> · <date> `` header shape (the id is
  wrapped in backticks).
- `## Acceptance Criteria` — extracted by `knit context` into
  `acceptance_criteria[]` as `{ "text", "checked" }` objects. **Only `- `
  bullets are extracted** — prose under this heading is stored and
  displayed but never reaches `acceptance_criteria[]`. A bullet's
  continuation lines join into its `text`. A leading `[ ]` / `[x]` sets
  `checked` to false / true (null for a plain bullet), and a bullet
  holding nothing else is dropped, so the template's seeded `- [ ]`
  doesn't surface as an empty criterion.
- `## Code pointers` — one-line bullets pointing at code worth
  reading (symbol names like `ItemUtils.Save` preferred over
  `path:line`); extracted by `knit context` into `code_pointers[]` as
  plain strings, same bullets-only rule. Starting hints, not an exhaustive list.
  Seeded by no template — add it when there is code to point at.

### Authoring style

Applies to everything written through Knit — items, KB, designs, sources,
comments. Goal: every important fact, as few words as possible. When the two
collide, keep the fact and cut the words.

- **Point first.** Open with what matters — one line when that is enough, a
  short paragraph when the topic needs it. Detail follows as bullets;
  background and refs go last.
- **Bullets over prose.** Sentences ≤ ~25 words. No filler ("This task is
  about…", "In order to…"), no hedging, no marketing.
- **Wrap body text at 120**, continuation lines indented under their bullet.
  Don't wrap tables, code, URLs, headings, `## Links` rows or frontmatter.
- **Cite by ref in body text, never a path link or a bare id.** An item as
  `` `item:G7H8K2M4P9Q1R5T3VX` "Add return-reason column" `` (full id); a KB
  entry, design or project as `` `adr:0007-refund-policy` ``. A ref survives a
  move or split unchanged, `knit kb rename` rewrites it, the web UI links it, and
  `knit verify` checks it. Write the `bucket:` prefix only for an entry that
  exists now — placeholders, wildcards, planned and old names go in plain text
  without it. The quoted title is a hint that can go stale after a retitle — the
  id is what counts.
- **Self-contained in the tracker.** A stranger or a fresh agent can act on
  it without the chat that created it — no "as discussed". Read items via
  `knit context`: the parent chain and KB count as part of the text.
- **Write a fact once, at the highest level it applies to.** Children state
  only what differs ("Unlike the epic: B2B included"); unstated means
  inherited. A fact shared by siblings moves up; a parent fact one child
  depends on gets a ref up, never a copy.
- **Each kind of text in its place:** description = current intent;
  progress and findings → comments; decisions → ADR / KB. A description
  never grows into a log.
- **Label guesses** (`Suspected cause:`); keep unknowns in an
  `Open questions` list.
- **Metadata lives in fields** (priority, due, status, assignee, tags), never
  in body text.
- **Code in backticks:** code, paths, commands, flags, keys and placeholders
  like `${VAR}`.
- **Absolute dates** (`2026-10-01`, not "next week"). Name things instead of
  "it" / "this" across sections, since sections are read alone.
- **Structure when it beats bullets.** A table for comparisons, options or
  field/value lists (never a one-row table). A diagram wherever a picture
  explains faster than text — see Diagrams.
- **Empty sections are noise.** A template is a checklist, not a form: delete
  the headings you didn't fill (`knit edit <id> --delete-section "<h2>"`,
  repeatable). Keep one only if you fill it this session.

### Writing items

Authoring style applies, plus these rules — whoever writes the item: a plan
scaffold, a split, a direct `knit add`.

| Type | Title (~60 chars, no type prefix) | Body opens with |
|---|---|---|
| project | noun phrase: `Customer portal relaunch` | why it exists + the end state |
| epic | noun phrase: `Self-service order returns` | the user outcome + its boundary |
| feature | noun phrase: `Return label PDF download` | what the user can now do |
| bug | the symptom, not the cause: `Order total ignores discount on reload` | symptom + where (version, OS, data set); impact when known; then `## Steps` / `## Expected` / `## Actual` |
| task | verb first: `Add return-reason column to orders table` | the change + why |
| todo | verb first: `Ask finance about refund cutoff` | nothing, or one line of context |

- **The opening adds to the title**, never repeats it: one sentence, or a short
  paragraph when the topic is complex. Then bullets for scope, constraints and
  detail; an `Out of scope` bullet when the boundary isn't obvious.
- **Acceptance criteria:** each bullet one observable pass/fail statement, never
  an implementation step.
- **Split by the structure of the work**, never by text length. A pasted log in
  a code block is fine.
- **Comments:** one point each; lead with the finding, decision or status;
  never restate the item.

### Diagrams

Diagrams are Mermaid fences in any body — item, KB entry, design, project doc —
in any type: flowchart, sequence, state, class, ER, Gantt, timeline, mindmap, …
The SPA and GitHub render them; `knit show` and agents read the source. An SVG
file next to the doc is the last resort.

- **A diagram serves understanding:** clear, its message gets across, links to
  related diagrams or sections where that helps the reader on. Beauty second.
- **Self-explaining labels**, since agents and `knit show` see only the source.
- **Meaningful node ids** (`authSvc`, not `A`). A pointer names the id, and the
  id survives a relabel.
- **Drawing and pointers:** type, size, labels, shapes and colour with a
  legend, layout, and the pointer a human pastes from `knit web` —
  [Diagrams](references/diagrams.md). Read it before drawing a diagram or
  acting on a pasted pointer.
- **Files** (an image, a PDF, a text file) are placed by hand and linked by
  relative path: next to the doc, or anywhere in the repo. Under `.knit/` a file
  no body links is reported by `knit verify`. Next to an item's flat `.md`, name
  it `<id>-<name>.<ext>` — the id ties it to the item; no subfolder in `items/`.

## Feedback loop

Agents hit friction humans miss — wrong skill text, ambiguous
`--help`, a workflow that wants a flag, a lookup that needs three
round-trips. Capture it once, in one place, so the knit team can
act on it. Feedback is always welcome.

**Destination:** `.knit/knit-feedback.md` at the repo's knit root
(not under `.knit/kb/` — KB is project knowledge, not tool meta).
`knit init` adds the entry to `.knit/.gitignore` so the file stays
local to the working copy; the user forwards what's actionable.

**File header** — on first creation, the file starts with this
fixed mini-header so its purpose is obvious to anyone who opens it:

```markdown
# Knit feedback

Local-only notes on Knit tooling friction (skill text, CLI gaps,
workflow rough edges). Not committed. Forward useful entries to
the Knit team. Entries below are appended by agents; review and
prune as you like.
```

**Hard rule — no project content leaks in.** Feedback is about
Knit itself, never about the project. Do **not** include:

- Code snippets, file paths, identifiers, or function names from
  the project.
- Item titles, KB content, ADR text, design-doc prose.
- Secrets, credentials, hostnames, environment values.
- User names, assignees, or internal terminology.

If a friction needs project context to make sense, abstract it
(`a long item title with embedded markdown` not the actual title).
If it can't be abstracted without losing the point, **don't log
it** — describe the class of problem in the next entry instead.

**When to append (trigger-gated, not vibe-gated):**

- Documented CLI behavior diverges from the skill's description.
- A skill instructs an action the CLI can't perform.
- A workflow needs ≥3 manual steps that look mechanizable.
- `--help` text is ambiguous and you had to experiment.
- A lookup needed grep / multiple `--json` round-trips where a
  flag would obviously serve.

Don't log "this was a bit awkward" without a concrete fix in mind.

**Entry shape** — append, never rewrite. One h2 per entry:

```markdown
## YYYY-MM-DD — short slug describing the friction
- **Trigger:** workflow | cli-mismatch | docs | lookup-friction | other
- **Context:** what was attempted (no project content).
- **Observed:** what the CLI / skill did or said.
- **Suggested:** concrete change (new flag, skill text edit,
  CLI behavior change).
```

**Cadence:** at most one entry per topic per session. Append, tell
the user briefly (*"Logged a feedback entry about X."*), keep
working. Don't ask permission per entry — the user can delete what
they disagree with.
