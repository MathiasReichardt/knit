# Project-management simulation driver

("PM" = *project management* — the whole team's work, PM and dev alike, not a
project-manager role in isolation.)

Reusable brief for an **autonomous, AI-driven dogfooding run**: a subagent
role-plays a small software team — a PM *and* the devs doing the actual
engineering — running a real project through Knit's CLI, then records where the
tool, its output files, or its skills fell short.

Both sides of the work must be exercised, weighted roughly evenly:

- **PM side** — planning, structuring, scheduling, re-planning, reporting.
- **Dev side** — the day-to-day of building: picking up a task, working it,
  discovering it's bigger than thought, filing bugs mid-flight, splitting or
  spawning follow-up work, recording progress, finishing. The item tree should
  read like a real team *did the work*, not just planned it.

This file is the *input*; each run produces `simulation-findings.md` as the
*output*. The two pair by name.

> **Two roles, on purpose.** The **driver** (the simulating subagent) runs under
> consumer-only conditions and cannot judge on-disk validity. The **verifier**
> (the operator who launched the run, with full repo insight) is the real
> validity oracle. This brief is written for the driver; the verifier's checks
> are listed at the end so the driver knows what evidence to leave behind.

## Invocation inputs

Passed in at launch — never hard-code them here:

- `RUN_DIR` — an empty working folder for this run.
- `KNIT` — path to a freshly built `knit.exe`, placed in `RUN_DIR`.

All commands below use `$KNIT` from inside `RUN_DIR`.

## Consumer-only conditions (hard constraints for the driver)

The whole point is to experience Knit as a *real consumer agent* would, so that
missing or unclear skill instructions surface as genuine confusion instead of
being silently worked around.

- **Allowed knowledge sources:** the installed skills (run
  `$KNIT skill install --target project` so they land in
  `RUN_DIR/.claude/skills`, then read those), `$KNIT <cmd> --help`, and whatever
  the CLI prints. That is the entire world.
- **Forbidden:** reading Knit's source, `design/`, tests, or this repo in any
  form. If you catch yourself wanting to open the code to understand expected
  behavior — **stop and log it as a finding** (the skill should have told you).
- **Run-local binary only — no global install.** Always invoke Knit through the
  absolute `$KNIT` path in `RUN_DIR`; **never a bare `knit`** — that can resolve
  a different, globally-installed binary on `PATH`. At the start, check what
  `knit` resolves to (`which knit` / `Get-Command knit`) and note it; if an
  ambient install exists, keep going but only ever call `$KNIT`.
- **Run-local skills only — no ambient knowledge.** The skills you may use are
  the ones `$KNIT skill install --target project` writes under `RUN_DIR`, read
  from there. **Never `--target global`** (that writes to `~/.claude/skills`,
  outside the sim). Do not consult any global/home skill location, and **do not
  lean on knit knowledge you already hold in context.** If you "just know" how a
  command works without having read it from the run-local skills or `--help`,
  treat that as suspect and re-derive it — pre-loaded skill knowledge is the
  exact leak this closes, because it papers over the missing-skill findings the
  run exists to catch.
- **Containment — everything stays under `RUN_DIR`.** The run must not create,
  modify, or delete anything outside `RUN_DIR` (no home dir, no global config,
  no other repo). Every command runs with `RUN_DIR` as the working directory and
  targets only paths beneath it. If a command's default or an option *would*
  write outside (e.g. `skill install --target global`, a global config write),
  **do not run it that way — log it as a finding** (a tool that escapes its
  working repo by default is worth knowing about).
- If you start `knit web` for the doctor view, bind loopback only and **kill it
  before finishing** — never leave a process or port held after the run.
- Do not fix, patch, or route around a problem. Hit it, record it, keep going.

## Reproducibility model

This brief is deliberately **not** a command script. It fixes a sequence of
**user intents** — what a PM or dev *wants*, phrased the way they'd say it — and
leaves the agent to work out *which CLI command(s)* satisfy each one, using only
the skills + `--help`. That split is the whole design:

- **Fixed** (so runs are comparable): the theme below, the intent sequence and
  its order, the concrete nouns (project / item names, people), the
  checkpoint/stop points, and the edge-case attempts.
- **Free** (so discovery stays honest): how each intent maps to commands.

The **intent→command translation is where findings come from.** If an intent is
awkward, needs an undocumented flag, or has no command at all, that is a
missing-skill / gap / confusing finding. **Never skip an intent or route around
it — attempt it, record what happened, and move on.** Keep the nouns exactly as
written so two runs produce diffable trees. Between checkpoints, proceed
autonomously; only halt at a `⏸`.

## Coverage — every area must be touched

A run is a **general Knit exercise**, not just the M1-polish additions. The
script below is built to hit all of these. If you reach the end and one is still
untouched, that itself is a finding — the script or the tool has a hole.

- **Item lifecycle** — add, start, done, reopen, block, unblock, dismiss, delete.
- **Hierarchy** — epic → feature/bug → task, split/spawn children, re-parent.
- **Editing** — assign, edit bodies/sections, retype, reorder/rank.
- **Read & find** — show, list/filter, tree, context, search, ready/next/pick.
- **Knowledge base** — decision/ADR, log, idea, source; browse it.
- **Design docs** — author (incl. a mermaid diagram), edit, attach to an item,
  reference.
- **Integrity** — verify, relink.
- **M1-polish** — projects, milestones, slates; due, priority, tags, effort;
  todos; timeline/roadmap.

Intents below are phrased the way a user would say them, with **no command
hints** — mapping each to the right verb (or discovering there isn't one) is the
point. The list above is the verifier's map, not a per-step cheat sheet.

## The project (fixed theme — don't invent your own)

Several intents need free prose: a design note, a feature description, an ADR
rationale, a progress log, an idea, a source. Write them from **this** story
every time — an invented one makes two runs' bodies incomparable and burns
attention that belongs on the tool.

**Pigeon Post Ltd.** — a parcel courier that still delivers by carrier pigeon
and has never had a customer login. Orders arrive by phone, get chalked on a
board, and a bird is dispatched. The **Website Relaunch** project is their first
site where customers can hold an account and track their bird in flight.
Management would like this before the spring migration, which is a real
deadline in a way software deadlines usually aren't.

- **alice** — frontend, joined from a company with actual users, mildly appalled.
- **bob** — backend, wrote the chalkboard-replacement in a weekend, defends it.
- **carol** — infra, sole owner of the one server, which lives in the loft.

Keep it dry and short — light comedy in the *content*, never in the item names
or CLI usage. The nouns in the script below are fixed; use them verbatim.

## Use-case script

Work top to bottom, using **only public CLI verbs**. Each numbered line is a
user intent — satisfy it, don't transcribe it. `⏸ CHECKPOINT` = snapshot
`.knit/`, run `knit verify`, then halt for external verification before
continuing. The query intents (phases 2–3) intentionally probe *discoverability*
— relationship/semantic look-ups that may have no command; "can I even do this?"
moments are prime findings.

**Two tiers.** Steps marked **●** are the **smoke subset** — a minimal,
dependency-clean end-to-end core run (init → hierarchy → assign → lifecycle →
read → integrity → wrap). Everything else is the **full sweep**. A smoke run
does the `●` steps top to bottom, skips the `⏸` halts, and takes one snapshot +
`knit verify` at the end; a full run does all 50 with the checkpoints. Smoke is
for routine "did anything regress" reruns; full is the real dogfood.

### Phase 1 — PM sets up the project
1. **●** Initialize a new Knit workspace here. No global install of skills or knit!
2. **●** Create a new project called **Website Relaunch**.
3. Add two milestones to it: **Beta** (due end of next month) and **GA** (two
   months out).
4. Create a slate **Sprint 1** and make it the current one.
5. **●** Under the project, add an epic **Authentication**, tagged `security`.
6. **●** Under Authentication, add features **Login** and **Password reset**;
   give each a rough effort estimate.
7. **●** Add a bug **Session token appears in logs** and a task **Set up CI**.
8. Write a short design note for how login should work, with a mermaid
   flowchart of the login flow, and attach it to the Login feature.
9. Record the decision that sessions will use JWTs, with a line on why.
10. **●** Assign Login to *alice*, Password reset to *bob*, Set up CI to *carol*.
11. Login is due in a week and is high priority; the token-leak bug is critical;
    Set up CI is already overdue.
12. Give the Login feature a proper written description.
13. Put Login and Set up CI in Sprint 1, and mark them for the Beta milestone.
14. Rank Login above Password reset.

⏸ **CHECKPOINT A**

### Phase 2 — devs build
15. As alice, what should I pick up next?
16. **●** Start work on Login.
17. Which feature or design doc specifies how login should behave?
18. **●** Give me the full context for the Login feature.
19. Login's turning out bigger than expected — split **OAuth callback** out as
    its own task under it.
20. Add the OAuth callback step to the login-flow diagram in the design note.
21. Add a follow-up reminder: **write login integration tests**.
22. **●** Found a bug: login crashes on an empty password. File it and block
    Login until it's fixed.
23. Log a quick note about today's progress.
24. **●** Fix that bug, close it, and unblock Login.
25. Save a link to the OAuth spec we're following as a reference.
26. Jot an idea for later: passwordless login.
27. Note progress on Login: happy path works, edge cases pending.
28. Turn the "write login integration tests" reminder into a real task.
29. Hand Password reset over from bob to alice.
30. Set up CI is finished.

⏸ **CHECKPOINT B**

### Phase 3 — query, report, re-plan
31. Show everything in Sprint 1 and where each item stands.
32. What's overdue or due this week?
33. What's blocked right now?
34. Search everything for "token".
35. Find the work related to authentication.
36. Which feature does OAuth callback sit under?
37. Browse the knowledge base — decisions, logs, ideas.
38. Move whatever's unfinished in Sprint 1 into a new slate **Sprint 2**.
39. Re-parent OAuth callback under the Login *feature* instead of the Login task.
40. We closed Login too early — reopen it, then close it again.
41. Beta milestone is reached — mark it.
42. Show me the roadmap / timeline.

⏸ **CHECKPOINT C**

### Phase 4 — edge cases, integrity & close-out
43. Add a task **Legacy data migration**, then abandon it.
44. Add a throwaway task **Delete me**, then delete it outright — confirm delete
    removes it where dismiss only closes it.
45. **●** Run an integrity check and repair anything it flags.
46. Someone hand-edited an item file so its `# heading` no longer matches its
    title — find it and fix it.
47. Try to give the Website Relaunch project a parent. *(expect a clear refusal)*
48. Try to add a loose todo with no parent. *(expect a clear refusal)*
49. Try re-parenting a task with `set <id> parent=<id>`. *(expect a refusal that
    points at the right command)*
50. **●** Wrap up: close out all finished Login work and report overall project
    status.

⏸ **FINAL CHECKPOINT**

## Tier-0 self-check (driver's own validity signal)

Knit ships its own validity oracle — use it as *one* signal, not the last word:

- Run `$KNIT verify` (and open the `web` doctor view) at a few checkpoints and
  at the end. Treat any finding as a lead.
- Note explicitly when `verify` reports **clean** but a view still looks wrong —
  that mismatch is a high-value finding (a hole in `verify` itself), and only
  the verifier can confirm it, so leave the evidence.

## Evidence the driver must leave in `RUN_DIR`

The verifier works offline from these artifacts — do not make them re-run the sim:

- `command-log.md` — chronological log: each command, its exit code, and a brief
  note of the output / what you expected vs. saw.
- `snapshot-a/`, `snapshot-b/`, `snapshot-c/`, `snapshot-final/` — a copy of the
  entire `.knit/` tree taken at each `⏸` checkpoint (A/B/C) and at the end.
- `simulation-findings.md` — the findings (format below).

## `simulation-findings.md` format

Brief. **Bullet points only**, no prose paragraphs. Group by category, one
section per category (omit empty ones):

- **Missing in skill** — behavior a real agent needed but no skill covered.
- **Gap** — capability the workflow wanted that the CLI doesn't offer.
- **Bug** — command errored, crashed, or corrupted files.
- **Wrong behaviour** — command "succeeded" but did the wrong thing (incl.
  silent no-ops that report success).
- **Confusing / unclear** — worked, but the skill or output misled you first.

Each bullet: what you were doing → what happened → the exact command. Keep it to
one line where possible; a sub-bullet only for a concrete repro detail.

## Verifier checks (run after the driver finishes — not the driver's job)

For reference, so the driver knows why the snapshots matter. The verifier, with
full repo insight, independently validates the produced `.knit/` against ground
truth — because `knit verify` shares the writer's codebase and can bless a
wrong-but-self-consistent format:

- **Structural:** frontmatter parses and matches the serializer/schema (fields,
  enum values, date format); flat-vs-folder layout matches `ItemLayout`;
  `## Links` rows / parent projection / slate·project·milestone wiring are
  well-formed; a re-parse → re-serialize round-trip is stable.
- **Semantic:** planning-layer containment, frozen-on-close lifecycle, and
  hierarchy rules hold — not just "it parses."
- **Reconcile:** confirm/correct each driver finding, drop false positives,
  and add anything the driver couldn't recognize (esp. `verify`-said-clean
  holes).
