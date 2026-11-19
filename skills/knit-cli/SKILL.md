---
name: knit-cli
description: CLI reference for Knit — the things `knit --help` doesn't tell you. Gotchas, idioms, cross-command behaviour, and the full command index; per-area detail (config, mounts, root, filters, fields, slates, status, timeline, projects, KB, verify) in reference files read on demand. The workflow skills (`knit-kb`, `knit-design`, `knit-work`, `knit-projects`) describe *what to do*; this skill describes *how the CLI itself behaves*.
---

# knit-cli

`knit --help` and `knit <cmd> --help` are the source of truth for
commands, flags, and arguments. **Run `--help` before guessing.** This
skill exists for the layer above: idioms, surprises, and cross-cutting
behaviour that `--help` doesn't surface.

This file holds the gotchas every command session needs. Per-area detail
lives in `references/`, linked from the index group it belongs to — open
one when the task touches that area.

For conventions (refs, link tables, reserved sections) see [`knit`](../knit/SKILL.md) skill > Conventions. For workflows,
see the `knit-kb`, `knit-design`, `knit-work` and `knit-projects` skills.

## Command index

Group → commands. Run `knit <cmd> --help` for flags.

- **Setup** — `init`, `skill install`, `skill update`.
  - `init` needs a handle + display name: an agent passes `--handle` /
    `--display` (`--non-interactive` makes a missing one an error, not a
    prompt) — unless the user's global config carries them; the repo then
    inherits them; a passed one lands in `config.local.json`, never the
    committed `config.json` — don't write one into it. `init --external <path> --hint "…"` is for a repo whose
    tracker is an existing knit repo ([Root](references/root.md)).
  - Two styles: `--style claude` (`.claude/skills`) and `--style agents`
    (`.agents/skills` — Codex, OpenCode), repeatable. Without `--style`, the
    folders are pre-checked from the agents found in the repo and home dir.
  - `--target global` writes to the user's home dir — pass it **only when the
    user explicitly asks**; never install globally on your own initiative.
  - A bare `skill install` (no `--target`) **skips a folder whose global bundle
    exists**: agents discover global skills in every repo, so a per-repo copy
    only pins a version. Prefer the bare form and let it decide; pass
    `--target project` only when a local pin is actually wanted.
  - `init` offers the pre-checked folders that have no bundle yet, and flags a
    stale one; a current bundle produces no output. Without a terminal it writes
    nothing unless `--skills global|project` is passed, so an agent scaffolding
    a repo never installs skills by accident.
  - `init` — also re-run in an initialized repo — owns the `## Knit` section of
    `CLAUDE.md` / `AGENTS.md`: it adds it, or replaces an outdated one up to the
    next heading. Don't hand-edit that section; rerun `knit init` after an upgrade.
  - The repo's existing instruction files get it; a new file only when the repo
    has none (pre-checked from the agents found), or interactively on a separate,
    default-no question. A `CLAUDE.md` that imports `@AGENTS.md` gets it via
    `AGENTS.md`. Without a terminal, `--agents-file project` writes it; without
    the flag, nothing is written.
  - Installs stamp `<skills-root>/.knit-bundle.json` with the producing binary's
    version. A bare `skill update` refreshes every bundle it finds (repo and
    global, both styles), one line each; `--global` / `--style` / `--target`
    narrow it, `--check` reports without writing.
    A bundle newer than the binary is flagged: update `knit`, not the skills —
    an unknown command the skills name usually means the binary is behind.
- **Create / edit items** — `add` (field flags: `--assignee`, `--due`,
  `--priority`, `--effort`, `--tags`, `--slate`, `--design`,
  `--agent-skills`), `set`, `move` (slug rename),
  `reorder` (rank via `--before` / `--after`, or `--by state`), `retype` (change type
  + optional re-parent), `edit`, `comment`. Set-rejections (`parent=`,
  `state=blocked`), `design=` validation, title / H1 sync, `reorder`,
  `reopen`, `retype`: [Items](references/items.md). Due, priority, health,
  tags: [Fields](references/fields.md).
- **Read items** — `show`, `list`, `tree`, `context`, `ref show`, `timeline`
  (deadline agenda + at-risk blockers), `status` (scope rollup: completion,
  per-assignee load, blocked + overdue), `status report` (writes the
  stakeholder page `project-status.md`). `ref show` also takes a `.knit/…/x.md`
  path; `<ref>#<anchor>` prints that section only, `--sections` lists each
  h2/h3 with its section ref, `<ref>#<file>.svg` the outline of the SVG next to
  the doc (desc, nodes, edges, groups, each node's position). [Filters, sort, dump](references/filters.md),
  [Timeline](references/timeline.md), [Status](references/status.md).
- **Share links** — `url <ref>`: the doc's blob URL on the forge's default
  branch (`HEAD`), for someone outside knit. An unknown host exits non-zero
  naming `knit config set forges.<host> <kind>` — ask the user which forge it
  is; never guess. [Config](references/config.md).
- **Find anything** — `search` (items + KB + designs + project docs;
  `--kind` narrows — see the `search` gotcha below)
- **Pick what's next** — `ready`, `next`, `pick`; `--slate <name>` / `--no-slate` keep each inside a slate
- **State** — `start`, `submit` (`in-progress` → `review`), `done`, `block`, `assign` (`handles` lists the
  spellings in use), `dismiss`, `reopen`,
  `delete` (hard-delete; prefer `dismiss` unless the item must be
  removed from disk)
- **Links** — `link` (incl. `--kb-ref`, `--milestone`, `--branch`,
  `--repo`, `--pr`, `--commit`), `unlink`; `kb link` / `kb unlink` for the
  same external rows on a KB entry. Branch / repo / PR / commit rows, KB refs,
  editing a row's note:
  [Links](references/links.md).
- **Knowledge base** — `kb add`, `kb show`, `kb list`, `kb set`
  (frontmatter), `kb edit` (body sections),
  `kb rename`, `kb delete`, `kb split`, `kb supersede`, `kb index`.
  Sub-docs, `kb delete`, KB-to-KB refs: [KB](references/kb.md).
- **Designs** — `design new`, `design show`, `design list`
  (`--completed`), `design edit` (body sections), `design archive`.
  Addressable as `design:<slug>` (see [`knit`](../knit/SKILL.md) skill > Ref
  format); `ref show design:<slug>` dispatches here.
- **Slates** — `slate add`, `slate list` (`--all`), `slate rename`,
  `slate move` (`--before` / `--after`), `slate set`, `slate archive`,
  `slate rm`. Assign via `set <id> slate=<name>`. [Slates](references/slates.md).
- **Projects & doc folders** — `project new` (`--roadmap`),
  `project roadmap`, `project list`, `project delete` (re-roots children,
  not cascade), `project doc rename`, `project milestone new` / `list` /
  `show` / `set` / `rename` / `move` / `delete`.
  [Projects & milestones](references/projects.md).
- **Config** — `config` (list / get / set / unset), `config setup`. Precedence
  `config.local.json` > `config.json` > global > default. Read a setting with
  `knit config get <key>`, never by parsing `config.json`. Commands that record
  you (`add`, `start`, `comment`, `--mine`, …) refuse without a handle.
  [Config](references/config.md).
- **Conventions** — `conventions` (bare: print user, then repo; `--scope`),
  `conventions set --scope repo|user` (replaces the file from stdin),
  `conventions remove --scope repo|user`.
  Paths, direct edits, what belongs elsewhere: [Conventions](references/conventions.md).
- **Root** — `root` (bare: show), `root <path>` / `--worktree` / `--local` /
  `--off`. `using knit root: <path>` on stderr means writes land outside the
  project you are in. [Root](references/root.md).
- **Mounts** — `mount` (bare: list), `mount add` / `set` / `remove`.
  Read-only directories outside the repo that KB sources point into; a mount
  is not a root. [Mounts](references/mounts.md).
- **Repair** — `verify`, `relink`. [Verify / relink](references/verify.md).
- **Local web UI** — `web` (browser UI, loopback by default; CLI stays canonical for scripted / agent flows).
  `--host <ip>` widens the bind (`0.0.0.0` = every interface) for a container
  or remote box. Only offer it when the user is asking to reach the UI from
  another machine, and say why it matters: the API is unauthenticated and has
  every mutator the CLI has, so a non-loopback bind grants write access to
  anyone who can reach the port. The command warns on stderr when it does.

## Gotchas (the bits `--help` doesn't shout about)

### Output streams are strict

- Errors go to **stderr**. Stdout stays capture-clean so
  `$(knit ...)` substitution works. If you parse stdout and find
  diagnostics in it, that's a bug — report it.
- ANSI styling is **auto-stripped** when stdout is redirected,
  `NO_COLOR` is set, `--no-color` is passed, or `--json` is used.
- `--json` works on every command; keys are snake_case.
- Mutator stdout is the resulting item as a single JSON object (with
  `--json`); diagnostics still go to stderr. Exception: `knit comment`
  returns `{ item_id, comment: {...} }`.

### Short id vs full id

- Item/comment ids are 18-char **NanoIDs** — uppercase letters + digits
  only (`0-9A-Z`), e.g. `KJMBEYIOYH80SQ16JJ`. They are **not**
  time-sortable (order by `created`, not by id).
- Ids are **case-insensitive**: stored uppercase, but any prefix you
  type resolves case-folded (human output lowercases for calm).
- Human-rendered output abbreviates each id to the shortest unique
  prefix (short 4-char floor), git-style.
- `--json` **always** emits the full id. Use it when you need a stable
  id to pipe into the next command.
- Most commands accept any unambiguous prefix as input — when in
  doubt, paste the full id from `--json`.
- **Never show the user a bare id** — id + quoted title,
  `k3f9 "Add rate limiting"` (see [`knit`](../knit/SKILL.md) skill > Orientation). Keep the pairing when you relay or summarize CLI output.

### `--watch` is human-only

Read-only commands (`list`, `tree`, `ready`, `next`, `show`, `context`)
accept `--watch` for live monitoring on a TTY. Agents never use it: no
parseable output, it blocks the process, and it is rejected with `--json`.
Re-run the one-shot command instead.

### Pick uses stderr for its prompt

`knit pick` renders its interactive picker on stderr and prints the
chosen full id to stdout. That's what makes `$(knit pick ...)`
substitution work cleanly. It refuses to run if stderr isn't a TTY
— agents and scripts use `knit list --json` instead.

### `ready` is the inventory; `next` is the recommendation

Two different jobs — don't read `ready`'s first row as "the pick".

- **`ready`** lists *everything* workable: open state, not blocked, no
  blocked open ancestor. A held item shows `⊘↑` in `list` / `tree`, a
  `held` row in `show`, and `held_by` (the ancestor's id) in `--json`;
  `unblock` on it refuses and names the ancestor. Containers (a project, an epic with open children) are in the
  list, because "what could be worked" includes them. Order is plain
  Rank ascending (`knit reorder <id> --before <other>`).
- **`next`** returns exactly one leaf item; its ordering rule is
  [`knit-work`](../knit-work/SKILL.md) skill > Pick-next ordering. The dim
  "why this pick" line is human output only — `--json` is a plain
  `JsonItem`, no reason field.

Both take `--mine` and `--assignee <handle>` (mutually exclusive), so
"what can bob pick up" / "what should bob do next" are one command,
not a `list --assignee` plus manual filtering. `--slate <name>` scopes
either to a slate the way it does `list` (effective slate) and composes
with `--under`; `next` still prefers in-progress work inside it.

### `knit start` auto-assigns to you

`knit start <id>` sets `state=in-progress` **and** sets `assignee`
to your handle from `.knit/config.json`. When the item already
belonged to someone else the human output says so
(`took over from <handle>`) — but `--json` only shows it in the
returned `assignee`, so read that field before assuming a start was
a no-op on ownership. `--keep-assignee` starts the item without
touching the assignee, which is what you want when you're running the
command on someone else's behalf.

### Check `knit handles` before assigning someone else

Handles have no registry: an assignee is whatever string was typed, matched
exactly. Before `assign` / `--assignee` to anyone but yourself, run `knit
handles` and reuse the spelling already there. Your own handle comes from
`knit config get handle` (what `start` uses), never from the list — it is
marked `me` and listed even before first use. If a near-spelling of yours
already exists, raise it and fix config; don't assign to the other spelling.

### Drafts: `knit approve`, `--draft` / `--todo`

A `draft` is an item nobody has specified or approved yet. `next`,
`ready` and `pick` never return one; every other count (rollups,
overdue, at-risk) includes it.

- `knit approve <id>` promotes `draft` → `todo`. `knit set <id>
  state=todo` does the same; `approve` refuses an item that isn't a
  draft.
- `knit start <id>` on a draft **refuses under `--json`** (or a
  redirected stderr) and names `approve`; at a terminal it asks and a
  yes promotes-and-starts. `knit done` from `draft` always refuses —
  use `approve` first, or `knit dismiss`.
- `knit add … --draft` creates one; `--todo` forces `todo`. Without a
  flag the `initial_state` config key decides (`todo` by default) and
  applies to every item type. It inherits, so `config set --global
  initial_state draft` makes everything *you* create a proposal
  wherever you work; a repo that sets its own still wins.

### `knit done` doesn't cascade

Closing a parent doesn't close its children. `knit dismiss` is the
only mutator that offers a cascade (interactive prompt; `--cascade`
flag in scripted / `--json` mode) and only sweeps open descendants.

### `knit delete` vs `knit dismiss`

- `knit dismiss <id>` is soft-delete: sets `state=dismissed`, file
  stays on disk, history preserved. Reversible via `knit reopen`.
  **Default choice.**
- `knit delete <id>` is hard-delete: the item file is removed from
  disk. Not reversible from the
  CLI — only `git`. Use only when the item must be gone (e.g. created
  by mistake).
- Only accepts an **item id** (or unambiguous prefix). It cannot be
  used to delete arbitrary files — the id is resolved through the
  item store and a missing/ambiguous id is rejected.
- Refuses when descendants exist unless `--cascade` is passed (or you
  confirm interactively). Interactive run prompts before deleting;
  scripted / `--json` runs require `--yes` (or `--cascade` for
  subtrees) — never deletes silently.
- Back-references in other items are **not** auto-cleaned. Run
  `knit verify` after a delete to surface broken links / parent refs.

### `--json` envelopes differ per command

There is no single wrapper — check the shape before you `jq` it:

| Command | Top level | Items at |
| --- | --- | --- |
| any mutator (`add`, `start`, `set`, …) | the item object itself | `.` |
| `comment` | `{ item_id, comment }` | — (the new comment) |
| `list`, `ready` | `{ items: [...] }` | `.items[]` |
| `tree` | `{ roots: [...], unparented: [...] }` | `.roots[].item`, recursing into `.children[]` |
| `search` | `{ hits: [...] }` | `.hits[]` (kind + id + snippet, not a full item; `type`/`state` only on item hits) |
| `timeline` | `{ from, to, nodes, edges, milestones }` | `.nodes[]` (timeline shape, not `JsonItem`) |
| `status` | counts + `{ by_assignee, blocked_items, overdue_items }` | `.blocked_items[]` / `.overdue_items[]` (full `JsonItem`s) |

- `tree`'s `unparented` is the **orphan** bucket — items whose `parent:`
  points at an id that doesn't resolve. Items with no parent at all are
  roots, not orphans. Both buckets have to be read to see every item.
- Every item carries `created` (frontmatter, immutable — use it for age) and
  `modified` (the item file's mtime, i.e. the last `knit` write — use it for
  recently-touched work).

### Blocked is an orthogonal flag, not a state

An item is blocked while it has any impediment: a `Blocked by` link row
or the free-text `blocked:` frontmatter reason. State is **never**
touched, and `knit set <id> state=blocked` is refused, pointing at
`knit block`.

`knit block <id> [--by <other>] [--reason "<text>"]` needs at least one.
`--by` adds a `Blocked by` row — a **live impediment edge** the
dependency graph also consumes (timeline edges, at-risk propagation;
`Blocks` and `Blocked by` rows deduped per pair). With `--by`, `--reason`
becomes that row's Notes (why it blocks; removed with the row). Alone,
`--reason` is the free-text reason for an impediment that is *not* an
item. Either replaces a previous text and reports it (`replaced_reason`
in `--json`); the free-text reason and the rows never touch each other.

Resolving:

- One blocker: `knit unlink <id> <blocker>`. The item stays blocked
  while other rows or a reason remain; when only the reason is left,
  `unlink` prints it — check whether it still holds before running
  `knit unblock`.
- Only the reason: `knit set <id> blocked ""`.
- Everything: `knit unblock <id>` — removes every row and the reason.
  Interactively it confirms (one impediment) or offers a picker
  (several; all preselected, Ctrl+C cancels).

Closing a blocker resolves nothing. Every call that closes an item
(`done`, `dismiss`, `set state=`) lists the open items still blocked by
it with the fix per item; `--json` carries them as `blocked_dependents`
(`id`, `title`, `remaining` — what else blocks it, null ⇔ this was the
only impediment). The key is present only when the call closed the item
(`[]` when nothing depends), on each `cascaded` entry too. `verify` warns
on the rows left behind.

The `blocked` key in `--json` (`show`, `list`, `timeline`) is the
combined summary — reason first, then `blocked by <title>` per row; null ⇔
not blocked. `context --json` lists it per source: the item's own block,
then the holding ancestor's (`inherited_from`).

To see what's stuck rather than what's workable: `list --blocked` /
`tree --blocked` (refused together with `--ready`, which means the
opposite). Blocked items carry a `⊘` marker in every `list` / `tree`
render even without the filter; the reasons are in `show` and in
the `blocked` key of `list --json`.

### `search` spans four corpora, not just items

`knit search "<query>"` covers items **and** KB entries, design docs and
project docs. Narrow with `--kind item|kb|design|project` (default
`all`). Read `kind` before `id` — the address shape differs per kind:

| kind | `id` | act on it with |
| --- | --- | --- |
| `item` | full uuid | `knit show <id>` |
| `kb` | `<bucket>:<slug>` | `knit kb show <id>` / `knit ref show <id>` |
| `design` | `design:<slug>` | `knit ref show <id>` |
| `project` | `<project>/<doc-path>` | read the file under `.knit/projects/` |

`type` / `state` are item-only and simply absent on doc hits. Human
output tags doc hits with their kind; item rows carry no tag.

### Concurrent writers

Several agents may write one `.knit/` at once. Each write lands whole, and a
write to an item someone changed since you read it is re-applied onto theirs:

- **Comments, link rows, tags merge** — both writers' additions survive.
- **A field both of you set** (state, priority, assignee, title, …) ends with
  the later write. Nothing *else* is lost.
- **A body both of you edited refuses** — exit `4`, "changed since this command
  read it", nothing written. `knit show` it again and redo the edit.
- **`move`, `retype`, `relink`, `kb split` / `rename` / `delete`** hold
  `.knit/.lock`. While another one runs they wait a few seconds, then exit `4`
  naming its host, pid and operation — re-run. A lock whose knit is gone is
  broken with a stderr note; one from another machine only once it is old.
- **A crash mid-`move`** can leave links half-rewritten: `knit verify`, then
  `knit relink`. A `.knit-*.tmp` that `verify` lists is an interrupted write —
  the file beside it is intact; delete the temp.
- **A write that can't replace its file** (a share dropped, a file held open)
  exits `4` with the path and writes nothing — don't retry in a loop; report it.

### Exit codes

- `0` — success
- `1` — usage error (unrecognized command/flag, missing argument);
  emitted before the command runs, so no `--json` code accompanies it
- `2` — bad argument (bad input, missing `.knit/`) — json `bad_argument`
- `3` — not found (id, prefix, parent) — json `not_found`
- `4` — conflict (ETag / verify mismatch, a concurrent write, the repo lock, a file
  another process holds locked — retry once it is released) — json `conflict`
- `70` — internal error — json `internal`

Codes `2`+ carry the stable string code (above) on stderr in `--json`
mode for programmatic dispatch; `1` does not, since it never reaches
the command handler.

## Idioms

### Shell substitution

```bash
$EDITOR $(knit show <id> --path-only)         # open the item file
cat     $(knit kb show feature:auth --path-only)
cat     $(knit kb show source:api-spec --source-path)  # the file it points AT
knit start $(knit pick --state todo --mine)   # interactive, human only
knit done  $(knit pick --under <epic-id>)
```

### Mutator chains in scripts

Every mutator returns the resulting item under `--json`, so you can
chain them without re-reading:

```bash
id=$(knit add task "do the thing" --json | jq -r .id)
knit start "$id" --json
# ...
knit done  "$id" --json
```

### Set fields at create time, not in follow-up calls

`knit add` takes a field flag for each of `--assignee`, `--due`,
`--priority`, `--effort`, `--tags`, `--slate`, `--design` and
`--agent-skills`, so a fully-specified item is one command rather than
one `set` per field:

```bash
knit add task "wire the widget" --parent <feature-id> \
  --assignee bob --due 2026-09-01 --priority important --slate q1 --json
```

The flags share `set`'s validation exactly. **Validation runs before
the item is written**: a rejected `--due` creates nothing, rather than
leaving an item with the other flags applied. `health` and `project` have
no `add` flag — both are project-only; use `set` after `knit project new`.

### Writing doc bodies non-interactively

`--set-section <h2> --value <text>` replaces a section's content, or
appends the section if absent, on all three body-carrying artifacts:

```bash
knit edit        <id>            --set-section "Acceptance criteria" --value "- [ ] it works"
knit kb edit     doc:caching     --set-section "Decision"            --value "- write-through"
knit design edit widget          --set-section "Goals"               --value "- ship it"
```

Never hand-edit the file for this. `Links` and `Comments` are
structural and refused on all three; `--value ""` clears the content but
keeps the heading. `kb edit` reindexes the bucket; `design edit` works
on active docs only (archive is `design archive`).

**Removing a section is a different flag.** `--delete-section "<h2>"`
drops heading *and* content, on the same three artifacts. It is
repeatable, so a template's four dead headings go in one write:

```bash
knit edit        <id>        --delete-section "Repro" --delete-section "Notes"
knit kb edit     doc:caching --delete-section "Consequences"
knit design edit widget      --delete-section "Non-goals"
```

Pick by intent: `--value ""` means "this section applies, nobody has
filled it in yet"; `--delete-section` means "this section does not apply
to this item". An unknown heading is an error naming the sections the
item does have — a typo never reports a cleanup that didn't happen — and
a typo in the second name leaves the first section alone.

### Minted ids can differ from what you asked for

Every slug you pass — KB slugs and sub-doc leaves, idea `--slug`, source
`id`, `kb rename` targets, every slate name — is slugified per segment:
lowercased, diacritics folded, non-alphanumerics → `-`, capped at 40 chars.
A stray `/`, space or punctuation is normalised, not rejected; a valid slug
is unchanged. When that changes the id beyond lowercasing and spacing, the
verb prints a `note: <input> → <id> (<reason>)` on **stderr** — stdout still
carries only the id, so `$(knit ...)` keeps working. Under `--json` the same
arrives as `id_trimmed` / `id_input` / `id_reason`. Read the id back from
the output rather than assuming the one you typed.

### Renames are tooling-only

Never `git mv` an item or KB file — the relative paths in
every `## Links` row break silently. Never `rm` a KB entry either — use
`knit kb delete`, which strips incoming `KB ref` rows first
([KB](references/kb.md)). Use:

```bash
knit move <item-id> <new-slug>       # item; rewrites incoming links + prose links (a retitle never renames the file)
knit kb rename <bucket>:<old> <new>  # KB entry; rewrites incoming refs + prose links
```

Renaming a folder-form entry moves its whole folder, so refs to its
sub-docs (`feature:foo/wiring`) are rewritten too — not just the
parent ref. Target may be nested (`feature:foo` → `bar/foo`) when the
new parent is already folder-form. Body-prose code-spans naming the old ref
and markdown links into the moved file are rewritten too; `knit verify`
reports whatever else still points at the old name.
An `item:<id>` ref needs no rewrite: `move` never changes the id.

### Changing an item's type — use `retype`, not `set type=…`

`knit set` is a low-level scalar editor; it does NOT validate the
hierarchy. `knit retype <id> --to <type> [--parent <other>]` is a single
atomic write, hierarchy-checked on both edges, and refuses to orphan
existing children. Scripted / `--json` runs need `--yes`. Details:
[Items](references/items.md).

## Version

`knit version` prints the binary's version as a single stdout line, with
no build metadata. `knit --version` is an alias emitting the identical
line — either is safe to capture in a script.

## Repo format version

`.knit/config.json` carries `format_version` — the on-disk contract's
version, **not** the binary's. It stays `1` for now.

- Every command **refuses to run** against a repo stamped higher than the
  binary understands (`knit version` still works). The fix is to upgrade
  the `knit` binary — never to hand-edit `format_version` down; the repo
  really was written by a newer knit and would be misread.
- A config without the key is a format-1 repo. There is no migration
  command; don't look for one.

## Licenses

`knit licenses` prints Knit's MIT license plus the notices for the
third-party code compiled into the binary (embedded — no repo, network or
side files needed); `--third-party` narrows it to the notices, `--out <dir>`
writes both files instead of printing.

## Hit friction?

If the CLI's behavior surprises you or contradicts this skill, log
it via the feedback loop — see [`knit`](../knit/SKILL.md) skill >
Feedback loop. No project content in the entry.

## Things knit does NOT do

- Never stages or commits. `.knit/` changes sit in the working tree like
  any other edit.
- Does not auto-resolve merge conflicts. Standard markdown merge.
- No sprints, time tracking, notifications, or auth. Identity comes
  from `.knit/config.json`.
- Does not push to a hub. M1 is local-only; sync is M2 work.
