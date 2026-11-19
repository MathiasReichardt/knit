# Knit — Overview

Knit is a git-native task tracker for engineering teams. Items are markdown
files in a git repo: either the **current repo** (local mode: one team, no
server) or a **dedicated knit repo behind a hub server** (hub mode, M2:
multi-repo, multi-user, web UI for people without git). The on-disk shape is
the same in both modes. A single-binary CLI (`knit`) drives day-to-day edits
for humans and AI agents, and the same binary serves the local web UI
(`knit web`) and, in M2, the hub. The pitch over Jira / DevOps / OpenProject:
no vendor lock-in, plain-text data that diffs and merges, no feature bloat, and
an interface agents use as fluently as humans do.

This doc holds the product thesis, the standing principles and the storage
shape. It is not the reference for the current surface:

| For | See |
|---|---|
| Item model: types, states, planning layers, glossary | [skills/knit/SKILL.md](../skills/knit/SKILL.md) > Conventions |
| Command surface, flags, gotchas | `knit --help`, [skills/knit-cli/SKILL.md](../skills/knit-cli/SKILL.md) |
| Tech choices, AOT constraints | [techstack.md](techstack.md) |
| What to test where | [test-strategy.md](test-strategy.md) |
| Why it is built this way: decisions, rejected alternatives | [decisions.md](decisions.md) |
| Milestones | [roadmap.md](roadmap.md) |
| Committed, not started | [planned.md](planned.md) |
| Parked ideas and future improvements | [ideas/backlog.md](ideas/backlog.md), [ideas/README.md](ideas/README.md) |

## Goals

- Items as markdown + frontmatter, friendly to humans and agents alike
- Git as the source of truth: no separate database for content
- Two modes, one on-disk format: local (items in the current git repo) and hub
  (items in a central git repo behind a REST API)
- AI agents are first-class users, not an add-on
- Minimal type system: project, epic, feature, bug, task, todo
- Stealth-friendly: items can track work for repos that aren't modified

## Non-goals (v1)

- Sprints / burn-down (slates are a dated set of items, not a velocity model)
- Time tracking (only a free-text `effort`)
- Fine-grained authorization
- Built-in chat
- Replacing PR review

---

## Principles

These hold across every milestone. A change that breaks one needs a decision
recorded first.

- **Git is the source of truth.** Every cache, index or in-memory view can be
  rebuilt from the markdown on disk, so losing one loses nothing.
- **The format is a valid prefix across milestones.** The on-disk shape doesn't
  change from M1 to M3. An admin with shell access to a hub can run the local
  CLI against the hub's checkout and get the same behavior.
- **Local mode never invokes git.** The CLI only touches files. Users stage,
  commit, push and pull `.knit/` like any other file in the repo, and
  conflicts go through a normal git merge.
- **No derived item store.** Every read enumerates the item folders, so nothing
  can go stale after a checkout, a merge or a hand-edit. If enumeration is ever
  measured as the bottleneck, a cache gets a staleness fingerprint designed in
  from the start.
- **Nothing under `.knit/` is silently hidden.** A file Knit can't load or
  address shows up in `knit verify` and in the SPA's doctor view with a path
  and a reason.
- **Adapters only map.** The CLI commands, the local web API and, later, the
  hub API are thin adapters over the use cases in `Knit.Core` / `Knit.Data`.
  Use cases return `OperationResult<T>` and know nothing about exit codes,
  terminal markup or HTTP. Protocol concerns (auth, ETags, commit batching)
  stay in the adapter.
- **Agents go through the CLI.** Skills document the CLI contract, and
  `--json` on reads and mutators is the structured surface, so agents don't
  have to parse ASCII art or prose.

### Error reporting: no silent failures

Every command, whether CLI or REST, reports failures explicitly and
actionably. There are no silent no-ops, no "exit 0 but nothing happened" and
no 500s without a body.

- **CLI:** non-zero exit on every failure, and the message goes to stderr
  (stdout stays capture-clean for `$(knit …)` and pipes). With `--json`, a
  structured error that carries a stable code.
- **HTTP APIs:** RFC 9457 Problem Details (`application/problem+json`) for
  every 4xx/5xx. Domain fields (e.g. `current_state` on a 412) go in as
  top-level extensions.
- **SPA:** show the problem as a toast or an inline panel, never swallow it.

What good looks like:

- It names what failed ("Item not found: `4F2K`", not "operation failed").
- It says why ("target item is already done", not "invalid state").
- It suggests a next step where one exists.
- It uses a stable error code (`item_not_found`, `conflict`, `parse_error`, …)
  so scripts and agents can branch on it.

Forbidden:

- Catching an exception and carrying on.
- Writing partial state and returning success.
- Returning an empty result when the query couldn't run. "Ran, zero matches"
  and "couldn't run" must stay distinguishable.
- HTTP 200 with `{"ok": false}`.

Every CLI command has at least one negative-path test asserting the code, the
exit status and the stderr message.

---

## Storage model

```text
.knit/
  config.json            committed repo settings (+ format_version)
  config.local.json      uncommitted per-machine overlay (knit root, mounts)
  items/<id>-<slug>.md
  kb/<bucket>/…          ADRs, docs, features, logs, ideas, sources
  designs/               active + completed design docs
  projects/<slug>/       readme.md, optional roadmap.md, milestones/, free-form files
  templates/<type>.md    written by `knit init`; the binary embeds the defaults
```

- One flat file per item. Frontmatter holds structured fields; the body holds the
  description, free `## sections`, the `## Links` table and `## Comments`.
- Links are a `## Links` table in the body, ids are coordinator-free NanoIDs,
  `format_version` guards the on-disk contract, and the root is found by
  walking up — the reasons are in [decisions.md](decisions.md) > Storage and
  Config & roots.
- The field list lives in the knit skill and in `Item.cs`. It isn't restated
  here.

### Identity

The value stored on items (`assignee`, comment authors) is always a
**handle**. `handle` and `display` come from config — the user-level one, or a
repo's uncommitted `config.local.json`. There is no registry and no git
dependency. The M2 hub adds a registry: [ideas/hub.md](ideas/hub.md).
