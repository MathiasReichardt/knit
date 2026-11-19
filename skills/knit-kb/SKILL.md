---
name: knit-kb
description: Knowledge-base operations for Knit. Capture decisions as ADRs, write feature/doc entries as implementation lands, add external sources, split long docs, repair broken refs. Owns the "don't pre-document" authoring discipline.
---

# knit-kb

Wraps `knit kb` for routine maintenance of the project KB under
`.knit/kb/`.

The local web UI (`knit web` → `/kb`) covers the same surface for
humans who prefer ad-hoc browsing or quick edits; the CLI stays
canonical for scripted / agent-driven workflows.

> **Run `knit conventions` once per session before the first write, and again after the context was compacted; they override the Knit skills' guidance, no other skill's.**
>
> **Conventions** — glossary, ref format, link table, reserved sections:
> see [`knit`](../knit/SKILL.md) skill > Conventions. This skill assumes you've
> read that block. For CLI mechanics (renames, `--watch`, flags), see
> [`knit-cli`](../knit-cli/SKILL.md) skill or `knit kb --help`.

## Subcommands

The argument the skill is called with picks the subcommand.

| Subcommand | Description |
|---|---|
| *(none)* | Discovery — list buckets + recent entries, route to a workflow. |
| `decision` | Capture an ADR for a decision just taken. |
| `source` | Add an external source (RFC, vendor doc, blog post) with summary + TOC bullets. |
| `split <ref>` | Promote a long `feature:` / `doc:` / `source:` entry to a folder; extract sub-docs. |
| `verify` | Run `knit verify` + walk findings; route into `knit relink` if needed. |
| `index` | Force `knit kb index` regen (per-bucket + master). |
| `help` | Skill description + subcommand list. |

## KB topic overview

Empty repos show no folders; first `knit kb add ...` creates the
bucket folder and `index.md` on demand.

- **features/** (`feature:<slug>`) — per-feature living docs.
  *Use when* a shipped feature needs documentation that outlives any
  single item (architecture sketch, integration surface, gotchas).
- **docs/** (`doc:<slug>`) — cross-cutting living docs: architecture,
  security, glossary, integrations, operations.
  *Use when* the topic spans features or describes the system as a
  whole (a glossary, the deployment story).
- **adrs/** (`adr:NNNN-...`) — numbered architectural decision
  records.
  *Use when* a decision has been **made** that binds later work and was
  a real choice (test: Capture a decision → ADR). One decision per ADR.
  Auto-numbered.
- **logs/** (`log:YYYY-MM-DD-...`) — dated entries: retrospectives,
  incidents, postmortems, meeting / call notes, status updates.
  *Use when* a thing happened at a point in time and the record is
  about *what occurred*, not *what is*.
- **ideas/** (`idea:<slug>`) — pre-tracker ideas, unstructured.
  *Use when* the thought is too rough for a tracker item, or you're
  capturing something to revisit before deciding whether to track it.
- **sources/** (`source:<id>`) — external references: RFCs, vendor
  specs, blog posts, books.
  *Use when* the work depends on an outside document you'll want to
  point at later.

**Finding an entry by content** — `knit search "<query>" --kind kb`
full-texts every bucket (title, body, tags) and returns `bucket:slug`
ids you can hand straight to `knit kb show`. `knit kb list` only
filters metadata, so reach for search when you don't know the slug.

## Authoring discipline

**Don't document what isn't implemented yet.** The single biggest cause
of KB rot is pre-loading entries during design or planning — by the
time the code lands, the doc disagrees with reality, and nobody notices
because nobody reads the lie. Write KB entries *as items complete*,
lagging the implementation.

The litmus test: *"if implementation changes tomorrow, does this entry
need rewriting?"*

- **Yes, and the whole entry becomes a lie** → don't write it yet.
- **Yes, but in a contained way (a diagram updates, a section gains a
  paragraph)** → safe to write; the entry is orientation, not claims.

Buckets sorted by how this rule applies:

- **adr** — naturally compliant. An ADR records a decision *made*
  (past tense), not a planned future decision — a `proposed` one too:
  it records a decision taken, not a claim that the code does it yet.
- **source** — naturally compliant. An external doc is a real,
  present thing; recording it is recording reality.
- **idea** — naturally compliant by construction. An idea is
  explicitly "not implemented, may never be." Safe to write freely.
- **doc** — safe for orientation (architecture, glossary, security
  model). The "Yes-but-contained" test applies.
- **feature** — **highest risk.** Easy to slip into "this is how
  user-mgmt will work" before it works. Write feature docs *as the
  feature's items complete*, not before.
- **log** — past tense by definition (retros, incidents). Compliant.

When in doubt, defer writing. An entry that doesn't exist is never
wrong.

Writing rules from [`knit`](../knit/SKILL.md) skill > Authoring style apply;
per bucket:

- **ADR:** Context / Decision / Consequences, 3–5 sentences each. Decision
  opens with the choice itself. Name the rejected options, one line of why
  each.
- **Feature / doc:** what *is*, in the present tense — not the plan, no item
  history, no "we added…". Open with what it is, in one line or a short
  paragraph.
- **Source:** open with what it is + why it matters here, in one line or a
  short paragraph; URL + retrieved date; summarise, quote only short excerpts.

## Workflows

### Capture a decision → ADR

When to write it, and with which status, is one rule:
[`knit`](../knit/SKILL.md) skill > ADR vs design `## Design Decisions`.

**The ADR test — offer an ADR only when both hold**, otherwise skip it:
- *It binds* — costly to reverse, or a rule later work must follow
  (without the record, the next person or agent would decide it again,
  maybe differently).
- *It was a real choice* — there were genuine alternatives, picked for
  reasons nobody can read from the code.

A decision that fails the test is still
recorded where it lives: the design's `## Design Decisions` while
designing, else an item comment. A standing rule also
belongs in the living doc that states current truth (`doc:architecture`);
the ADR keeps why and when.

Knit follows **Nygard / adr-tools** ADR conventions: both the body
h1 and frontmatter `title:` carry the `NNNN.` prefix (e.g.
`# 0007. Use UTC timestamps`). The CLI sets this up for
you; don't strip the prefix from either side, or `knit verify`
will flag a title drift.

- `knit kb add adr "<title>"` — auto-numbers, scaffolds frontmatter,
  drops you into a body template with sections for Context, Decision,
  Consequences.
- **Record where the decision came from** with `--origin` (repeatable):
  `--origin design:<slug>` for the design cycle it emerged from,
  `--origin item:<id>` for the item whose work produced it — or both.
  This is the structured back-link that lets a future reader (or agent)
  trace the ADR to its motivating plan; it's distinct from `feature:`
  (the feature living-doc the decision *shapes*). Each origin ref must
  resolve — a typo is rejected at add time, and `knit verify` later
  flags an origin whose target was deleted or renamed. Set it on an
  existing ADR with `knit kb set adr:<slug> origin "design:<slug>, item:<id>"`.
- Fill the three sections; keep them prose-tight (3–5 sentences each
  is usually enough).
- **Advance the status as the decision's life moves on**
  (`knit kb set adr:<slug> status <value>`). A new ADR is `proposed`;
  the lifecycle values are:
  - `proposed` — taken, its work not yet closed (the default on
    creation). Edit it in place while it is.
  - `accepted` — in force. Set at the sweep when its work closes, or
    right after `add` for a decision outside a design. Never edited
    after; a change is a new ADR that supersedes it.
  - `rejected` — proposed but decided against; keep the ADR for the
    "why not" record.
  - `deprecated` — was accepted, now retired, but *not* replaced by a
    specific ADR.
  - `superseded` — don't set by hand; it comes from
    `knit kb supersede` (below), which also wires `superseded_by`.
- If the ADR replaces an earlier one, wire it up:
  `knit kb supersede adr:<new-NNNN-slug> adr:<old-NNNN-slug>` — this
  flips the old ADR to `superseded` for you.
- Link from the feature doc whose architecture the decision shapes:
  edit the `## Decisions` section of `feature:<slug>` to add a one-
  liner pointing at the ADR.
- If the ADR governs work in flight, link it to the **item** too —
  bind it once at the lowest item the decision governs and let
  `knit context` inheritance carry it to descendants (see
  [`knit-work`](../knit-work/SKILL.md) skill > Adding relations). Don't
  re-link it on every sub-item.

**Auditing an existing repo** for decisions that were never recorded: read
archived designs and closed items, apply the ADR test, dedupe against
existing ADRs and skip what was superseded since. Two designs that
contradict each other become an item to settle, not an ADR.

### Add an external source

External docs (RFC, vendor spec, library design doc, blog post) get
recorded so future readers can find them.

1. Open the doc (browser, file). Skim it.
2. Pull a **1–2 line summary** in your own words — what is this, and
   why is it relevant to the project. Not a paraphrase of the doc's
   abstract.
3. Pick the 3–6 most useful TOC entries; capture them as a bulleted
   list in the body so future agents can grep for the right section
   without re-reading the whole doc.
4. `knit kb add source <id> --kind link --url <url> --summary "<line>"`
   (or `--kind file --path <relative-path>` for a file in the repo).
5. Paste the TOC bullets into the body. Don't editorialise inside
   `## Comments` — keep editorial separate from the source record.

**A file that lives outside the repo** needs a **mount** — a named
directory declared in config, because a bare `../` path is refused
(nothing may read outside the repo root unless config says so).

- `knit mount list` shows what is declared, with each mount's effective
  path and whether it resolves.
- `knit mount add <name> <path>` declares one; the path is relative to
  the repo root (`../company-docs`) so the committed config stays
  portable. Add `--local` to write the uncommitted
  `.knit/config.local.json` instead — use it when the location is
  specific to this machine — or `--global` for a directory every repo on
  the machine should see (one docs checkout used from several projects);
  a global path must be absolute or `~`-anchored, since a relative one
  would mean a different directory in every repo.
- Then `knit kb add source <id> --kind file --path <path>` — **pass the
  real path you can see** (`../company-docs/specs/plan.md`, or an absolute
  one). A path that leaves the repo root is matched against the declared
  mounts and stored split (`path: specs/plan.md` + `mount: docs`), with the
  chosen mount named on stderr. `--mount <name>` is only needed when two
  mounts overlap; an undeclared or non-covering mount is refused at add
  time, and a plain repo-relative path is stored exactly as written.
- **Why the split form.** The mount name is the single point of repointing
  — `knit mount set docs <elsewhere>` moves every entry riding on it at
  once. Don't hand-write a `../` path back into `path:` to make the entry
  look self-contained; use `knit kb show <ref>` (which prints the resolved
  path) or `knit kb show <ref> --source-path` (the bare path, for
  `$(...)`).
- Mounts are **read-only**: they resolve for viewing / opening, and no
  mutator accepts one. An external repo has its own history.

**If a mount does not resolve** (`mount 'docs' unresolved`, from
`knit verify` or the SPA), the KB entry is fine — only opening the file
is disabled, and it is a config problem. Offer the user exactly two
ways out, don't report a dead end:

1. the committed path is wrong for everyone —
   `knit mount set <name> <path>`; or
2. it is right for others but not here —
   `knit mount set <name> <path> --local`.

`knit mount list` names the layer each value came from (`global`,
`config.json`, `config.local.json`) and shows a row a later layer
overrides — a repo mount shadowing a global one of the same name is
deliberate, not a defect.

**An entry written before mounts existed** (or added wrong) carries a bare
`path: ../Docs/x.pdf` and no `mount:`. It still lists and shows — only
opening it in the SPA is refused — so this is a repair, not an outage:

1. `knit kb show <ref> --source-path` prints where it points, so the path
   can be checked before anything is rewritten;
2. declare a mount covering it (`knit mount add docs ../Docs`);
3. re-point the entry at the real path — `knit kb set <ref> path
   ../Docs/x.pdf` — and the split form is stored, exactly as at add time.
   Step 3 is per entry; there is no bulk adopt command yet.

The summary lives in frontmatter and is what `knit kb list` /
`knit context` will render alongside the ref.

### Write an entry's body

`knit kb edit <ref> --set-section "<h2>" --value "<text>"` — replaces
that section's content, or appends the section if absent. Use it for
every body write (filling an ADR's Context/Decision/Consequences,
pasting source TOC bullets, updating a living doc) rather than opening
the file: it reindexes the bucket afterwards, and `Links` / `Comments`
are refused so the structural parsers keep owning them. `kb set` is the
frontmatter counterpart — the two never overlap.

`knit kb edit <ref> --delete-section "<h2>"` (repeatable) removes a
heading and its content — for an ADR section the decision doesn't have.
`--value ""` is the other operation: it empties a section you intend to
fill. Don't leave template headings standing empty — `knit kb show`
prints the body verbatim, so a reader sees every one of them.

### Tag an entry

`knit kb set <ref> tags "<a>, <b>, <c>"` — comma-separated, written
as a list. Values are trimmed and deduped case-insensitively
(first-seen casing wins), so `"API, api"` stores `[API]`. `set` is
full-replace, not append — pass the complete tag set each time.
Filter with `knit kb list --tag <tag>`. Tags are thematic and
orthogonal to `feature:` (structural parent linkage).

### Split a long feature/doc, or group sources

`feature:`, `doc:` and `source:` grow into folders; `adr` / `log` /
`idea` stay flat by design (numbered, time-ordered, or a scratch list —
a folder buys them nothing).

Split a `feature:` / `doc:` when it grows beyond a soft threshold —
~400 lines, or 5+ distinct `##` sections that each want their own
context. Split a `source:` when its material accumulates: the parent
stays a real source entry and the children hang off it
(`source:vendor/api-spec`) — there are no pure category folders.

1. `knit kb split <bucket>:<slug>` — moves `<slug>.md` →
   `<slug>/readme.md` atomically. Existing refs keep resolving, and the
   relative paths that pointed at the old file are repointed for you.
2. Pick which `##` sections of `readme.md` to extract as sub-docs.
   Good candidates: long sections, sections referenced by multiple
   items, sections that diverge in tempo from the rest.
3. Create each sub-doc with `knit kb add <bucket> <slug>/<sub>` (a
   source child still needs its `--url` / `--path`). The nested slug form only works
   after the parent has been split — the command refuses if the
   parent is still flat. The result is `<slug>/<sub>.md` next to
   `readme.md`, with frontmatter scaffolded like a top-level entry.
   Keep a one-line pointer to it from `readme.md` (under the section
   heading it replaces, or a `## Sub-docs` index).
4. Update incoming references to point at sub-docs where appropriate:
   `feature:<slug>/<sub>`. From items, `knit link <id> --kb-ref
   feature:<slug>/<sub>`.
5. Rename sub-docs later with
   `knit kb rename feature:<slug>/<old> feature:<slug>/<new>` — same
   ref-rewriting guarantees as a top-level rename.

### Repair broken refs

After a hand-edit, a botched merge, or a stray `git mv` slipped past
the rename discipline:

- `knit verify` — walks every item and KB entry, lists broken
  `Target` cells (the linked file doesn't exist). Also warns on
  dangling body-prose refs in items, KB entries, designs and project
  docs: inline `` `bucket:slug` `` code-spans, `item:` refs without the
  full id, and `[..](path.md)` / image links into `.knit/` that resolve to
  nothing. Those are warnings (exit 0); `--strict` promotes them to
  exit 4 for CI. Read-only — the only prose `relink` repairs is a short
  `item:` ref, expanded while still unique.
- `knit relink` — interactive repair. For each break, pick a
  candidate (resolved by remaining id or slug match), edit the
  target manually, or skip.

Run `knit verify` after any merge that touched `.knit/` folder
structure.

### Regenerate indexes

`index.md` per bucket plus the master `kb-index.md` are tool-owned and
auto-regenerated on every `knit kb add/set/rename/supersede/split`.
If they get out of sync (you deleted one, a merge dropped a section):

- `knit kb index` — full regen of every index file.

## Rename discipline

**Never edit KB filenames or item folder names directly.**

- `knit kb rename <bucket>:<old> <new>` — renames the file (or the
  folder for grow-to-folder buckets), rewrites every incoming
  `KB ref` row across all items atomically, rewrites body-prose refs and
  markdown links to it (items, KB, designs, project docs), and **rewrites the entry's own
  identity field**: a source's `id:` is its slug, an ADR's `id:` is the
  number in its slug (so renumbering an ADR renumbers the field, and a
  target without a number is refused). Path-identified buckets — doc,
  feature, log, idea — have no `id:` and never gain one.
- `knit move <item-id> <new-slug>` — same on the tracker side.

After a rename or move, run `knit verify` and fix the prose warnings left
by hand — a ref the rename couldn't recognise. Gate CI on it with
`--strict` (`knit-cli` skill > Repair).

Manual renames break the `## Links` tables silently — the file is
gone, but every linked Target cell still points at the old path.
Recovery: rename back, or `knit verify` + `knit relink` to repair.

**An entry whose `id:` disagrees with its filename is a defect, not a
quirk to route around.** Refs resolve by filename, so the declared id
resolves nowhere — addressing the entry by it fails, and the failure
names the file that declares it. `knit verify` reports the drift with
both values; `knit relink --ids` repairs it from the filename. Never
"fix" it by adding a second entry under the stale id.

The entries-vs-folder-entries + rename/delete/index model KB uses is the
**shared doc-folder model** — designs and project workspaces store
markdown the same way. It's defined once in
[`knit`](../knit/SKILL.md) skill > Conventions > Doc folders; KB adds only the
bucket specifics (which buckets grow to folders, `kb split`). Project
doc folders reuse it — see [`knit-projects`](../knit-projects/SKILL.md) skill.

## Hit friction?

Friction (a missing flag, a workflow rough edge, a wrong skill
claim) goes to the feedback loop — see
[`knit`](../knit/SKILL.md) skill > Feedback loop. KB content goes in
`.knit/kb/`; tool meta goes in `.knit/knit-feedback.md`.

## What this skill does NOT do

- **Design cycles** — owned by the [`knit-design`](../knit-design/SKILL.md) skill.
  It creates designs, scaffolds the plan as Knit items, and
  references KB entries from items as work progresses. Its decisions
  that pass the ADR test become `proposed` ADRs.
- **Item↔KB linking mechanics** — `knit link --kb-ref <ref>` and the
  `## Links` table format live in [`knit`](../knit/SKILL.md) skill >
  Conventions and [`knit-cli`](../knit-cli/SKILL.md) skill.
- **Bulk import** — there is no `knit kb import`. Add entries one at
  a time so each gets a summary and the right bucket.
