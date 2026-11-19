# Filters, sort, dump

## Filtering large collections (`list`, `tree`)

`list` and `tree` share the same filter flags. Use them when the
default render is too noisy to scan.

```bash
knit list --open                          # everything still live
knit list --type task --type feature      # OR within a flag
knit list --exclude-type bug              # everything except bugs
knit list --open --exclude-type bug --under <epic>   # actionable build-out
knit tree --open --exclude-type bug       # same, hierarchical
```

Flags (all on `list` and `tree`):

- `--type <t>` / `--state <s>` — repeatable; OR within a flag, AND
  across flags.
- `--exclude-type <t>` / `--exclude-state <s>` — repeatable.
- `--open` — shorthand for `--exclude-state done --exclude-state
  dismissed`; both closed states drop out, not just `done`. Mutually
  exclusive with `--state` / `--exclude-state`.
- `-q / --query <text>` — substring match on title (also on `tree`).
- `--assignee <handle>` — exact-match assignee filter (also on `tree`).
- `--mine` — items assigned to me (also on `tree`).
- `--blocked` — only items carrying the `blocked` flag. Cannot be
  combined with `--ready`.
- `--overdue` — only open items past their `due` date (done/dismissed
  never count as overdue).
- `--due-before <YYYY-MM-DD>` / `--due-after <YYYY-MM-DD>` — items whose
  `due` is strictly before / after the date (undated items never match).
  Raw date windows, **not** state-aware like `--overdue`: done and
  dismissed items keep their due dates and still match, which is what
  makes retrospective queries possible. Add `--open` for live work only.
- `--min-priority <level>` — threshold, inclusive upward
  (`--min-priority important` ⇒ important + critical).
- `--tag <t>` — repeatable; **AND** (item must carry every tag), unlike
  `--type`/`--state` which OR within a flag. Case-insensitive.
- `--no-tag <t>` — repeatable; drops items carrying any listed tag.
- `--slate <name>` / `--no-slate` — mutually exclusive; match on the
  **effective** (inherited) slate, not just the item's own field — see
  Slates. Also on `next`, `ready` and `pick`. A name that is neither in the
  registry nor on any item is an error, not an empty result.
- `--milestone <slug | project/slug>` — items whose **effective**
  (inherited) milestone is that sub-doc — own `Milestone` row, else the
  nearest ancestor's; see Projects. Bare `<slug>` resolves within the item's
  own project; qualify as `<project>/<slug>` to disambiguate. Also on
  `tree` + `timeline`.
- `--health <signal>` — repeatable, OR. Only items carrying that health
  signal; an item with **no** signal never matches, since unset is not
  an assessment. See Fields.

**Tree default: ancestor context (dim).** When a filter excludes an
ancestor but keeps a descendant, `tree` **keeps the ancestor visible
but dim** so the structural context survives. `--flat` drops those
ancestors instead and marks the promoted matches with an `↑` prefix.
JSON output emits a per-node `match: true|false` so programmatic
consumers can tell matches from ancestor-context nodes; under
`--flat`, only matches are emitted.

Use `--flat` when piping into grep/awk where ANSI dim is stripped
and you need unambiguous output. The default context mode is
optimised for an interactive eye.

```bash
knit tree --type task            # default: ancestors stay (dim)
knit tree --type task --flat     # only matches, marked with ↑
knit tree -q login               # find titles containing "login"
```

`--ready` combines freely with the filter flags
(e.g. `knit tree --ready --exclude-type bug`).

## Dumping a slice: `--dump`

`--dump` on `list` / `tree` prints each selected item **in full**
(frontmatter, body, links, comments) instead of one row — read a whole
slice in one call instead of looping `knit show`. It changes the output,
never the selection: every filter above resolves exactly as it does
without it.

```bash
knit tree --under <epic> --dump          # the epic and its work, contents and all
knit list --slate q1-push --dump --json  # same slice, show --json shape per item
```

- Each record opens with `===== knit <full-id> (<short>) · <type> · <state> =====`
  — address anything you read with `knit show` / `edit` directly, don't
  re-`list` for ids.
- In `tree`, an ancestor kept only for structure is dumped too and marked
  `· context`; `--flat` drops those.
- `--json` keeps the envelope (`items` / `roots`+`children`) and swaps the
  row payload for the `show --json` shape.
- It refuses above `--dump-max` (default 40) rather than truncating — the
  error names the count, so narrow the filter or raise it deliberately.
  The size is reported on stderr.

## Sorting `list`

`knit list --sort <key>[:asc|desc]` — keys: `tree`, `rank`
(default), `type`, `state`, `assignee`, `created`, `due`, `priority`.
The `tree` key
emits DFS-by-rank order (mirrors `knit tree` ordering on a flat
list) with the Unparented group appended last. `due` puts **undated items
last in both directions** — dated work is the actionable set.
`state` orders done → review → in-progress → todo → draft, dismissed last
both ways, rank within a state. `knit tree --sort rank|state` orders each
sibling group the same way; default is the `tree_sort` config key.

```bash
knit list --sort tree            # parent-first walk, flat output
knit list --sort created:desc    # newest first
knit list --sort due             # soonest deadline first, undated last
knit list --sort priority        # critical-first, rank tiebreak
```
