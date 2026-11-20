---
name: knit-work
description: Item execution workflow on Knit. Use when the user asks to work on a tracked item — "do <id>", "start / work on / implement / fix / continue <id>", "next item" — or to finish one ("done <id>", "close <id>"). Rule even without reading further — `knit start <id>` before any work; `knit submit <id>` when the work is finished and waits for the user, `knit done` once they confirm (in `auto`, a leaf item closes on met criteria; a feature / epic / project goes to `review`). Owns knit next / context / start / done, the finish-what-you-started ordering, in-flight decision capture, and the `auto` multi-item flow with its stop conditions.
---

# knit-work

Drive tracked items to done. The item is the brief
(`knit context <id>` replaces re-reading a plan markdown) and state
moves through `knit start` / `knit done` / `knit block`.

**What items track.** Items hold the *current plan* — see
[`knit`](../knit/SKILL.md) skill > Glossary. Concretely for execution:
if the plan changes mid-flight, **edit the item body** rather than
leaving the old version in place with a "actually we're doing X"
comment. Plan history goes in the design doc's `## Design
Decisions` or an ADR. `knit comment` is for human / process traffic
(blockers, questions, bug notes) — not for narrating implementation
choices.

> **Run `knit conventions` once per session before the first write, and again after the context was compacted; they override the Knit skills' guidance, no other skill's.**
>
> **Conventions** — glossary, ref format, link table, reserved sections:
> see [`knit`](../knit/SKILL.md) > Conventions. For CLI mechanics
> (renames, `--watch`, flags), see [`knit-cli`](../knit-cli/SKILL.md) skill or
> `knit <cmd> --help`.
>
> **Authoring discipline** — don't document what isn't implemented yet;
> see [`knit-kb`](../knit-kb/SKILL.md) skill > Authoring discipline.
> Feature docs land with the work, not as scaffolding before it; ADR
> timing is [`knit`](../knit/SKILL.md) skill > ADR vs design
> `## Design Decisions`.

## Subcommands

The argument the skill is called with picks the subcommand.

| Subcommand | Description |
|---|---|
| *(none)* | Pick the next ready item and execute it. Stops after one item. |
| `<id>` | Execute a specific item by id (full id or unambiguous prefix), even if out of order. |
| `next` | Pick + execute the next ready item without re-prompting for selection. Stops after one item. |
| `under <id>` | Pick the next ready item *under* a parent (epic / feature). Stops after one item. |
| `skip` | Stop work on the current item with a reason. Records a `knit comment` capturing why; does not move state. |
| `block` | Mark the current item blocked by something concrete (a missing dep, a question, an external system). Uses `knit block --by` for an item, `--reason` for anything else; state is untouched. |
| `unblock` | Resolve blocking: `knit unblock` removes every `Blocked by` row and the reason. Resolving one blocker = `knit unlink` that blocker; the item stays blocked while other impediments remain. |
| `dismiss` | Abandon the current item without completing — sets state to `dismissed`. Offers cascade when there are open children. Uses `knit dismiss`. |
| `status` | Progress overview: ready / in-progress / review / blocked-flag / done / dismissed counts across the current scope. |
| `auto <parent-id>` | Drive every actionable descendant of a parent to done in one loop. Leaf items (`task`, `bug`, `todo`) close on met acceptance criteria; `feature` / `epic` / `project` go to `review`. See [Auto sub-flow](#auto-sub-flow). |
| `help` | Skill description + subcommand list. |

## Pick-next ordering: finish what you started

`knit next` encodes a single intent: **complete what you've started
before starting something new.** It recommends one item; `knit ready`
is the wider inventory and is *not* pre-sorted for you (plain rank).

- **Leaf work only.** `next` never returns a `project`, and skips any
  item that still has an open child — those are containers you plan
  with. A childless feature is pickable; you can work it directly.
- Order, first key that separates two candidates wins:
  1. **in-progress before untouched** — the finish-what-you-started rule.
  2. **overdue**, then **due-soon**.
  3. **priority** (critical → someday).
  4. earlier **due date**, then **rank** (`knit reorder <id> --before <other>`).
- The human output prints a dim line naming why the pick won ("in
  progress · overdue by 2 days · critical priority"). If that reason
  looks wrong for the situation, that's your cue to override rather
  than to distrust the tool.
- Prerequisites are encoded as `Blocked by` rows, not parent-child;
  blocked items are already filtered out before any of this applies.
- Practical pattern: lean on `knit next --under <epic>` to drive one
  branch to completion before moving to the next. Override with an
  explicit `knit reorder <id> --before <other>` when the ordering is a
  standing decision rather than a one-off.
- **Priority never outranks started work.** A `critical` item does not
  pull you off something in progress — it wins only among items nobody
  has touched, and only after deadline pressure. Never abandon an
  in-progress branch to chase priority without the user's say-so.

## Scanning a big tracker

When the default render is too noisy to think about, filter:

- `knit list --open` — everything still live (drops `done` *and*
  `dismissed`).
- `knit tree --open --exclude-type bug` — focus on the build-out path
  in hierarchy form, hiding bug noise.
- `knit list --open --under <epic>` — scope to one epic.
- `knit ready --under <epic>` — everything workable under it (containers
  included; `knit next` is what filters down to one leaf). `--assignee
  <handle>` asks the same question for someone else — as does
  `knit next --assignee <handle>` when you need one recommendation to
  hand over.
- `knit list --blocked` — the inverse of `ready`: what's stuck and on
  what. Worth a pass before reporting progress, since blocked items are
  invisible in every ready/next view by design.
- `knit tree --slate <name>` — scope to a **slate**, the named
  what-to-work-on-next selection (see [`knit-cli`](../knit-cli/SKILL.md) skill
  > Slates). If the tracker defines slates, prefer the active slate as
  your working scope before reaching for ad-hoc filters — `knit next
  --slate <name>` / `knit ready --slate <name>` pick inside it.
- `knit tree --milestone <project>/<slug>` — scope to the items aiming
  at one **milestone**. Shaping the milestones / roadmap themselves is a
  planning task, not execution — see
  [`knit-projects`](../knit-projects/SKILL.md) skill.
- `knit timeline` — deadline triage: what's due, what's overdue, and
  which blockers endanger a deadline (see
  [`knit-cli`](../knit-cli/SKILL.md) > Timeline). Use it when the
  question is *when*, not *what*.
- `knit status [--under <epic>|--slate <name>|--project <slug>]` — the
  rollup rather than the rows: completion %, per-assignee load, blocked
  and overdue in one read. Reach for it when you're *reporting* on a
  scope instead of picking work inside it.

Full flag list: [`knit-cli`](../knit-cli/SKILL.md) > Filters, sort,
dump.

### Catching up on a slice: `--dump`

Start cheap: `knit status --under <epic>` for where the slice stands,
`knit context <epic>` for its brief. Dump only when you need the items'
content.

When you need to *understand* a slice — picking up an epic, reviewing a
slate, answering a question that spans items — add `--dump` to the filter
that already describes it, instead of looping `knit show`:

```bash
knit tree --under <epic> --dump
```

Every dumped item leads with its full and short id, so act on anything
you find without listing again. A dump is expensive — narrowing the
filter is how you pay less, and it refuses outright above `--dump-max`.
When you're acting on one known item, keep using `knit show`.

## Workflow

**In a git worktree, share the main checkout's state first.** A worktree's
`.knit/` is its branch's copy, so items you touch there diverge from every
other agent's. Run `knit root --worktree` once, non-interactively — then every
command reads and writes the main checkout's `.knit/`. `knit root --local`
keeps the worktree's own on purpose. Check with `knit root --json`. Until one
of them runs, every command prints a stderr note saying so. A branch older than
`.knit/` has none at all: merge main into it, then `knit root --worktree` —
never `knit init` there (a second `config.json` conflicts with main's). In a
bare repo's worktree there is no main checkout: run the `knit root <path>` the
note names. Once redirected, never edit the worktree's own `.knit/` files —
[`knit-cli`](../knit-cli/SKILL.md) skill > Root.

**A fresh clone may refuse until it is set up.** When every command says the
checkout requires a setting it hasn't filled in, run `knit config setup` with
the answers as flags (`--knit-root <path>`, `--mount <name>=<path>`), using
the hint the error quotes. A refusal for a missing handle is fixed with
`knit config set --global handle <you>`. Ask the user for a path or handle
you don't know; don't guess one.

### 1. Select item

- No id given → `knit next [--under <id>] [--slate <name>]` to pick the
  next ready item; `--slate` keeps the pick inside the current slate. If none, report "nothing ready" and stop.
- Id given → `knit show <id> --json` to confirm it resolves and
  isn't already `done`.
- If an item is already `in-progress`, ask the user: *"Item X is
  still in progress from a previous session — resume it or pick
  something else?"* Don't silently start a different one.
- **A `draft` is not ready work.** `knit next` never hands you one, but
  a user can name one directly. Nobody has specified or approved it, so
  **ask before executing it** — confirm the scope, then `knit approve
  <id>`. Never approve one on your own initiative.
- **Claim it now: the item reaches `in-progress` before any work**, not
  retroactively once it's finished. `knit start <id>` is the usual path —
  it also assigns the item to the current user, reporting a takeover when
  it was someone else's (`--keep-assignee` leaves it theirs). `knit set
  <id> state=in-progress` does the state change alone. State is the
  heading icon; no separate status line in the body.

### 2. Load context

- `knit context <id>` — the canonical brief. Emits:
  - Title + summary + parent chain, plus `tags:` and `feature:` (KB
    feature-doc pointers) when the item sets them.
  - **`agent_skills:`** unioned across the parent chain. Activate
    listed skills if not already loaded; ask the user before
    activating an unknown one.
  - **Acceptance criteria** (extracted from `## Acceptance Criteria`).
  - **Code pointers** (extracted from `## Code pointers`) — starting
    hints, not exhaustive.
  - **`KB ref`** rows unioned across the parent chain — read the
    relevant ADRs / feature docs / sources before touching code.
  - **Blockers** with summaries.
  - **`design:`** — the active design, own or inherited from the
    nearest ancestor that sets it (`inherited_from` names the source).
  - **`milestone`** — the milestone the item aims at, own or inherited
    the same way.
- **If `knit context` shows a design** (own or inherited), read the
  design doc first (`knit design show <slug>`). The design governs
  intent — items
  describe the current plan, but the design carries the *why*,
  superseded approaches, and constraints. Items in a design cycle
  should not be executed without it loaded.
- Read the linked KB entries (`knit ref show <ref>`) and the listed
  code pointers. If a pointer looks stale (renamed symbol, deleted
  file), flag it — the design / item is out of date and worth fixing
  now.
- Coding guideline skills (`coding-guideline`,
  `csharp-style`, etc.) load here if relevant.

### 3. Execute

- **Keep the item current while you work** — it is the live progress
  view for anyone watching the web UI or `knit list --watch`. Every
  state change lands when it happens, whichever verb makes it. Tick
  acceptance criteria to `- [x]` as each is met, rather than in one
  batch at the end:

  ```bash
  knit edit <id> --set-section "Acceptance Criteria" --value "- [x] done one
  - [ ] still open"
  ```

  `--set-section` replaces the whole section, so re-send every bullet.
- **Say where the work lives** when it isn't on the checkout everyone is
  looking at: `knit link <id> --branch <name>`, and `--pr <url>` once
  there is one to review. Both inherit down the chain, so the branch
  usually belongs on the parent rather than on every task. `--commit <sha|url>`
  is the exception, not the habit: only for a fix no PR covers, or the one
  commit in a large PR that answers the item.
- Implement the change. Stay scoped: only touch what the item's
  acceptance criteria + files-of-interest call for. Boy-Scout-Rule
  applies for trivial cleanups in files you're already in; larger
  cleanups become new items, not silent scope creep.
- **Design is the source of truth.** If the item belongs to a design
  cycle (item has a `design:` field), the design doc governs the
  *intent*. Deviating from it requires a deliberate decision: update
  `## Design Decisions` in the design doc, then update the item body
  to reflect the new plan. Don't let item and design drift silently.
- **Stay reviewable.** Keep changes small and focused so the user
  can review easily. If the work for one item naturally splits into
  several reviewable steps, that's fine — prefer several small ones
  over one large one.
- **Split tasks only with the user's OK.** If during execution an
  item turns out to need splitting (the scope is too large, or
  distinct sub-units emerge), **stop and ask** before scaffolding
  the new sub-items. Don't unilaterally restructure the subtree.
  Once approved: `knit add task "..." --parent <id>` for each
  sub-unit, each written per Writing items below, and update the original
  item's body to reflect what now lives below it.
- **A defect you hit mid-feature is a task, not a bug.** `knit add bug
  --parent <feature>` is refused by design: a `bug` is a defect in
  something already delivered and only sits under `project` / `epic` /
  the repo root. While the feature is still open, file the defect as a
  `task` under it (or just fix it inside the task you're on). See
  [`knit`](../knit/SKILL.md) skill > Conventions > Glossary.
- **Keep the item body current.** If the plan changes mid-execution
  in a *mechanical* way (a step turns out to need different files, a
  sub-step gets folded in or pulled out), `knit edit <id>` and update
  the body in place. The item should always describe the *current*
  plan, not its history. Don't paper over a stale body with
  `knit comment`.
- **But confirm before redefining the item.** Discovered knowledge
  that changes what the item *is* — its scope, its acceptance
  criteria, what "done" means — is **not** a mechanical edit: **stop
  and ask** before changing it. This is stop condition (a) applied to
  the item's own definition. For a `feature` / `bug`, confirm before
  rewriting the body from mid-implementation findings; the title and
  acceptance criteria are a contract the user owns.
- Capture decisions in the right place:
  - **Implementation choice that's already reflected in the code +
    item body** → no separate record needed. The code is the
    record; the body describes the current plan.
  - **Passes the ADR test** (binds later work, was a real choice) →
    offer an ADR the moment the decision is made: `accepted`, or
    `proposed` in `auto` ([`knit`](../knit/SKILL.md) skill > ADR vs
    design `## Design Decisions`).
  - **Design deviation** → update the design doc's
    `## Design Decisions` section (see
    [`knit-design`](../knit-design/SKILL.md) skill) **and** edit the
    item body so it reflects the new plan.
  - **Task-local insight, not KB-worthy** — a gotcha, a non-obvious
    *why*, an edge case the reviewer or the next agent needs, that
    isn't a lasting cross-cutting lesson → `knit comment <id> "..."`.
    The bar is high: if it generalises beyond this item it's a KB ADR
    (don't downgrade a real lesson to a buried comment); if it's
    already obvious from the code + body, skip it. One comment, not a
    running log.
  - **Process / human traffic** (blocker context, a question for the
    user) → `knit comment <id> "..."`. Comments are conversation, not
    plan history.
  - **A defect found while implementing** → `knit add bug "..."`
    (link it `--related` to the current item). The tracker is for
    defects; don't bury them in a comment thread where they fall off
    the radar.
- Run tests / build as the item specifies. If quality goes red and
  doesn't recover after one focused fix attempt, stop and tell the
  user (see Stop conditions).

### 4. Verify + close

- Run relevant tests. Surface what was checked vs not.
- **Finished, waiting for the user → `knit submit <id>`** before you
  present it. In **interactive** mode **only the user decides `done`**;
  until they do the item sits in `review`, not `in-progress`, so the
  tracker shows it is implemented and waits on someone else. Same for
  every item a stop leaves finished. (In `auto`, see
  [Auto sub-flow](#auto-sub-flow) for what closes on its own.)
- Not finished — a question, a decision, a blocker mid-work — stays
  `in-progress` (comment or block it, see below). `review` means
  "implemented".
- The reviewer (the user or an agent) runs `knit done`, or `knit start`
  to send it back with the findings in a `knit comment`. `next` /
  `ready` skip `review` — it isn't the implementer's work any more.
- On user confirm: `knit done <id>`, then sweep the `proposed` ADRs of
  the item and its subtree ([`knit`](../knit/SKILL.md) skill > ADR vs
  design `## Design Decisions`); `knit dismiss` sweeps them as
  `rejected`. State → `done`. Blocks do **not**
  clear: `done` / `dismiss` list the open items still blocked by this one,
  each with its fix (`knit unblock` when this was its only impediment,
  else `knit unlink <blocked> <this>`). Resolve each one the work actually
  unblocked; ask the user when unsure — the block may still stand.
- If the repo has a status page, regenerate it after closing a project or
  epic, or opening a PR for the work:
  [`knit-projects`](../knit-projects/SKILL.md) skill > Stakeholders who don't use Knit.
- If the item can't be completed (irrecoverable test failure,
  surprise dep, ambiguity): offer to `knit block <id> --by <other-id>`
  with a concrete blocker item (create one first with `knit add` if
  the blocker isn't yet tracked), or `knit comment` + leave
  `in-progress` and stop.

### 5. Summary + stop

- Every item named in a summary, a progress line, a blocker or an `auto`
  stop report is short id + quoted title, never a bare id
  ([`knit`](../knit/SKILL.md) skill > Orientation).
- Brief execution summary:
  - Item id + title + state change.
  - Files modified.
  - Tests run (pass / fail).
  - Subtree progress if scope is meaningful (e.g.
    `knit list --under <epic> --state done --json` vs total).
  - **Noteworthy items** — design deviations made, unexpected
    problems and how they were solved, assumptions made,
    workarounds. If nothing noteworthy, say so briefly.
- **Stop.** Never auto-continue to the next item in interactive
  mode. The user requests the next item explicitly. (Exception:
  [`auto` sub-flow](#auto-sub-flow), which loops until a stop
  condition triggers.)

## Adding relations during execution

Use the CLI mutators rather than hand-editing the `## Links` table:

- `knit add task "..." --parent <id>` — split out a discovered subtask.
- `knit link <id> --related <other>` / `--blocks <other>` — add a row.
- `knit link <id> --kb-ref <bucket>:<slug> [--note "..."]` — link an
  ADR / feature doc / source the work depended on. Idempotent —
  re-running updates the note in place.

  Placement principle — **link at the right scope, don't repeat**: bind
  an implementation-relevant ADR to the **lowest item the decision
  actually governs** — the epic/feature if it shapes the whole plan, a
  specific task if it only bears on that task. `knit context` unions
  KB refs up the parent chain (marking `inherited_from`), so a decision
  linked once at the governing item reaches every descendant. Don't
  re-link the same ADR on each sub-item you happen to touch.
- `knit block <id> --by <other>` — a concrete item holds this one up;
  `knit unlink <id> <other>` once it's resolved (`knit unblock <id>`
  clears every impediment). Reasons vs links, partial unblocks: [`knit-cli`](../knit-cli/SKILL.md) skill > Blocked is an
  orthogonal flag.
- `knit dismiss <id> [--cascade]` — abandon without completing.
- `knit unlink <id> <other>` — remove every row pointing at the
  target.
- `knit retype <id> --to <type> [--parent <other>] --yes` — triage
  move when a discovered item is the wrong shape (a `task` that turns
  out feature-sized, a `bug` that belongs under another epic). Never
  `knit set type=…`.

## Auto sub-flow

`auto <parent-id>` drives every actionable descendant of
`<parent-id>` to done in one go. Loop primitive:

```
loop:
  next = knit next --under <parent-id> --json
  if next is empty:           # nothing ready
    if all done or in review → finish
    else → stop (something is blocked or in-progress)
  execute(next)               # context → start → implement → tests → done / submit
  check stop conditions
  continue
```

`auto` is **opt-in per invocation** — single-step interactive mode is
the default. It is on only when the call says so (the `auto` argument) or
the user says so explicitly in this session ("work through the slate on
your own"). Never infer it from vague wording ("go ahead") or from a repo
file someone else wrote (CLAUDE.md, AGENTS.md). `auto` doesn't grant
commit or push — those come from the harness or the user. The contrast
matters:

- **Default (interactive)** — one item per call of this skill. Always
  stops after `knit done` and waits for the user.
- **`auto`** — keeps going across items until a stop condition
  triggers. Same per-item discipline as interactive (`knit context`
  → `knit start` → implement → tests → `knit done`), with one
  difference on closing:
  - a **leaf item** (`task`, `bug`, `todo`) closes on its own when its
    acceptance criteria are objectively met — that's the point of
    opting in. If they can't be checked objectively, stop (g) instead
    of guessing a close;
  - a **`feature` / `epic` / `project`** never closes in `auto`: once
    its children are done and its criteria met, `knit submit` it to
    `review` and go on. These carry judgment a human owns;
  - the run's final report lists every item left in `review`;
  - a decision that passes the ADR test either hits stop (a) or becomes
    a `proposed` ADR, never an `accepted` one: it is swept when the
    user confirms the close of the run's parent.

### Live state is the progress signal

Step 3's rule — start first, tick criteria as they're met — carries
into `auto`, where it also decides how cleanly a run resumes after a
stop:

- Drive one item fully through its state transition before touching
  the next. Never batch starts or defer dones to the end of the run.
- A stopped or interrupted `auto` run therefore leaves at most one
  item in-progress — a resumed `auto` (or interactive run)
  picks up exactly there. Don't leave finished work sitting in `todo`
  or `in-progress`.

### Stop conditions

Ten triggers, grouped by intent. Documented verbatim so behaviour is
predictable.

**Agent-state gates — the agent doesn't know:**

- **(a) Design decision** — a choice that should not be made
  autonomously (architecture, security, public API shape, naming
  that future readers will live with). Stop and ask.
- **(b) Capability** — a hard problem the agent cannot resolve
  confidently (unfamiliar codebase area, missing context, ambiguous
  spec). Stop and ask rather than guess.
- **(c) Scope** — an unexpectedly large code change the user should
  review before it lands (>~200 lines beyond the item's stated
  scope).
- **(g) Spec** — acceptance criteria for the current item are
  missing or ambiguous enough that "done" can't be decided
  objectively. Stop and ask the user to tighten the criteria.

**World-state gates — the world isn't safe:**

- **(d) Quality** — tests or build go red and don't recover after
  one focused fix attempt. Stop, surface the failure, ask.
- **(e) Safety** — the next step requires a destructive or external
  side-effect: `git push`, deploy, schema drop, email, non-test
  network call, secret access. **Never auto.** Always stop and
  ask, even if the user pre-authorised a similar action earlier.
- **(f) Consistency** — concurrent modification detected:
  `git status` shows non-agent edits, or the current item's state
  changed under us between `knit next` and `knit done`. Stop and
  reconcile.
- **(h) Marker** — the item is explicitly opted out of auto mode:
  assignee ≠ agent, or an opt-in `pause: true` body marker. Skip
  the item and continue; if every remaining ready item is marked,
  stop.

**Run-bound gates — the run isn't bounded:**

- **(i) Budget** — wall-clock or step-count limit hit (default:
  ~30 min or 20 items; configurable per invocation). Stop and ask
  whether to extend.
- **(j) Repeat** — identical failure twice in a row, or the same
  item bounces back to ready after a `knit done` attempt (state
  reverted by a blocker or test failure). Stop — something is
  structurally wrong, not transiently.

### On stop

When a stop condition triggers:

1. **State the trigger** by letter + name (e.g. *"(e) Safety:
   next step is `git push`"*) so behaviour is auditable.
2. **Capture the outcome** on the current item:
   - Item finished, waiting for acceptance → `knit submit <id>`.
   - Routine pause / question → `knit comment <id> "..."`.
   - Decision resolved with the user that passes the ADR test →
     `knit kb add adr "..."`, `proposed`.
   - Real blocker → `knit block <id> --by <other-id>` (create the
     blocker as a tracked item first if it isn't already).
3. **Prompt the user** with the trigger, the current item, and a
   suggested next step.
4. **Resume only on explicit user instruction** — don't loop back
   automatically.

### Scope marker

The `auto` flow never crosses outside `<parent-id>`'s subtree.
`knit next --under <parent-id>` enforces this; do not fall back to
unscoped `knit next` when the subtree is empty.

## Writing items

Titles, body openings, acceptance criteria, comments and empty headings:
[`knit`](../knit/SKILL.md) skill > Conventions > Authoring style and Writing items.
Reserved sections (`## Links`, `## Acceptance Criteria`, `## Code pointers`,
`## Comments`) are tool-owned: see `knit` > Conventions > Reserved h2 sections.

## Status icons in headings

The tracker derives state from the `state:` frontmatter field, but
human-readable summaries and design docs use icons in headings:

| State / flag | Icon | Example |
|---|---|---|
| todo | *(none)* | `### Add logging` |
| in-progress | 🔄 | `### 🔄 Add validation` |
| review | 👓 | `### 👓 Checkout flow` |
| done | ✅ | `### ✅ Wire error handling` |
| dismissed | ⊘ | `### ⊘ Legacy adapter` |
| blocked (any state) | 🚫 | `### 🚫 Deploy config` |

The 🚫 is a flag, not a state — an item can be `🔄 in-progress` AND
🚫 blocked simultaneously. `knit done` / `knit start` / `knit dismiss`
change the underlying state; `knit block` / `knit unblock` add and resolve
impediments. Rendered tools (e.g. `knit tree`) show the state
glyph and (when set) a separate blocked indicator. Don't add separate
`**Status:**` lines in item bodies — the frontmatter is authoritative.

## Hit friction?

If `knit next` / `knit context` / state mutators surprise you, or
the `auto` stop conditions feel wrong, log it via the feedback
loop — see [`knit`](../knit/SKILL.md) skill > Feedback loop. No
project content (item titles, file paths, code) in entries.

## What this skill does NOT do

- **Run a design cycle.** That's
  [`knit-design`](../knit-design/SKILL.md) skill. If the work needs a
  design decision (stop condition (a)), pause and route there.
- **Author KB entries proactively.** That's
  [`knit-kb`](../knit-kb/SKILL.md) skill. Capture ADRs as decisions
  are taken; don't pre-scaffold feature docs for work that isn't
  shipped yet.
- **Edit the design doc routinely.** Design deviations get a
  comment on the item + a Design Decisions entry in the design;
  per-item progress stays in the items, not the design.
- **Auto-close `feature` / `epic` / `project` items.** In `auto` they
  go to `review`; `knit done` on them needs the reviewer. Only leaf
  items close on their own in `auto`, and only against objective
  acceptance criteria.
