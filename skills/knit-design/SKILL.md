---
name: knit-design
description: Feature-design workflow on Knit. Uses .knit/designs/ for design docs and scaffolds the plan as a Knit item subtree (epic -> feature(s) -> tasks). Owns the design-cycle interview, KB-aware design, and the milestone mapping.
---

# knit-design

Design cycles on Knit: design docs live in `.knit/designs/` (managed
by the `knit design` command group), and the **plan side is a Knit
item subtree**, not a separate markdown file. Items reference the
design via their `design:` frontmatter field.

Design cycles are for larger or cross-cutting work. A one-off item
needs no design — just `knit add` (see [`knit`](../knit/SKILL.md) skill >
Creating items).

> **Run `knit conventions` once per session before the first write, and again after the context was compacted; they override the Knit skills' guidance, no other skill's.**
>
> **Conventions** — glossary, ref format, link table, reserved sections:
> see [`knit`](../knit/SKILL.md) > Conventions. For CLI mechanics
> (renames, `--watch`, flags), see [`knit-cli`](../knit-cli/SKILL.md) skill or
> `knit design --help`.
>
> **Authoring discipline** — don't document what isn't implemented yet;
> see [`knit-kb`](../knit-kb/SKILL.md) skill > Authoring discipline. It
> applies to design docs too: write sections on demand, not
> defensively.

## Subcommands

Subcommands — the argument the skill is called with — map to phases of the design cycle. The CLI commands
below (`knit design ...`) handle file I/O; the agent-facing logic
lives in this skill.

| Subcommand | Description |
|---|---|
| *(none)* | Discovery — pick an active design or start a new one. |
| `<slug>` | Jump to the named design. |
| `new` | Create a new design, skipping discovery. |
| `status` | Cross-design progress: active vs archived, plan completion per design. |
| `review` | Audit the current design for gaps, stale open questions, missing security considerations, design/items drift. |
| `plan` | Scaffold (or update) the Knit item subtree for the current design. |
| `summarize` | Concise summary of the current design (elevator pitch + core ideas + risks). |
| `evaluate` | 8-dimension critique with bar chart + verdict. Thinking tool. |
| `export` | Clean export of the design (no todos / open questions / superseded). |
| `ticket` | Feature card text for paste into an external tracker. |
| `diff` | What changed in the design (and item subtree) since the last session. |
| `archive` | Move the design to `completed/` (`knit design archive`). |
| `help` | Skill description + subcommand list. |

## Workflow

### 1. Discovery

- `knit design list --json` to enumerate active designs — each row
  carries `title` and `status` (the `## Status` line), so you can
  present the choice without reading the docs.
- Looking for a design by topic rather than slug —
  `knit search "<query>" --kind design` (covers completed cycles too;
  ids come back as `design:<slug>`).
- If the user named a design (a `<slug>` argument), load it directly.
- Multiple actives → present a numbered list including "start new".
- One active → confirm "continue with this, or start new?"
- None → go to step 1b.

### 1b. New design setup

- Ask for a **slug** (short, kebab-case).
- `knit design new <slug> --title "<Title>"` — scaffolds
  `.knit/designs/<slug>-design.md` with the embedded template:
  frontmatter (`title`, `slug`, `created`), `# Title`, and six
  placeholder sections — **Overview**, **Goals**, **Non-goals**,
  **Approach**, **Open questions**, **Status**.
- **Fill or delete the placeholders — don't leave them.** A `## Goals`
  still holding `- ...` lies about the document's state as loudly as an
  empty `## Security Considerations` you added "just in case". Anything
  beyond the six gets added on demand as the design develops.

### 2. Load context

- Read the design doc into context.
- Check for an existing item subtree: `knit list --json` then filter
  by `design: <slug>` in frontmatter. If present, you're resuming;
  surface progress.
- **KB awareness — brief scan.** Run `knit kb list --json` for the
  titles + summaries (no body reads). Surface the top 3–5 entries
  that look related to the design topic: ADRs that may constrain
  the design, architecture / glossary docs to align with, sources
  already captured, ideas worth promoting. *Mention to the user when
  you find something relevant — don't silently absorb.*
- **KB awareness — lazy lookup.** As the design grows and topics
  surface, re-check the KB for that keyword (`knit kb list --tag X`
  or grep `kb-index.md`). Cheaper than a full scan up front.
- Read project code referenced by the design. For new designs,
  explore folder layout to build context before designing.
- **Coding guidelines.** Load relevant guideline skills so design
  decisions align with project conventions.

### 3. Scope

Only touch the design doc and (when planning) Knit items via the
CLI. Reading project code is encouraged. The one KB write during
design is a `proposed` ADR for a decision that passes the ADR test
([`knit`](../knit/SKILL.md) skill > ADR vs design `## Design Decisions`);
feature docs and the rest wait for the work (`knit-kb` skill > Authoring
discipline).

## Design document structure

`.knit/designs/<slug>-design.md` — markdown with frontmatter
(`title`, `slug`, `created`).

Seeded by `design new`. Fill the ones that apply and leave the rest — an
empty heading is inert, and there is no CLI verb that removes a section:

- **Overview** — what this is, why it exists. Required from the start.
- **Goals** / **Non-goals** — the scope boundary. Non-goals earn their
  keep: they're what stops the design creeping.
- **Approach** — technical approach, components, data flow, key
  decisions.
- **Open questions** — unresolved items.
- **Status** — draft / agreed / superseded. Its **first non-empty line
  only** is what `knit design list` shows as the doc's status, so keep
  it to a short phrase and advance it as the design settles. Leave the
  section empty and the doc lists with no status; rename the heading
  and the status silently disappears.

Write a section without opening an editor:
`knit design edit <slug> --set-section "Goals" --value "<bullets>"` —
replaces the section's content, or appends the section if it isn't there
yet, which is how the on-demand sections below get created. Active docs
only; `Links` / `Comments` are refused.

Added on demand, the first time each has content:

- **Requirements** — functional + non-functional.
- **Design Decisions** — significant decisions with context +
  alternatives + rationale, once `Approach` outgrows carrying them
  inline. Superseded entries stay, marked `[superseded]` with a
  pointer to the new decision. A decision that passes the ADR test is a
  one-line pointer here to its `proposed` ADR.
- **Milestones** — see below. Omit for short designs.
- **Sources** — references that informed the design. Each entry is
  a one-liner with link or KB ref (`source:<id>`).
- **Security Considerations** — threats, mitigations, compliance.
- **Todo** — open topics to design later. Items get resolved (folded
  into the design) or dropped; the section drains over time.

Writing rules from [`knit`](../knit/SKILL.md) skill > Authoring style apply, plus:

- Open with the problem + the proposed direction, in one line if it fits or a
  short paragraph if the design needs it.
- Prose is fine for reasoning; decisions as bullets with their rationale;
  open questions in their own list.

### Sources subsection (design ↔ KB)

If a source (RFC, vendor spec, blog post) informed the design:

1. Add a `## Sources` section to the design with a one-liner per
   source.
2. **If the source isn't already in the KB**, offer the user
   `knit kb add source <id> --kind link --url <url>` to capture it.
   Then update the design's Sources entry to reference the KB ref
   (`source:<id>`) instead of the raw URL.
3. The `knit-kb` skill workflow for adding a source applies — 1–2 line
   summary + 3–6 TOC bullets.

## Plan as Knit items

The plan side of a design cycle is a **Knit item subtree**, not a
separate markdown file. Items reference the design via the `design:`
frontmatter field, set automatically when scaffolding from this
skill.

**What items track vs what the design tracks.** Items hold the
*current plan*; the design doc holds plan *history* (see
[`knit`](../knit/SKILL.md) skill > Glossary). Superseded approaches stay
in the design's `## Design Decisions` with rationale; lasting
decisions are ADRs. Don't expect to reconstruct "we used to plan
X" from items alone — that's by design.

### Hierarchy rule

| Design size | Mapping |
|---|---|
| **No milestones / 1 milestone** | 1 epic (the design) → 1 feature (the work) → tasks (phases) → subtasks (T-numbers). |
| **2+ milestones, ship together** | 1 epic (the design) → N features (one per milestone) → tasks → subtasks. **Default for multi-milestone.** |
| **2+ milestones, ship independently** | N epics (one per milestone). Rare — only when audiences / schedules genuinely differ. |

**Prompt for the top item at plan time — don't force an epic.** A
design scales to its work: it may top out at a single **feature** or
**bug**, or at an **epic** when it groups several features (or related
bugfixes) that cohere into a whole greater than its parts — including a
coarse feature that turns out to need several smaller features. Before
scaffolding, ask which shape fits:

- *"Is this one deliverable (a feature / bug), or an epic grouping
  several? Create an epic umbrella, or hang the work off a feature /
  bug directly?"*

When an epic fits, its title carries the design name and
`knit list --under <epic>` is the natural "what's in this cycle" query.
When it doesn't, set `design:` on the top feature / bug instead (per
"Link the design to the plan's top item"). The hierarchy table above is
the shape *once an epic is chosen*, not a mandate to create one.

**Exception: trivially small designs.** If the design turns out to
be a handful of tasks under an *existing* feature (e.g. a tightening
pass on a shipped feature, a small set of fixes), **prompt the user**
before scaffolding:

- *"This is small. Map it under existing feature `<slug>` (no new
  epic), or create a new epic anyway?"*
- If mapped under an existing feature, that feature is the plan's top
  item: set `design:` on it (per "Link the design to the plan's top
  item" below), but create no new epic / feature. The tasks attach
  directly to the chosen feature.

**When milestones exist (2+), ask the user**: ship together (default)
or ship independently? Default to "ship together" — it's the typical
case.

### T-shirt effort

Items carry `effort:` as a string frontmatter field. Convention:
`XS`, `S`, `M`, `L`, `XL`. No tooling enforcement.

- `knit add` does not take `--effort`. Workflow is:
  `knit add ... → knit set <id> effort=M`.
- Set effort on epics, features, and top-level tasks (= phases).
  Leaf subtasks usually don't need it.

### Milestones table

If the design has milestones, the design doc gets a `## Milestones`
section with one row per milestone:

```
| # | Milestone        | Description                | Effort | Risk | Complexity |
|---|------------------|----------------------------|--------|------|------------|
| 1 | API surface      | Endpoints + auth flows     | M      | L    | M          |
| 2 | UI integration   | Notification bell + polls  | M      | S    | M          |
```

Brief: bullet-point or one-liner per row. Effort / Risk / Complexity
use the same t-shirt sizes as items.

The `## Milestones` table is the design's own planning record. When a
milestone must become **first-class and linkable** — items pointing at
it, roadmap tracking — promote it to a project milestone sub-doc and
link items with `knit link --milestone`. That workflow (projects,
roadmap, milestone lifecycle) lives in
[`knit-projects`](../knit-projects/SKILL.md) skill.

### Example: short design (no milestones)

Design: `notification-bell-design.md` — adds an in-app notification
bell. One ship cycle, no internal milestones.

```bash
# Epic = the design umbrella
knit add epic "Notification bell"
knit set <epic-id> effort=M

  # Single feature = the whole work
  knit add feature "In-app notification bell" --parent <epic-id>
  knit set <feat-id> effort=M

    # Phases = top-level tasks under the feature
    knit add task "Add /notifications API endpoint" --parent <feat-id>
    knit set <p1-id> effort=S
      # T-numbers = subtasks
      knit add task "Define the notification API schema" --parent <p1-id>
      knit add task "Implement the endpoint handler" --parent <p1-id>

    knit add task "Add BellComponent with polling" --parent <feat-id>
    knit set <p2-id> effort=M
      knit add task "Build the bell component" --parent <p2-id>
      knit add task "Show the unread count badge" --parent <p2-id>

    knit add task "Wire to app shell" --parent <feat-id>
    knit set <p3-id> effort=S
```

`knit tree --under <epic-id>` renders:

```
Notification bell                              [epic, M]
└── In-app notification bell                   [feature, M]
    ├── Add /notifications API endpoint        [task, S]
    │   ├── Define the notification API schema [task]
    │   └── Implement the endpoint handler     [task]
    ├── Add BellComponent with polling         [task, M]
    │   ├── Build the bell component           [task]
    │   └── Show the unread count badge        [task]
    └── Wire to app shell                      [task, S]
```

For multi-milestone designs, replace the single feature with N
features (one per milestone) under the same epic.

### After scaffolding

- **Link the design to the plan's top item — once.** Set
  `design: <slug>` on **whatever the top item of this plan is**: the
  epic normally, or the existing feature for the small-design exception
  above. `knit set <top-id> design=<slug>`.

  Placement principle — **don't repeat; bind at the top**:
  - One binding, on the top item. `knit context` resolves the design
    own-or-nearest-ancestor, so every descendant task already sees it
    (`inherited_from` names the source).
  - Never re-set `design:` on sub-items — inheritance covers them. Only
    override on a sub-item if a subtree genuinely follows a *different*
    design (rare).

- **`agent_skills:` (frontmatter, optional).** Skill ids to activate
  when working items in this subtree. Unioned across the parent
  chain by `knit context`, so a skill declared once reaches all
  descendants.

  Placement principle — **don't repeat; place at the right scope**:
  - General to a whole design / area → on the epic or feature.
  - Specific to a phase of work → on the phase item.
  - Specific to a single leaf (rare) → on the leaf itself.
  - Never duplicate the same skill on multiple items in one chain —
    inheritance does that for you.

  Omit when nothing especially relevant applies.

  Example: `knit set <id> agent_skills=csharp-style,result-types`

- **`## Code pointers` (body section, optional).** One-line bullets
  pointing at code worth reading before starting the task. Place on
  **leaf tasks** — the granularity where pointers are actually
  useful. Not inherited (intentionally; pointers are task-local).

  Conventions:
  - Prefer **symbol names** over `file:line` — `ItemUtils.Save`
    survives renames; `src/Knit.Data/ItemUtils.cs:42` doesn't.
  - Use `path:line` only when the reference is non-symbolic (config,
    markdown, generated code) or a specific line genuinely matters.
  - Treat as starting points, not an exhaustive list — the codebase
    is the truth.
  - Omit when nothing especially relevant applies.

  Example:

      ## Code pointers

      - `ItemUtils.Save` — write path all mutators go through
      - `RepoVerifier.ScanItemFiles` — surfaces item files that fail to load
      - `.knit/config.json` — repo identity / defaults

  Write non-interactively via:
  `knit edit <id> --set-section "Code pointers" --value "<bulleted body>"`

- Items don't get acceptance criteria written for them ahead of
  time. Add them as work approaches per item. Non-interactive:
  `knit edit <id> --set-section "Acceptance Criteria" --value "<bullets>"`.
- **Every scaffolded item follows [`knit`](../knit/SKILL.md) skill > Conventions >
  Writing items** — title form per type, a body opening, no empty headings.
- Execution is the `knit-work` skill's job. This skill stops here.

## Behavior while designing

- **Interview mode.** Multiple open decisions → walk through them
  one at a time rather than dumping a wall of questions.
- **Suggest alternatives.** Proactively propose alternate
  approaches with trade-offs.
- **Warn on risks.** Flag security, scale, and "looks like
  over-engineering" patterns early.
- **Recommend libraries.** When a well-known library solves the
  problem, suggest it instead of designing a custom solution. For
  non-trivial dependencies (anything load-bearing — skip the writeup
  for a small utility), capture in the design:
  - **Name & version** considered.
  - **License.** Default to permissive (MIT, Apache-2.0, BSD, ISC,
    Unlicense, MPL-2.0 — note MPL is weak file-level copyleft). Flag
    copyleft / source-available (GPL, AGPL, SSPL, BUSL, ELv2,
    PolyForm) as a decision point for the user; never silently pick.
  - **Maintenance verdict** — active / slow / abandoned, backed by
    last release **and** last commit (stable libs can be quiet on
    purpose; abandoned ones are quiet by neglect).
  - **Adoption** — downloads / stars / vendor or foundation backing,
    plus any deprecation path.
  - **Fit constraints** — runtime / platform requirements that are
    hard yes/no for the project (e.g. AOT-safe, ESM-only, SSR,
    bundle / binary size budgets). Skip if none apply.
  - **Trade-offs** — what it brings vs. what it locks in (transitive
    deps, extensibility ceiling, exit cost).
  - **Alternatives** — for load-bearing picks, name 2–3 candidates in
    a small table with the winner and a one-line "why this, not the
    others". Single-candidate picks are fine for low-stakes choices.

  If a strong candidate fails the license / maintenance / fit bar,
  **mention it and say why** — don't silently omit it.
- **Reference existing code.** When a decision relates to a
  specific class / interface, name it (`OrderService.CreateOrder`,
  `IPaymentGateway`).
- **Reference existing KB.** When a design depends on or aligns
  with an existing ADR, glossary term, or architecture doc, link to
  it. If the source informing your decision isn't in the KB yet,
  offer to add it (see Sources subsection above).
- **Diagrams.** Offer Mermaid diagrams for architecture / data flow
  / state machines. Ask before generating. How to draw them and pasted
  pointers: [`knit`](../knit/SKILL.md) skill > Diagrams.
- **Be aware of existing design decisions.** Before proposing a
  decision, check `knit kb list --bucket adr` for ADRs that may
  already settle the question or constrain the new design. Mention
  it when found.

## Subcommand details

### `status`

- `knit design list --json` (active) + `--completed --json`
  (archived). For each active, find the linked epic and report
  open-questions / todos / item completion percentage.
- Compact table.

### `review`

- Re-read the design doc and the linked item subtree.
- Check: inconsistencies between design and items, empty
  reserved-name sections, stale open questions, unaddressed
  security concerns, design decisions without rationale, items in
  the subtree without acceptance criteria past a certain progress
  point.
- Report findings + suggested fixes.

### `plan`

- Run discovery to pick a design.
- If no item subtree exists yet, scaffold per the hierarchy rule above.
- If a subtree exists, **propose a diff** — never apply silently:
  1. Read the design doc's milestones + phases (or feature breakdown
     for non-milestoned designs).
  2. Read the existing subtree: `knit list --under <epic-id> --json`.
  3. Build three lists:
     - **Add** — items in the design that have no matching subtree
       entry (match by title, then by phase / milestone position).
     - **Stale** — subtree items whose corresponding design section
       has been deleted or substantially rewritten.
     - **Status mismatch** — subtree items whose state contradicts
       the design (e.g. design marks a milestone shipped but linked
       items are still `todo`).
  4. Present each list to the user. **Adds** are applied on confirm
     via `knit add ... --parent <id>`. **Stale** items are flagged
     but never auto-deleted — closed items stay as history; open
     stale items get a comment and the user decides. **Status
     mismatches** become a question, not a write.
- For multi-milestone designs, run the "ship together vs ship
  independently" interview before scaffolding.

### `summarize`

Concise structured summary, readable in under two minutes. Omit any
section with nothing to say.

- **Elevator pitch** — one sentence.
- **Core ideas** — 3–5 bullets.
- **Behavior changes** — what existing behavior changes.
- **Breaking changes** — API / schema / contract incompatibilities.
- **Out of scope** — explicit exclusions.
- **Open risks** — Open Questions + currently-blocked subtree items.
- **Status** — item completion via
  `knit list --under <epic> --state done --json` vs total subtree
  size; call out blocked items separately.
- **Evaluation snapshot** — if `evaluate` has run: verdict + one
  sentence. No bar chart repeat.

### `evaluate`

8-dimension critique. For each dimension, reason before scoring:

- **Design Quality** — clear, consistent, well-reasoned?
- **Complexity** — added complexity, justified?
- **Over-engineering** — simpler solution available?
- **Simpler alternative** — ~20% effort for ~80% benefit path?
- **Value / Benefit** — who, how many, how significantly?
- **Risk of doing** — regressions, security; also: what's likely to
  trigger `knit block` on items mid-execution? Dependencies not yet
  captured as `Blocked by` rows?
- **Risk of not doing** — cost of skipping (tech debt, user pain)?
- **Reversibility** — easy to remove / change later? A decision is
  reversible-enough if a future ADR can `knit kb supersede` it
  without rewriting completed items.

Produce a bar chart (1–10, `█` filled, `░` empty), then a **Verdict**
with a label and 1–2 sentences:

- *No-brainer* / *Do it* / *Big but worth it* / *Consider if needed*
  / *Only if free resources* / *Not worth it* / *Simplify first*

**Confidence note.** If the design is too abstract to assess
reliably, say so explicitly. Better to flag uncertainty than produce
a misleading chart.

After the verdict, ask whether to save to the design doc. Default:
skip — evaluation is a thinking tool, not always a permanent record.

### `export`

Clean consolidated markdown of the design for copy / save. Excludes
Todo, Open Questions, superseded decisions.

### `ticket`

External-tracker handoff only (Jira / Linear / GitHub Issues) — Knit
is the tracker, so internal handoffs use `knit show <epic-id>` or
`knit context <epic-id>`. When generating: title (≤70 chars,
action-oriented), 2–3-sentence description focused on user benefit,
5–7 acceptance bullets from Requirements, plus out-of-scope,
dependencies, and breaking-change notes if relevant. Detail belongs
in the design, not the ticket.

### `diff`

Summarize changes to `<slug>-design.md` (via git history) and the
linked item subtree since the last session.

### `archive`

When the design has shipped and the linked items are all `done`:

- `knit design archive <slug>` — moves
  `.knit/designs/<slug>-design.md` →
  `.knit/designs/completed/<slug>/<slug>-design.md`.
- `--cycle <n>` to add a cycle suffix (`<slug>/<n>-design.md`) if
  the design iterates and you're keeping each cycle's snapshot.
- Markdown links to the doc, and its own relative links, are repointed;
  `design:<slug>` refs keep resolving to the archive.
- Confirm with the user before archiving, then sweep the design's
  `proposed` ADRs ([`knit`](../knit/SKILL.md) skill > ADR vs design
  `## Design Decisions`). Dropping a design sweeps them too, as
  `rejected`. The items stay in
  `.knit/items/` (closed); they're queryable forever via
  `knit list --under <epic-id>`.

## Hit friction?

If a design step wants a knit command that doesn't exist, or a
subcommand here behaves wrong, log it via the feedback loop — see
[`knit`](../knit/SKILL.md) skill > Feedback loop. No project content
(design titles, decisions, requirements text) in entries.

## What this skill does NOT do

- **Execute items.** That's the [`knit-work`](../knit-work/SKILL.md) skill.
  Use `knit next --under <epic-id>` to pick the next ready item.
- **Write KB entries** beyond `proposed` ADRs. That's the
  [`knit-kb`](../knit-kb/SKILL.md) skill.
- **Edit code.** Designs are markdown + items only. Reading code is
  encouraged; writing it isn't this skill's job.
- **Auto-create acceptance criteria.** Items get acceptance criteria
  filled in as work approaches them, per `knit-work`.
