# Decisions

Decisions still in force, with the reason and what was rejected — so they
aren't re-debated or broken by accident. Not a reference for the surface (the
skills are) and not a history: an overturned decision is replaced in place.
New entries land when the work does; open work keeps its decisions in
[planned.md](planned.md) until then.

Entry shape: the decision as the title, one line of what, **Why**, and
**Rejected** where a real alternative was weighed.

## Storage

### Frontmatter YAML is read and written by Knit's own serializer
Minimal hand-rolled reader/writer behind `IFrontmatterSerializer`; lenient on read, unknown keys preserved on write.
- **Why:** a YAML library's dynamic deserialization path throws under Native AOT; hand-edited files must never be
  rejected or stripped of fields a newer version added.
- **Rejected:** VYaml (AOT-first, yet its dynamic path breaks under AOT).

### An item's file name is fixed at creation; a retitle never moves the file
`set title=` / `edit` / web PATCH keep `<id>-<slug>.md`; `knit move` is the only rename and rewrites incoming links.
- **Why:** Targets are paths that verify and plain markdown viewers follow; re-deriving the slug on write broke
  children's `Parent` links and undid `knit move`.
- **Rejected:** resolve links by id (dead links in viewers); rewrite referrers on every retitle (N-file diffs, races).

### Items are flat files; attachments are id-prefixed siblings, allowed only when a body links them
`items/<id>-<slug>.md` + `items/<id>-<name>.<ext>`; an unlinked non-markdown file is a stray-file finding.
- **Why:** no command made the folder form and links resolved wrong in it; "nothing hidden" must cover binaries too,
  without auto-deleting them.

### The body's leading `# h1` stays on disk; `## Links` is written after it
- **Why:** viewers that don't render frontmatter still show a heading; a table before the h1 breaks every
  "first body line is the h1" rule (title drift, `show --json` strip). Render side strips the duplicate.
- **Rejected:** stripping an h1 equal to the title on write — the heading vanishes in plain viewers.

### Writes go to the resolved canonical file, at one chokepoint
`KbStore.ResolveWritePath` runs every write ref through `RefResolver`; never rebuild a path from the typed ref.
- **Why:** writing via a short ref (`adr:0002`) forked a duplicate `0002.md`; per-call-site fixes miss writers.

### Links live in a `## Links` body table, not in frontmatter
`Rel | Target | Notes`; the Target cell is a relative markdown link and is the structural edge Knit parses. The table is
authoritative; frontmatter `parent` is a derived convenience kept in sync on every write.
- **Why:** frontmatter renders in no markdown viewer; a relative link in the body is clickable in GitHub, VS Code and
  Obsidian, so the graph is browsable without Knit.
- **Rejected:** a frontmatter link list.

### Path links only in the Links table; prose uses typed refs
In body text a reference is `` `adr:…` `` / `` `item:<full-id>` ``; `knit verify` checks both forms.
- **Why:** prose paths rot silently on every move; typed refs resolve by identity and are mechanically checkable.

### `format_version` stamps the on-disk contract; stamp + guard, no migration engine
An integer in `.knit/config.json` (missing = `1`), `1` for the whole M-series. A newer repo version makes the CLI
refuse every command except `version`; the SPA warns instead.
- **Why:** the binary version bumps every release — too noisy to key migrations on; misreading a newer format is worse
  than not reading it. The engine waits for the first real breaking change.

### Ids are 18-char NanoIDs over `[0-9A-Z]`, generated inline
Items and comments; resolution case-insensitive; every "is this an item id" check goes through `NanoId.IsValid`.
Stored refs always carry the full id; only human output shortens.
- **Why:** short folder names; one-case alphabet avoids cross-OS case collisions; ~93 bits is safe without a
  coordinator. The shape check is also what tells an item link from a KB/design `.md` target — replace, never delete.
- **Rejected:** a central counter — agents create items in parallel and offline, so counters collide; storing short
  prefixes — unique today, ambiguous as the repo grows; re-encoded UUIDv7 (longer; time order unused — `rank`/`created` order things); dropping ambiguous
  chars (ids are pasted, not typed); a NanoID package (~10 lines over `RandomNumberGenerator.GetInt32`).

### No item index or cache file, CLI included; read item files once per invocation
- **Why:** measured — cost is the per-file open on Windows, not parsing; a cache saved ~0.1 s at realistic sizes but
  brings a format, integrity checks, locking, root-mode handling and stale answers. Revisit past ~5k items.
- **Rejected:** committed index (churn, merge conflicts); `index.jsonl` fast path (drifted, nothing read it);
  `.knit/.cache/items.cache` (designed — header + SHA-256, validate-never-trust — and parked).

### Every write under `.knit/` is atomic: temp + `File.Replace`
`AtomicFile`; readers open with `FileShare.Delete`; replace retries with backoff; a leftover temp is a verify stray.
- **Why:** concurrent agents on different items never see a torn file. `MoveFileEx` over a file a reader holds is
  denied even with `FileShare.Delete`; retry covers editors/scanners that don't share.

### Same-item collisions: compare a hash of the bytes read, then three-way merge
`ItemMerge` against the first read: list-shaped changes merge, scalars last-write, body refuses if both edited it.
- **Why:** several agents on one state make silent loss likely; the guard is cheap. mtime+length misses same-length
  edits (`todo`→`done`) within NTFS/SMB timestamp granularity, and the bytes were read anyway.
- **Rejected:** last-write-wins; repo-wide lock on every mutation (per-command cost, stale locks); mtime+length.

### Multi-file mutations get exclusion via `.knit/.lock`, not atomicity
`move`, `retype`, `relink`, `kb split/rename/delete`; lock names host, pid, op; a crash mid-way is repaired by
`verify` + `relink`. Every refusal (collision, lock, unwritable) is one `WriteRefusedException` → exit 4 / HTTP 409.
- **Why:** a journal with replay/rollback is a format, a replay path and a test matrix for a rare crash existing
  tooling already surfaces; owner info makes a stale lock on a share self-explaining.
- **Rejected:** journal / rollback.

### Renames go through the CLI; a raw `git mv` is reported, never healed on read
`knit move` / `knit kb rename` rewrite incoming Targets; breakage from a manual rename surfaces in `verify`, repaired by explicit `relink`.
- **Why:** Targets are filename-derived paths, so a manual rename silently breaks every incoming link; a command makes it visible and atomic.
- **Rejected:** stable frontmatter id decoupled from the filename (second source of truth); fuzzy auto-heal on read (implicit, no audit trail).

## Config & roots

### Config layers: defaults → user-level → repo `config.json` → `config.local.json`; never beside the binary
User level is the OS config dir (`KNIT_CONFIG_HOME` overrides); user `conventions.md` lives there too.
- **Why:** committed team policy beats personal preference, local overlay is the per-machine escape (git's model).
- **Rejected:** beside the binary (unwritable, replaced on update, shared across users); the cache dir (disposable);
  a global `config.local.json` (meaningless).

### `KnitConfig` is a positional record with ctor defaults; layers merge as raw JSON, never as the record
New keys are constructor parameters with defaults; `ConfigLoader` overlays `JsonObject`s; writers use `ReadRawLayer`/`SaveRawLayer`.
- **Why:** property initializers don't survive source-gen deserialization (absent `due_soon_days` read as `0`).
  After deserialization "absent" equals "default", so a record merge/save would write every default into the
  overlay and override settings the user never touched; provenance is computed from raw layers for the same reason.

### The root is found by walking up: `--root` → `KNIT_ROOT` → knit root redirect → walk
- **Why:** walk-up is the universal pattern (git, cargo, npm, jj, direnv); cwd-only would force every call from the
  repo root. Nested repos with their own `.knit/` resolve to the nearest — the same trade-off as git.

### A root is a `.knit/config.json`; the walk stops at the nearest `.git`
A `.knit` dir without `config.json` is skipped (note + verify stray); outside git the walk is unbounded; an
unexpected root is announced on stderr, not refused.
- **Why:** a folder merely named `.knit` (unpacked binaries in home) captured every path below it; existence, not
  parseability, so a typo'd config doesn't de-root the repo.
- **Rejected:** `items/` as a second sentinel; refusing `$HOME` / create (non-git repos are supported — false positives).

### Mounts are named, read-only; sources store `mount:` + a mount-relative `path:`
Mount paths are committed repo-relative; the mount is found lexically at write time, ambiguity is an error.
- **Why:** external repos have their own history and the unauthenticated API must not be a write vector; mount-relative
  storage makes a repoint one config change. Unlike `knit_root`, a mount follows the project's checkout layout.
- **Rejected:** literal `../` paths (repoint = rewrite all); covering mount detected at read time (ambient
  authorization, ambiguous on overlap); a prefixed `docs:path` string.

### The knit root is redirected by a configured path, not by git branch or auto-detection
`knit_root` points a worktree or project repo at one authoritative `.knit/` for reads and writes; no git shell-out.
- **Why:** worktrees and agents must share one current state; the main checkout is derivable from the `.git` file.
  Aggregating many repos read-only and multi-user writing are other mechanisms (`repos.json`, M2 hub).
- **Rejected:** git plumbing against a tracker branch (every mutation a commit, agents race on refs); redirecting
  whenever a worktree is detected (no opt-in; a worktree couldn't keep its own `.knit/`).

### `knit_root` is local-only; the repo commits a `requires` declaration, never the value
Unset-but-required refuses every command except the repair verbs (`root`, `config`, `mount`, `init`, …).
- **Why:** the path is a machine fact — committed it would leak into main and every worktree. Without a committed
  declaration a fresh clone silently uses its own empty `.knit/`; a warning lets reads look right on the wrong tracker.
- **Rejected:** committed repo-relative `knit_root`; storing it in `.cache/` or a per-user dir (invisible when
  debugging); flat paired keys or a sentinel value instead of the `requires` object.

### A broken redirect refuses every command; no fallback to the checkout's `.knit/`
`knit root` validates the target on write (exists + `IsRepo`, no write probe); bare `knit root` and `--off` run
before resolution and are exempt from the format guard.
- **Why:** quiet fallback *is* the split state, and agents ignore stderr. Remedies lead with reconnect, not `--off`
  — the usual cause is a share that's offline. Validating on write avoids self-inflicted bootstrap deadlocks; a
  write probe would create files in someone else's repo.

### The checkout is a pointer only: one hop, and a real minimal `.knit/`
The target's `.knit/` answers every other config key; `knit root` flattens chains at write time, runtime chains
refuse; the checkout keeps `config.json` + overlay (`knit init --external`).
- **Why:** layering the checkout's overlay over the target makes a half-repo with two `config.local.json` jobs;
  one hop can't loop, so no cycle detection. An overlay-only `.knit/` fails `IsRepo`, so the pointer would never be read.
- **Rejected:** checkout overlay layered over the target; runtime chaining; relaxing `IsRepo`; `knit root` scaffolding.

### A shared root is allowed under one writer at a time; network paths are not refused
- **Why:** path type polices the wrong axis — SMB with one writer is fine, a synced cloud folder looks local and is
  worse; writer count can't be detected. Real multi-user is M2.
- **Rejected:** refusing network paths.

### No handle is a state (`null`), not `""`; commands that record *you* refuse without one
`add`, `start`, `comment`, `--mine` refuse; `assign` and reads don't; a config `""` counts as unset. An item's `assignee: ""` still means unassigned.
- **Why:** `""` made items authored by nobody and `--mine` match every unassigned item; redirects make unset identity common. An item can be nobody's, a handle can't.

## Items & domain

### Order is one global fractional string rank; `reorder` respaces locally and says so
Tree, list and every other view sort by the same `rank`; a closed gap respaces one sibling group inside `reorder`,
reports `respaced N`, and read paths never respace.
- **Why:** a reorder touches one file (no sibling renumbering, small diffs, few merge conflicts); a respace is a visible
  write, never a silent one.
- **Rejected:** integer positions (renumber siblings); a per-view/per-column ordering; a manual rebalance verb or
  doctor card (built, then removed as dead code).

### Rank is the plan, state is fact — sorting by state is a view
State order in tree/list is a sort; writing it into ranks is the explicit, scoped (`--under` / `--all`), minimal
`knit reorder --by state` (only items outside the longest in-order run move).
- **Why:** re-ranking on `done` turns the plan into history and churns ranks on every close.
- **Rejected:** auto-reorder on state change; an unscoped repo-wide re-rank.

### `Blocked by` rows are live impediments; closing a blocker never edits the blocked item
A row is an impediment in its own right (beside the free-text `blocked:` reason) and feeds the dependency graph;
resolving is explicit (`unlink` / `unblock`). Closing a blocker only reports the now-stale rows (`blocked_dependents`,
verify warning).
- **Why:** git history is the audit trail, so rows outliving their meaning paint phantom timeline edges; a done
  blocker doesn't necessarily lift the impediment.
- **Rejected:** rows as history with the flag as sole truth (the original model — no dependency edges);
  auto-unblock on close; `unblock --keep-links` (manufactures stale rows).

### The hierarchy rule lives once in `Hierarchy.cs`; the SPA mirrors it literally
`src/lib/hierarchy.ts` copies the parent/type table and refusal text, held equal by parity tests; the server stays
authoritative. Refusal messages are derived from the rule, not hand-written per call site.
- **Why:** instant drag/drop feedback without trusting the client; hand-written refusal copies drifted (six of them).

### Leading `# H1` and frontmatter `title` are one value, synced both ways on write
CLI `set title` rewrites the H1; a body save refreshes the title from it; the renderer hides the H1 only when equal.
- **Why:** otherwise a CLI retitle is clobbered by the next body save; a drifted H1 stays visible (and is a verify
  error) instead of being silently hidden.
- **Rejected:** picking one side as winner and overwriting the other.

### `draft` is the only pre-approval state: excluded from selection, counted everywhere else
Skipped by `next` / `ready` / `pick`; counted in rollups, overdue and at-risk; `done` from `draft` refuses; `approve` promotes it.
- **Why:** `todo` already means "ready to pick"; an unspecified item with a near date is *more* at risk, not less.
- **Rejected:** state name `new` (implies recency); a per-actor default state (no reliable actor signal).

### Comments are deliberate input only; Knit never writes activity into them
State mutators append nothing to `## Comments`.
- **Why:** keeps comments signal (blocker context, questions), not status noise; history is git's job.

### A link's note lives on the referencing side, in the Links row
- **Why:** relevance is per-link context the target can't know — two items cite the same ADR for different reasons.
- **Rejected:** note on the target file; note as an item comment (loses its binding on rename).

### `agent_skills` and KB refs are set per item and unioned up the parent chain
`knit context` merges them from every ancestor; set once on the feature / epic, tasks inherit.
- **Why:** the skills an item needs vary per work item, so per-item beats one project setting.
- **Rejected:** project-level `agent_skills` config (ignores per-task variation).

### Branch / Repo / PR / Commit are explicit link rels with bare (non-path) target cells
PR / commit stored as the full URL (forge-agnostic), displayed shortened; branch never validated.
- **Why:** a bare cell keeps them out of every path scan (`verify`, `relink`); explicit rels read better than one generic rel.
- **Rejected:** a generic scheme-prefixed `Ref` rel; storing `org/repo#42` (needs forge config to resolve); frontmatter fields (duplicate, drift).

### `due` is a date-only ISO day, ISO input only, never inherited
- **Why:** no timezone ambiguity, one parse path; relative / natural input brings locale and week-start surprises. A due date is a
  per-item fact — unlike a slate, it doesn't flow down the tree.
- **Rejected:** datetime with timezone; relative input (`+5d`) — revisit only on demand.

## Planning

### Projects and todos are item types in the one tree, not a second plane
`project` is a root-only item, `todo` a PM-side leaf; project lifecycle reuses the item state enum — the only new field
is `health`. A tree node has one parent; a shared epic is consumed by other projects via `Blocked by`.
- **Why:** a parallel kind duplicates store, graph, verify and filters, and makes the common cross-audience link
  (launch todo blocked by a code task) second-class. Code vs PM work differs in who does it — that split belongs in views.
- **Rejected:** projects/todos as a separate kind with its own store; multi-parent / co-owned epics (at-risk
  propagation answers "who is impacted" better).

### A milestone is a dated doc plus a `Milestone` link — never a tree node, never an epic
- **Why:** epic = scope, milestone = time; one milestone collects slices of several epics and one epic spans several
  milestones, so welding them forces re-parenting on every schedule slip. Milestones are optional — an epic's `due`
  suffices until one target cuts across two epics.
- **Rejected:** unifying epic and milestone.

### A project is two halves — tracker item + doc folder — and its docs are link targets only
The item's `project:` field names `.knit/projects/<slug>/`; an orphaned half is a verify *warning*, not forbidden;
milestone creation is refused without `roadmap.md`. Roadmap is cross-cutting prose, not a milestone index. Project
docs never carry `## Links`; consumption edges live on items.
- **Why:** state vs authored prose — merging them special-cases project items in store/graph or pushes state into
  prose. Forbidding orphans is unenforceable in git (mv, merges); milestone create is fully Knit-mediated, so gating
  it is cheap. A prose milestone list is derived data that goes stale.
- **Rejected:** projects as a scoped mini-KB (KB buckets are a global fixed set — only the doc-folder mechanics are
  shared, via `Knit.Core.DocFolders`).

### Milestone rename/move re-points backlinks; deletes leave them dangling
Rename/move rewrite items' `Milestone` rows; milestone/project delete leave refs for verify; project delete detaches
its direct children (they become roots) instead of cascading.
- **Why:** after a rename the target exists, so a dangling ref would be wrong; after a delete, surfacing beats silent
  scrubbing (same as `knit delete`).
- **Rejected:** refusing cross-project moves; reassigning items' project on move.

### Slates are a markdown registry (`.knit/slates.md`) plus a slug on items, inherited at read time
Row order = display order; `rename` walks items; `rm` is lenient (values on items survive; the picker shows
registry ∪ item values).
- **Why:** diffable, human-editable, renders anywhere (the `## Links` precedent); ordering, empty slates and archive for
  free; read-time inheritance lets one task move slates without touching siblings.
- **Rejected:** JSON registry; slates derived from items only (no order, no empty slate); naming-prefix ordering.

### Work selection (`next` / `ready` / `pick`) scopes by `--slate` only
No `--milestone` / `--project` on them; an unknown slate errors.
- **Why:** a slate is the "now" cut — project/milestone work is pulled into a slate rather than growing parallel
  selection filters. An unknown slate returning "nothing ready" was a silent fail.

### Deadline timeline, not a Gantt
Only `due` exists; at-risk = open blocker undated or due after its implied deadline (min of downstream dues),
computed over the full graph regardless of filters. SPA draws custom SVG on a hand-rolled day scale; CLI prints a
text agenda plus the full graph in `--json`.
- **Why:** bars need start+duration and a data-entry burden nobody keeps up; at-risk is the feature's point. Day-granular
  ISO strings make `Date`-based scale libs a timezone footgun for one multiply.
- **Rejected:** Gantt with a `start:` field; vis-timeline / frappe-gantt / d3-scale (bar- or `Date`-centric);
  an ASCII dependency graph in the CLI (not legible, not monochrome-safe).

## Knowledge base

### KB buckets split by document lifecycle, not topic
ADRs (immutable), living docs (edit in place), logs (append-only), plus features / ideas / sources; topics are tags
and filenames.
- **Why:** topics aren't orthogonal — decisions cut across them; a lifecycle bucket gives each artifact one home and one
  mutation rule.
- **Rejected:** topic folders (ADRs duplicated or arbitrarily placed); feature-folder-primary (cross-cutting ADRs
  homeless, per-feature ADR numbering).

### One home per fact: a doc's kind comes from its location, its status from one place
KB entry type = bucket folder (no `kind:` field; singular ref `adr:`, plural folder `adrs/`); milestones = files under
`milestones/`; other project docs are free-form. ADR status lives only in frontmatter (no Nygard body `Status:`);
design status only in its `## Status` section.
- **Why:** a second copy (frontmatter `kind:`, a status twin) drifts from the first; location typing gives crisp
  verify rules.
- **Rejected:** a frontmatter discriminator + reserved `roadmap` slug for project docs.

### Designs are scratchpad outside the KB; plans are items; KB extraction is skill-driven
`.knit/designs/` holds flip-flops and open questions; the plan is the item subtree (`design:` field), not a plan file;
moving settled decisions into the KB is an agent walk-through, not a CLI parser.
- **Why:** design history isn't canonical knowledge; execution belongs in the tracker; extraction boundaries weren't
  known, so a parser would encode wrong assumptions.
- **Rejected:** designs as a KB bucket; `<slug>-plan.md` files; a `knit kb extract` command.

### ADRs get sequential `NNNN`; items get coordinator-free ids
- **Why:** ADRs are low-volume and immutable, so readable numbers are cheap (and survive copy/paste out of the folder);
  items are minted in parallel across branches, where counters collide on merge.
- **Rejected:** sequential item ids; UUID or slug-identity ADRs; a repo-spanning ADR namespace (needs a coordinator).

### ADRs are `proposed` when the decision is taken, `accepted` when its work closes
The ADR test applies wherever a decision is written; a design writes a `proposed` ADR, a decision outside a design
goes straight to `accepted`, `auto` only ever writes `proposed`. Every close the user confirms sweeps the `proposed`
ADRs whose `origin` names the closed subtree or its design. Skills only; the rule lives once in `knit`.
- **Why:** a "graduate to an ADR later" step no workflow ran left lasting decisions buried in archived designs,
  closed items and comments, so a fresh agent decided them again.
- **Rejected:** a promotion checkpoint over all design decisions at archive / close (judgement every time, skipped
  like the old step); a `knit verify` check for unpromoted decisions (most rightly fail the test, so it warns
  forever); `kb add adr --from` (`--origin` covers it); an `ADR candidate:` comment tag (the `proposed` ADR is the
  record).

### Grow-to-folder only for `feature` / `doc` / `source`; the folder must be a real entry
A folder needs its `readme.md`; sub-docs need an existing folder-form parent (`kb split` first, no auto-split).
`adr` / `log` / `idea` stay flat.
- **Why:** ADRs are numbered, logs chronological, ideas scratch; sources and docs accumulate. Splitting keeps every ref
  valid (the slug resolves `<slug>.md`, then `<slug>/readme.md`).
- **Rejected:** category folders that aren't entries (breaks "every KB folder has a readme"); grouping by tag only.

### Structured KB refs are rewritten mechanically; prose refs are only reported
Rename/delete rewrite or strip `Links` rows and frontmatter refs (`supersedes`, `superseded_by`, `feature`) by exact
match, sub-doc paths included; delete strips refs *before* removing the files. Prose refs are left to `verify`.
- **Why:** structured refs are exact tokens, safe to rewrite; prose is fuzzy and there is no rename journal. Ordering
  means an IO fault leaves refs pointing at a live entry, never dangling.

### Index files are wholly tool-owned, narrative docs wholly human-owned; the tracker gets no index
`index.md` / `kb-index.md` are regenerated from frontmatter on every KB mutation; Knit never writes sections into prose docs; `items/` has no index file.
- **Why:** a clean ownership line avoids in-place merging and clobbered prose. Items change state many times a day — an index would churn git or go stale, and `knit list` already answers it.
- **Rejected:** marker auto-sections (`<!-- knit:… -->`) in narrative docs; an items `index.md` or an "active items" section in `kb-index.md`.

## CLI

### On success stderr stays empty, except the root notice
`using knit root: <path>` goes to stderr on every command when the root isn't cwd or cwd's git root; nothing else does.
- **Why:** scripts discard stdout and watch stderr for problems — noise hides real errors. The notice is the only signal
  of an unintended root; a set-but-bad `--root`/`KNIT_ROOT` errors naming the override, never falls back to the walk.
- **Rejected:** refusing to create items under a non-obvious root — blocks legitimate redirects.

### No prompt ever blocks a non-TTY caller
Missing stdin = `--non-interactive`; destructive commands (`kb delete`) need `--force` without a TTY or with `--json`;
`init` without a TTY writes nothing unasked. `--force` never bypasses structural guards (`--delete-folder`).
- **Why:** a prompt with no TTY crashed `init`, the first command a new user runs; agents must never hang or
  silently destroy.

### Filtered `tree` keeps dimmed ancestor context; `--flat` opts into promotion (`↑`)
`--json` carries `match`; plain piped output can't tell context rows apart — use `--flat` / `--json`.
- **Why:** "where does this hit live?" is the common follow-up; `--flat` survives for grep/awk.
- **Rejected:** promotion as default — loses structure.

### Exclusion is `--exclude-type` / `--exclude-state`, not a negation syntax
- **Why:** `!` is a PowerShell operator needing quoting; explicit flags are scriptable.
- **Rejected:** `--type !bug` mini-language.

### `--dump` on `list`/`tree`: same selection, full content, refused above `--dump-max` (40)
`--json --dump` reuses the `show --json` shape; refusal names count + size estimate.
- **Why:** one call instead of N `show`s; a truncated dump that looks complete is worse than a refusal. Item count
  matches the filters the caller narrows with; bytes can't be predicted up front.
- **Rejected:** raw `body` on `JsonItem` (two JSON shapes per item); paired closer / adaptive fence delimiters.

### `ready` is the inventory, `next` is an opinionated leaf-work pick
`ready`: any open state, no `blocked` flag, containers included, rank order. `next`: never a project or an item
with an open child; in-progress → overdue → due-soon → priority → due → rank → id; human output names the winning key.
- **Why:** splitting the jobs left every `ready` caller (SPA, `pick`, scripts) untouched; the reason line keeps the
  pick auditable.

### Date-window filters are raw; only `--overdue` is state-aware
`--due-before` etc. keep closed items; compose with `--open`.
- **Why:** "what was due before X" is a legitimate retrospective query; uniformity would delete it or need an
  implicit exception.

### `knit status` takes scope flags only, never state filters
Dismissed leaves the denominator; health only when scope is exactly one project; unknown `--project` errors.
CLI and SPA share `StatusRollup`.
- **Why:** a % over a set without done items is always 0%; dismissed would cap a finished project below 100%;
  health of several projects can't merge; a silent empty scope reads as "empty project".

### Human output names items as `id "Title"`; free-text table cells wrap on words
`Out.Titled` for sentences (tables exempt); `Out.Prose` for free-text cells (slug/path columns fold).
- **Why:** agents copy the shape they see; Spectre's default fold splits words mid-letter in squeezed columns.

### Timeline: textual agenda + full `--json` graph, no ASCII graph
Dated milestones inline in the agenda; `--json` stays unclipped.
- **Why:** terminals can't draw dependency arrows legibly or monochrome-safe; agents and the SPA get
  nodes/edges/at_risk from JSON.

### `verify` severity tiers: structure = error, drift-prone scans = warning, machine state = info
Title drift (frontmatter vs h1) always errors; prose refs are warnings (exit 0, `--strict` → 4); status-page
staleness is info and never fails `--strict`.
- **Why:** prose scans are false-positive prone — hard-failing breaks CI on a doc example and erodes trust; title
  drift silently misleads every view.

### `verify` checks local targets for existence only inside the repo root or a resolving mount; never URLs
Prose links and KB source `path:` values (warnings): missing there is flagged; leaving the repo root with no mount
covering it is flagged as `outside_repo` without an existence check; under an unresolved mount the mount's single
row speaks for them.
- **Why:** inside, a missing file is content rot; outside, existence depends on the machine, but the link already
  works on no other one — and the usual cause is one `../` too many. Verify stays offline and deterministic.
- **Rejected:** checking URLs (network, flaky, breaks the no-outbound promise); existence checks outside the repo
  (machine-specific); a finding per source on an unresolved mount; links only into `.knit/` (a renamed repo file
  broke silently).

### Share links: forge kind per host via `forges` config; origin read live; ref `HEAD`; raw origin never output
Built-ins for common public forges; unknown host → non-zero exit with the `config set` hint (no prompt). Origin from
`.git/config` (common dir for worktrees), paths relative to the git root. Pasted links resolve by their `.knit/`
segment, same repo only.
- **Why:** SSH aliases / self-hosted hosts can't be inferred; `Inheritable` = answered once per machine; tokens in
  origin must not leak; `HEAD` survives missing origin/HEAD, reftable, renames; knit root may be a subfolder.
- **Rejected:** reading `~/.ssh/config` / `insteadOf`; bracket key syntax (shells glob it); current-branch links
  (rot on branch delete).

### `knit status report` writes a deterministic, marked GFM page and never overwrites a hand-written file
Marker first line, refused without it unless `--force`; day-only stamp, rank order → byte-identical reruns; path
from `RepoOnly` `status_report.path`, contained, not under `.knit/`. Knit never regenerates or commits it.
- **Why:** readers are non-Knit stakeholders on the forge; stable output gives empty diffs; mtimes reset on clone,
  so staleness reads the page's own `Generated` line.

### Colour is never load-bearing; the status page is the one emoji surface
No CLI colour-by-priority. The generated status page uses emoji (🟩/⬜ bars) despite the CLI monochrome rule.
- **Why:** the priority glyph already encodes level and colour strips under redirect/`--json`; the monochrome rule
  targets ANSI-stripped terminals, a rendered page has no such limit and code fonts lack matching block glyphs.

### Skill install has two styles (`claude`, `agents`), each instruction file gets its own Knit block
`.claude/skills` + `CLAUDE.md` vs `.agents/skills` + `AGENTS.md`; block copied, never imported (unless `CLAUDE.md`
already imports `@AGENTS.md`), refreshed with consent.
- **Why:** every common agent reads one of the two; `.agents/` is the vendor-neutral convention; no agent reads both
  files. "A repo with `.knit/` tracks its work there" lives in the skill description instead of a global pointer.
- **Rejected:** one adapter per agent (`IAgentAdapter`); `--agents-file global`; `knit skill show`.

### `skill install` defers to an existing global bundle; version stamp sits beside `SKILL.md`
Bare install skips a folder whose global bundle exists; `.knit-bundle.json` `{version, installed_at}`.
- **Why:** agents discover global skills in every repo, per-repo copies only drift; the Agent Skills frontmatter
  contract stays untouched. `KNIT_SKILLS_GLOBAL` keeps tests off the real home dir.

### `knit version` and `knit --version` print one identical line, with no build hash
Both route through `VersionInfo.Get`; System.CommandLine's built-in never answers;
`IncludeSourceRevisionInInformationalVersion=false`.
- **Why:** the single-line stdout is a scripting contract; every CLI has `--version`, so dropping it would answer
  "unknown option". "One way" is enforced on the output, not the spelling. A test pins it so an SDK default can't
  reintroduce the `+hash`.
- **Rejected:** removing `--version`.

## Skills

### Six skills; `knit-cli` is a lean core plus one-hop `references/`; PM workflow is its own skill
Shared doc-folder model (KB / designs / projects) lives once in `knit` Conventions.
- **Why:** per-skill descriptions trigger better than one merged skill; the core keeps silent-fail gotchas in every
  session while detail loads on demand (item session ~20k → ~13k words).
- **Rejected:** one skill + references for the whole bundle.

### Conventions rule is placed in three skill layers, with no CLI hint
`knit` description (always in context), `knit` body, a direct line in each workflow skill; repo beats user,
both beat Knit skills only.
- **Why:** agents often skip the router, and a pointer into an unloaded skill isn't followed.
- **Rejected:** per-call CLI hint (repeats), throttled hint (reaches the wrong caller), JSON field (noise).

### Skills are agent- and policy-neutral: no commit policy, no slash syntax
Skills say only that the CLI never stages or commits; cross-references read `` `knit-cli` skill ``.
- **Why:** whether an agent commits is the user's call and clashes with consumer repos; `/name` is Claude Code
  syntax.

### Writing-style rules are skill guidance only, no `verify` lint
- **Why:** title / line length are judgment; the only enforced style is the CLI's own `id "Title"` output.
- **Rejected:** verify lint for titles / line length; a no-emoji rule.

### Conventions may narrow agent behaviour, never widen it
They override skill guidance only — not CLI behaviour, `auto` stop conditions or permissions.
- **Why:** a shared repo's conventions file is the same injection surface as a cloned `CLAUDE.md`.

## Web API

### REST DTOs are separate from the CLI `--json` shapes
WebApi owns its DTOs (camelCase, `ApiJsonContext`); the CLI keeps `JsonItem` / `JsonCommentResult`.
- **Why:** CLI `--json` is a contract for shell substitution; coupling would force both to evolve together.
- **Rejected:** reusing the CLI JSON types — locks REST to the CLI shape and snake_case.

### Live refresh is file watcher → SSE with per-resource invalidation
Watcher on `.knit/` coalesces a 200 ms burst into one `created|changed|deleted` per `(kind, id)`; editor temp-then-rename
is reported as one `changed`. SSE frames (resource, activity, heartbeat) go through one write gate.
- **Why:** SSE is exactly the one-way "refetch" shape and AOT-trivial; per-resource keys stop a `git checkout` from refetching everything.
- **Rejected:** WebSockets — two-way overkill; polling — only the `--no-watch` fallback; one coarse "changed" event — refetch storms.

### A missed event is never silent: resync, not replay
`FileWatcher.Error` (buffer overflow) emits `resync` and drops the item cache; every SSE reopen invalidates all event-driven queries.
- **Why:** data is local, so a full refetch is cheap and always correct; overflow on bursts is real.
- **Rejected:** event ids + `Last-Event-ID` replay — state to keep for no gain.

### Only a visible SPA tab holds an SSE stream
Close `EventSource` when hidden, reopen + resync when shown.
- **Why:** plain-HTTP loopback is HTTP/1.1, ~6 connections per origin; ~6 open tabs stalled every fetch.
- **Rejected:** leader tab (Web Locks + BroadcastChannel) / SharedWorker — cross-tab protocol for tabs nobody looks at; HTTP/2 needs TLS.

### Item cache correctness never depends on the watcher
Every request revalidates against the file (mtime + size); entries within 2 s of their mtime are re-read (racy-git rule);
snapshots are immutable. Doctor verify reads uncached and heals the cache on mismatch, logging the files.
- **Why:** a lost event may only cost a late push, never wrong data; same-tick edits and FAT 2 s timestamps defeat mtime alone.
- **Rejected:** periodic full refresh; size-limited cache; the CLI notifying a running server.

### Two concurrency guards: ETag for stale views, store guard for disk races
GET returns an ETag (XxHash64); mutations require `If-Match` — 428 absent, 412 mismatch. Store-level refusals
(collision, `.knit/.lock` contention) surface as 409 `write_refused`. Structural KB ops (rename/supersede/split/delete) take no `If-Match`.
A cascade dismiss checks the parent's ETag only; each cascaded write is its own SSE event.
- **Why:** CLI, agents, editors and tabs write the same files; last-write-wins would silently clobber. Structural ops have no base revision.

### Clients never construct ranks
`POST /api/items/reorder` takes the moved ids (each with `ifMatch`), the parent and one neighbour; the server computes ranks,
all-or-nothing. `rank` is not patchable.
- **Why:** a client rank mirror duplicated Core logic in two languages and couldn't trigger server-side respacing; per-row calls reversed tail drops.
- Cycle / hierarchy pre-checks stay client-side on purpose — refuse without a round-trip; server stays authoritative.

### Catch-all route segments are terminal, so actions take refs in the body
`/api/kb/rename|supersede|split|delete` are POSTs with the ref in JSON; `/api/file?path=` / `/api/raw?path=`.
- **Why:** ASP.NET `{*catchall}` must be the last segment, so `…/{*slug}/rename` can't exist; a bucket name must not collide with a literal segment.

### Paging metadata rides in headers; list bodies stay bare arrays
`X-Total-Count` / `X-Has-More`; `limit` absent or ≤ 0 = unbounded.
- **Why:** existing list consumers keep working; no envelope DTO.
- **Rejected:** `{ items, hasMore }` envelope.

### Repo health lives under `/api/doctor/*`
- **Why:** keeps `/health` / `/healthz` free for a future liveness probe; `GET /api/health` stays the info endpoint.

## Security

### Cross-site writes: foreign `Origin` refused, and every write must be `application/json`
`RequestOriginGuard`: a write (not GET / HEAD / OPTIONS) with a foreign `Origin` gets 403; one without
`Content-Type: application/json` gets 415, bodyless ones included. A loopback bind also refuses a non-loopback `Host`.
- **Why:** the `Origin` check alone trusts the browser to send it; a JSON content type forces a CORS preflight the API
  never grants, so no page can fire the write. Endpoints parse `req.Body` themselves, so a `text/plain` POST would land.
- **Rejected:** a custom header (same effect, less conventional for curl users); `Sec-Fetch-Site` (also a browser signal);
  a CSRF token (needs a session the API doesn't have).

### The SPA never writes config, mounts or identity — it shows the CLI command to copy (except `repo_name`)
`/api/config` and `/api/mounts` are read-only (only `repo_name` renames in place, refused while `config.local.json` sets it); `identity_drift` repair is a copy-command. The user-scope conventions file is
writable only when bound to loopback.
- **Why:** committed vs local vs global layer is a decision a form can't make; an unauthenticated UI must not rewrite machine-wide
  or identity state, and on a wider bind the browser user may not be the machine's user.

### Path containment rejects any symlink below the root, not just the final target
`PathContainment.HasSymlinkBelow` checks every segment.
- **Why:** a cloned repo can plant a junction mid-path.
- **Rejected:** `ResolveLinkTarget` on the final target only — missed intermediate symlinks.
- Re-audit if `knit web` ever binds non-loopback by default.

### Files outside `.knit/` are view-only; deletes go by ref only
`/api/file` is read-only, 2 MiB cap, NUL-byte sniff on the first 8 KiB. KB delete removes only the entry's own folder inside
`.knit/kb/` — no arbitrary-path write or delete endpoint exists.
- **Why:** editing arbitrary repo files is a separate security conversation; the ref is the only safe handle.

### Raw bytes only for an allow-listed set of types; SVG is never served raw
`/api/raw`: png/jpg/gif/webp/avif/pdf, fixed content type, `nosniff`. SVG goes through `/api/file` → DOMPurify → inline.
- **Why:** repo files come from whoever pushed; sniffed HTML or raw SVG would run script in the origin of the mutating API.

## Search

### Search keeps no index: one live in-memory scan per query
`LiveScanSearcher` (Knit.Data) scans the store per query; no cache file, no warm-up, no reindex.
- **Why:** SQLite FTS5's native `e_sqlite3` shipped only as a dynamic lib, so AOT emitted a sidecar and `knit.exe` alone broke
  search; it was also the one derived cache that broke "folders are authoritative".
- **Rejected:** SQLite FTS5; static-linking a self-built SQLite (per-RID C toolchain); `DirectPInvoke` (no static lib, LNK1120).

### `ISearcher` is entity-keyed with no reindex / readiness members
`Query` + `LookupRef` → `SearchHit(Kind, Id, …)`; shared by `knit search` and `/api/search`.
- **Why:** results navigate to entities; a stateless scan has nothing to reindex. A future stateful backend must key by entity id
  and evict on KB / design rename (those keys are path-derived).
- **Rejected:** file:line keys — "ghost line" bugs.

### One matcher, server and client
Substring per token, multi-token AND, quality tiers (exact word > word-start > mid-word title > other). Server snippets are
plain text; the SPA re-finds and paints matches — no server `<mark>`, no `dangerouslySetInnerHTML`. Diacritic fold is a manual
Latin-1 map.
- **Why:** a second matcher had diverged; `InvariantGlobalization=true` makes `Normalize(FormD)` a no-op.
- **Rejected:** word-start-only prefix — surprising; length-switched match modes — a rule that silently flips.

## SPA

### Items have one navigator (tree / list) over one filter store; no parallel item views
`/items` toggles tree and list over the same URL-mirrored filters; aggregates are a strip above the tree.
- **Why:** every extra view re-implements filtering and drifts the moment a filter differs; one store keeps state across the toggle.
- **Rejected:** kanban board, SPA and CLI (team doesn't work off a board; accepted loss: drag-to-change-state);
  separate status/report view; "Ready / Now" screen (rank order ≠ execution order — a "next up" card misled);
  plan-execution view (duplicates the `under` filter).

### Stay on mermaid 11; 12 is parked
- **Why:** 12 hard-depends on `elkjs` (EPL-2.0, ~1.5 MB) for a layout Knit would switch off; 11 gets backported security fixes.
  Revisit on an unbackported advisory or once ELK is optional.

### A glyph only where it encodes a value; a scalar default gets none
`normal` priority shows no glyph; static fields (assignee, effort, dates) carry no icon; item types always show their type icon.
- **Why:** a static glyph is decoration and competes with the glyphs that carry state.

## Build & packaging

### One binary: the SPA is embedded in the CLI, guarded by a publish-time size budget
Vite `dist/` embedded as manifest resources; `CheckPublishBinarySize` warns at 35 MB, fails at 50 MB on every AOT publish.
- **Why:** single-file distribution is the product promise; the gate sits in MSBuild so every local publish — and any future CI — inherits it.
- **Rejected:** separate `knit-web` binary; SPA assets on disk beside the binary — both break single-file.

### The SPA is built by a plain MSBuild `Exec` target (`BuildSpa`), not SpaProxy
Incremental (Inputs/Outputs), skipped on design-time builds and `SkipNpmBuild`; HMR = Vite proxying to the host.
- **Why:** transparent and debuggable; `dotnet build` / `test` on a fresh clone needs only Node.
- **Rejected:** `Microsoft.AspNetCore.SpaProxy` — reflection-heavy, hard to debug.

### Trim / AOT warnings fail the AOT publish
`IlcTreatWarningsAsErrors=true`; build-time analyzers run via `IsAotCompatible`.
- **Why:** a trim / AOT warning is a runtime break in the single binary that no compiler error catches; ILC also sees
  warnings from dependencies that the build-time analyzers don't.
- **Rejected:** `TreatWarningsAsErrors` for all warnings — unrelated analyzer noise would block releases.

### Third-party license gaps are pinned by hand in `OVERRIDES`, never guessed
`tools/gen-npm-notices.mjs` refuses an unresolved license; a dual license or a missing field gets an `OVERRIDES` entry.
- **Why:** the notices ship inside the binary (`knit licenses`), so a wrong license is a wrong legal statement.
- `dompurify` (`MPL-2.0 OR Apache-2.0`): Apache-2.0 elected — no file-level copyleft, and its text ships anyway
  for `@chevrotain/types`.
- `khroma`: no `license` field in the lockfile, but it ships an MIT license file — a metadata gap, MIT applies.
- **Rejected:** MPL-2.0 for `dompurify` (file-level copyleft obligations for no gain).

## Testing

### SPA behaviour is tested in vitest + jsdom; no browser / e2e test runner
- **Why:** the decided UI behaviour (focus, keys, popovers, events) is DOM/event fact jsdom sees; a headless browser adds a
  second runner, a download and flakiness for no extra coverage.
- **Rejected:** Playwright-style headless-browser tests.

### Logic implemented in both C# and TypeScript is pinned by one shared fixture table
Both suites read the same JSON cases (e.g. `heading-anchors.fixture.json`: section anchors, read by `DocSectionsTests` and vitest).
- **Why:** two hand-kept copies of a rule drift silently; one table fails whichever side diverges.
