# Slates

An optional `slate:` frontmatter field naming a **slate** — a loose,
named selection of what to work on next (deliberately not a sprint: no
time-box, no ceremony). The registry is `.knit/slates.md`, a markdown
table where **row order is the display order**; it's created lazily by
the first `knit slate add`.

```bash
knit slate add q1-push --status active   # add (status: planned|active|archived)
knit set <id> slate=q1-push              # assign; slate= clears
knit list --slate q1-push                # filter to the slate
knit tree --no-slate                     # only unassigned items
knit next --slate q1-push                # the next leaf to work inside it
```

- **Inheritance (read-time):** an item's effective slate is its own
  `slate:` if set, else the nearest ancestor's. Filtering a slate
  therefore includes descendants — and an **explicit child value
  overrides**, re-rooting that subtree under its own slate. Nothing is
  ever written to children.
- **`--json` carries both**: `slate` is the item's own field (null when it
  inherits), `effective_slate` is the resolved value. Read `effective_slate`
  to know which slate a row belongs to — on an inherited item `slate` is
  null even in the output of `list --slate <name>`. Present on `list`,
  `tree`, `ready`, `show` and every mutator; omitted entirely when the item
  has no slate by either route.
- `set slate=` validates against the registry; `--force`
  forward-references a slate you haven't added yet.
- `slate rename` walks items and rewrites `slate:` (reports the count).
- `slate rm` **clears the slate from every item carrying it** (reports the
  count) — their descendants lose the inherited slate too. To keep the
  assignments, `slate archive` or `slate rename` instead. It also clears
  a slate that is only on items, not in the registry.
- `slate list` hides archived rows unless `--all`.
- `verify` warns on a `slate:` value with no registry row (`unregistered_slates`):
  `slate add` keeps it, `slate rm` clears it. `--strict` fails on it.
- **No separate display name:** `knit slate add "Sprint 1"` stores and echoes
  `sprint-1`, which every later command and `--json` payload uses. Keep the
  prose form in `--note "Sprint 1 — payments hardening"` (shown by `slate list`).
