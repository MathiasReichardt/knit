# Fields — due, priority, health, tags

## Due dates

An optional `due:` frontmatter field, date-only ISO `YYYY-MM-DD` (no
time / timezone). Set and clear it with `knit set`:

```bash
knit set <id> due=2026-07-15   # set a deadline
knit set <id> due=             # clear it
```

- A malformed `due:` **already on disk** degrades to "no due" — it never
  fails the item (CLI input, by contrast, is rejected).
- `list` / `tree` emphasize **overdue** (red) and **due-soon** (amber)
  rows; the due-soon window is `due_soon_days` — a config key, so it may come
  from the repo or the user's global layer (default
  `3`; `0` is valid and means "amber only on the due day itself" — there is
  no value that turns the signal off). `--json` emits `due` as the raw ISO
  string.
- Filter with `--overdue` / `--due-before` / `--due-after`, sort with
  `--sort due` (see Filters).

## Priority

An optional `priority:` frontmatter field, one of
`someday` · `normal` · `important` · `critical` (low→high). Set / reset
with `knit set`:

```bash
knit set <id> priority=critical   # drop-everything
knit set <id> priority=someday    # do-whenever
knit set <id> priority=normal     # or priority= — both reset to the default
```

- **Unset ⇒ `normal`.** An item with no `priority:` sorts and filters
  exactly as `normal`; the field is only written when it differs from
  `normal`. A malformed value **on disk** degrades to `normal`; CLI input
  (and `--min-priority`) rejects an unknown level.
- Filter with `--min-priority <level>` (threshold, inclusive upward),
  sort with `--sort priority` (critical-first, rank tiebreak). Both on
  `list`; `--min-priority` also on `tree`.
- `--json` always emits the resolved `priority` (even the `normal`
  default).
- **Priority does not drive `ready`** — that stays plain rank order;
  priority is an explicit sort/filter lens there. `next` *does* weigh it,
  but below deadline pressure (see the core's `ready` vs `next`).

## Health

An optional `health:` frontmatter field — the PM signal on how a
project is going, one of `on-track` · `at-risk` · `off-track`. **Valid
on `project` items only**; `knit verify` flags it anywhere else. Not to
be confused with `knit verify`'s repo integrity checks.

```bash
knit set <id> health=at-risk
knit set <id> health=            # clear the signal
knit list --health at-risk --health off-track
```

- **Unset ≠ `on-track`.** No signal means nobody assessed it — the human
  views render it as nothing at all, and `--health` never matches it.
- Rendered as a word, not a glyph: alongside the state in `list`, after
  the title in `tree`, as a row in `show`, and as its own column in
  `knit project list` (the one view where every row is a project).
- A malformed value on disk degrades to unset; CLI input (`set`,
  `--health`) rejects an unknown signal.

## Tags

An optional `tags:` frontmatter field — free-form cross-cutting labels,
the **same mechanism as KB tags** (not a parallel one). Set with
`knit set` (replace-only — the value list replaces the whole set):

```bash
knit set <id> tags=api,security   # set/replace the tag set
knit set <id> tags=               # clear all tags
```

- **Normalized on write:** trimmed, empties dropped, case-insensitively
  deduped keeping first-seen casing/order (`API,api` ⇒ just `API`).
  Unset ⇒ empty; the field is only written when non-empty.
- No `tags+=` / `tags-=` yet — the setter splits on the first `=`, so
  incremental add/remove is a tracked follow-up; use replace for now.
- Filter with `--tag` (repeatable, **AND**) / `--no-tag` (repeatable,
  exclude-any) on `list` + `tree`; `--json` always emits the resolved
  `tags` array (`[]` when none).
