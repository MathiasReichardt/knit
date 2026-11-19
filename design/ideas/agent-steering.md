# Agent steering from the web UI — idea

Status: **idea / not scheduled.** Captures the concept so it isn't lost.
Not a design doc; no commitment, no milestone. Revisit after M1c.

## Brief idea

- Drive coding agents (start / watch / approve / review) from the Knit
  web UI, with each run bound to a Knit item (one agent per
  feature / bug / task).
- The SPA becomes a small "mission control": trigger work, see live
  output, approve tool use, get notified on done, review the diff and
  send back per-line/file comments as a todo list or new items.
- **Why Knit specifically:** items are a context-engineering surface —
  a well-authored item (goal, acceptance, linked KB/design, blockers)
  *is* a clean, curated brief. The agent starts from a tight seed
  window instead of burning tokens rediscovering what the item already
  states. See [The item is the brief](#the-item-is-the-brief-why-this-belongs-in-knit).
- This is also what resurrects the deferred plan-execution view
  (m1c Phase 9.1/9.2): with live run-identity, the scoped tree + leaf
  pin show *agent position*, not a re-skin of the `under` filter.

## The four problems (separable)

- **Identity** — which run is on which feature/bug? → key by item id;
  worktree-per-agent; run registry in `.knit/runs/<item_id>.json`
  (stores `agentKind` for reattach after a `knit web` restart).
- **Spawn / lifecycle** — who starts/stops them? → server-spawned for
  full control, or observe-only for agents the user launched (cheap,
  no process mgmt). Ship observe-first if needed.
- **Observe** — what is it doing now? → normalized structured event
  stream over the existing SSE channel → rich cards. Raw terminal
  (xterm.js) only as a fallback.
- **Steer** — change it mid-run → inject a message, approve/deny tool
  use, interrupt, kill.

## Use cases (priority order)

Complete loop: trigger → observe → approve → answer → notify → review →
accept/merge or send-back → (stop anytime).

1. **Trigger** — start an agent implementing a selected
   feature/task/bug from the item view.
2. **Observe** — watch agent output live to see what's happening.
3. **Approve tool use** — route permission prompts to the SPA;
   approve / deny per call. Policy to cut noise: auto-approve reads,
   prompt on writes / bash (allowlist), not per-call for everything.
4. **Answer a question** — distinct from tool approval; agents block on
   clarifying questions ("which approach?"). Reply from the SPA.
5. **Notify on done** — push a notification when the run finishes
   (success / failed / needs-input).
6. **Review + comment** — review repo changes (diff) and attach
   per-line / per-file comments; collect several, then send off as a
   todo list to the running agent **or** add as new Knit item(s).
7. **Accept / merge** — happy path: satisfied → merge the worktree
   branch, mark the item done, clean up the worktree.
8. **Stop / interrupt** — kill switch for a looping or misbehaving run,
   available at any point.

## The item is the brief (why this belongs in Knit)

- The agent's prompt is **assembled from the item**: body + acceptance
  criteria + linked KB / design + blockers. The spec already lives in
  Knit — that's the reason to do this here vs. a generic agent runner.
- Knit's item context is **designed for driving a task** — structured,
  agent-ready by construction (clear goal, acceptance criteria as the
  done-check, KB/design as grounding, blockers as guardrails). It isn't
  scraped or reverse-engineered into a brief; it *is* the brief.
- **Clean seed context, not scraped.** Items pre-curate the relevant
  window (links = hand-done retrieval) → fewer exploratory tokens,
  better grounding, reproducible runs. Caveat: only as good as the
  item's authoring, and it's a *seed* — the agent still explores beyond
  it; what it learns should feed back to enrich the item.
- An agent that drives the `knit` CLI already surfaces its mutations in
  the Phase 8 **activity feed**, attributable by handle — the cheapest
  observe channel, and it already exists.

## Agent preparation / abstraction

- **Assume Claude Code now.** Keep OpenCode / aider / others as a
  *possible* future — design the seam, don't build a framework.
- Single thin seam: `IAgentAdapter` → `IAgentSession`
  (`Events` channel + `SendAsync`). Everything else (identity,
  registry, worktree, SSE, control surface) stays agent-agnostic.
- **Normalized event model** defined by what the SPA needs, not the
  union of all agents:
  `SessionStarted · AssistantMessage · ToolCall · ToolResult ·
  PermissionRequest · TaskProgress · TokenUsage · Error · Finished ·
  RawChunk`. `RawChunk` feeds the terminal fallback.
- **Caveat — "raw terminal output" is a design choice, not a freebie.**
  Claude Code's `--output-format stream-json` yields the *structured*
  events cleanly but **not** a real TTY stream, so a `RawChunk` built
  from it is reconstructed text — no colors, spinners, or cursor
  control. A genuine xterm.js experience needs a **PTY-backed spawn**
  (e.g. pseudoconsole / ConPTY on Windows), which is a heavier, more
  RCE-sensitive spawn path than a plain piped subprocess. **Decide this
  willingly per adapter**, up front: pick reconstructed-text-in-a-log
  vs. true-PTY before designing the spawn — it drives the spawn shape,
  the security boundary, and the `StructuredEvents`-only-vs-`RawChunk`
  capability flags. Don't let the default fall out of whichever is
  easiest to wire.
- **Normalized commands**: `SendMessage · AnswerPermission ·
  Interrupt · Stop`.
- **Capability flags** per adapter
  (`StructuredEvents · InjectMessage · RoutePermissions · Interrupt ·
  Resume`) — the SPA disables controls an agent can't do; degrades
  cleanly to "terminal + kill" for unstructured agents.
- Seam validated by two *different* integration shapes: Claude Code =
  subprocess + `--output-format stream-json`; OpenCode = HTTP/JSON API
  client (no child process). If both satisfy `IAgentSession`, the
  seam is at the right altitude.
- **Discipline:** one concrete adapter (Claude Code) first. No
  plugin discovery / config-driven registry / agent-SDK abstraction
  until a *second* adapter actually arrives.

## Outlook

- **Git worktree per agent** — `git worktree add worktrees/<short-id>
  -b feat/<slug>-<short-id>`. Isolation (agents don't trample each
  other), and `git worktree list` alone maps worktree → item. Review
  diffs the agent's branch.
- **One vs. multiple agents** — registry + worktrees make N concurrent
  runs natural; SPA lists active runs, each pinned to its item. Start
  with one, the model already scales. Worktrees isolate *during* work,
  but merge-back conflicts remain; the blocker graph could sequence
  dependent runs (one finishing unblocks the next).
- **Remote agents** — local `knit web` today (localhost-only; spawning
  + stdin piping is an RCE surface by design). Toward M3 (hosted
  server) agents could run remotely; the adapter seam already hides
  local-subprocess vs. remote-API, but the security boundary must be
  gated hard before any non-local exposure.

## Token usage in the plan / run view

- Surface **token usage + remaining budget** per run, fed by the
  `TokenUsage` normalized event.
- Show in the run/plan view: cumulative in/out tokens, and remaining
  against a per-run cap if one is set.
- Cheap signal for "is this agent stuck burning tokens?" and for
  deciding when to interrupt or re-steer.
