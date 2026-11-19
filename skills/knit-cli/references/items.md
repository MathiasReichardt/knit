# Items

## `reorder` may rewrite its siblings' ranks

A sibling group can hold ranks with no room left where you are asking.
`reorder` respaces the group itself and reports `respaced N sibling rank(s)`,
so nothing needs running first — but expect those siblings in the diff
alongside the item you moved.

## `reorder --by state` writes the state order into rank

`knit reorder --by state --under <id> [--shallow]` or `--all` — a scope is
required. Minimal: only items out of state order get a new rank. A snapshot;
the next state change drifts again. `--dry-run` reports the count first.

## `knit reopen` reverts to todo

`knit reopen <id>` sets `state=todo` from any other state
(`in-progress`, `review`, `done`, `dismissed`). Assignee, blocked flag,
parent, and link rows are untouched — it's a pure state revert.
Idempotent on items already in `todo`.

## `knit set state blocked` errors

`blocked` is a flag, not a state: `knit set <id> state=blocked`
returns a `BadArgument` exit pointing at `knit block`.

## `knit set <id> parent=…` is rejected — parent is a link

Parent lives in the body `## Links` table (a relation row), not as a
settable frontmatter scalar — the `parent:` frontmatter is a derived
projection. `knit set <id> parent=<pid>` returns a `BadArgument` exit
pointing at `knit link <id> --parent <pid>` (and `parent=` at
`knit unlink <id> parent` to clear). Use those verbs — they validate
the hierarchy and self-parent rules. `retype --parent` re-parents while
changing type.

## `knit set design=<slug>` validates the slug exists

`design=` checks its value (as does `slate=` — see Slates): the slug must
resolve to a design doc (active `.knit/designs/<slug>-design.md`, or an
archived `completed/<slug>/…`). A typo returns a `NotFound` exit citing
the expected path and pointing at `knit design new <slug>`, rather than
silently binding a dangling `design:` that `knit context` would then
report as an "Active design" with no file behind it. An empty value
(`design=`) still clears the field.

## Title and the leading body H1 stay in sync

The leading `# H1` of an item's body is not independent content — it
*is* the title, mirrored. Both write paths keep them equal:

- `knit add <type> "<title>"` seeds the body with `# <title>`.
- `knit set <id> title="<new>"` rewrites the frontmatter title **and**
  the leading `# H1` in one write — replacing it if present, inserting
  it right after the frontmatter if absent. Only the single leading H1
  line is touched; a non-leading heading or prose is never altered.

The web body editor does the reverse: editing the leading H1 there
updates the title.

A leading H1 that drifts from the title (legacy / hand-edited files) is
left visible by the renderer rather than hidden, and surfaces in
`knit verify` — repair toward the body H1.

## Changing an item's type — use `retype`, not `set type=…`

`knit set` is a low-level scalar editor; it does NOT validate the
hierarchy. Re-typing an item the wrong way leaves you with a
feature under a task or an epic with a feature child that is no
longer a valid descendant.

Use `knit retype` instead — single atomic write, hierarchy-checked
on both edges, refuses to orphan existing children.

```bash
knit retype <id> --to feature                  # change type, keep parent
knit retype <id> --to feature --parent <epic>  # change type AND re-parent
knit retype <id> --to task --parent ''         # re-parent to root (detach)
knit retype <id> --to feature --yes --json     # non-interactive (for agents)
```

retype validates both edges and refuses (non-zero, with an `error_code`
in `--json`) on an unknown type, an illegal parent/type combo, a change
that would orphan existing children, or a parent inside the item's own
subtree. The message names the offending ids — run it to see specifics.

`--yes` is required for non-TTY / `--json` runs because retype is
destructive in the "I changed how this fits the tree" sense and you
should make that choice explicitly when scripted.
