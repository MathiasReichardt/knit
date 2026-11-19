# Backlog — future improvements

Shorter than an [ideas](README.md) entry: things someone wanted, with the one
reason each is still waiting. Nothing here is committed.

## CLI

- **Bulk state verbs** (`knit done <id> <id> …`). The atomic verbs already
  compose — `list --json | jq -r '.[].id' | xargs -n1 knit done` — and a bulk
  form owes a partial-failure contract (exit code, ordering, all-or-nothing).
  Same call as [Bulk re-slate](README.md#bulk-re-slate--wont-do).
- **`@last` alias** for the item just created. Needs per-shell state (where
  does the marker live, what about concurrent shells?); the surprise surface
  outweighs the keystrokes.
- **Shell completion** via a `knit completions <shell>` subcommand printing an
  `eval`-able snippet (the `gh` / `kubectl` model). Not `dotnet-suggest`: a
  global .NET tool contradicts the single-binary story. Blocked while
  System.CommandLine returns nothing for sub-command context (`knit add <TAB>`)
  — empty completion reads as "takes no arguments", which misleads.
- **`knit summary <id>`** — terse rollup of state, blockers, recent activity and
  child progress, so agents don't read every file on long-running work.
  Compaction of long-closed items (summarize and prune comments) is the bigger
  sibling; it collides with frozen-on-close.

## Items and data

- **Attachments** (screenshots, logs). Today the only mechanism is a link.
  Binaries in git inflate clones and are a one-way trip; options are git-LFS
  with a size policy, an external blob store with a reference field, or
  links-only by rule. Don't add without solving growth. Until then the user
  places the file and links it; no `knit attach` (a `cp` plus a link, and it
  would answer the growth question by default).
- **Threaded comments** (`reply_to`).
- **Saved queries / personal views.**
- **Structured `effort`** — a timeline bar wants a length; free text can't give
  one. Touches the item model, so its own decision.
- **Item cache file for the CLI.** The server cache shipped; the CLI's file
  cache is parked (measurements and design in
  [decisions.md](../decisions.md) > Storage): ~0.1 s saved per call at 1k
  items, opening the files is the cost.
  Holds back: "nothing goes stale" is an invariant, and the gain doesn't pay
  for a cache file's lifecycle yet. Revisit past ~5k items in a real repo or
  on reported agent latency.

## Git and integrations

- **Derive state from git topology** (branch / PR status) and attribute time
  from commit activity. Needs the git wrapper from
  [spa-git-integration.md](spa-git-integration.md) first; local mode's
  "Knit never invokes git" principle bounds how far it goes.
- **CI integration** — `Closes <id>` in commits, PR merge → done.
- **Jira / DevOps import** beyond the one-shot [CSV migration](csv-backlog-migration.md).

## Web UI

- **Agent control plane and review loop** — see
  [agent-steering.md](agent-steering.md) (M1f). Extras not captured there:
  "Implement with AI" / "Create review" buttons per item by state; a diff viewer
  over the working tree with comment-on-line routed to an agent; AI review
  results saved to a `reviews` KB bucket; commit-message generation from the
  diff or the item subtree.
- **Link-graph explorer** (low priority).
- **Virtualized tree and list.** Every expanded or filtered row is a real DOM
  row, the likely first limit at a few thousand items (unmeasured). Waits for
  a repo that size; costs: variable row heights, the tree's expand/collapse,
  keyboard nav, scroll-to-item and Ctrl+F over rows that aren't rendered.
- **Fold closed work in the state-sorted tree** — `N done ▸` / `N dismissed ▸`
  summary rows per parent. Cut from wave 2 Phase 27: a new row kind with its
  own drag-and-drop rules, focus handling and an off switch, while the `open`
  filter and parent collapse already hide closed work. Waits for long done
  lists to actually hurt.

## Distribution

- **Tauri desktop wrapper** — the embedded SPA + web API in a native window
  with tray and OS install, no terminal needed to launch `knit web`.
