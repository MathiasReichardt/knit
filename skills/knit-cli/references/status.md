# Status

`knit status` is the scope rollup: completion %, state split,
per-assignee load, and the blocked + overdue lists in one read.

```bash
knit status                              # whole repo
knit status --project relaunch           # one project's tree
knit status --milestone relaunch/beta    # items aiming at a milestone
knit status --slate q1 --json            # a slate, machine-readable
knit status --under <epic-id>            # one branch
```

- **Scope flags only** — `--project`, `--milestone`, `--slate`,
  `--under`, AND-ed. There is deliberately **no state / type / assignee
  filter**: a completion percentage over a set that already dropped done
  items is always 0%. Want rows instead of a rollup? That's `list`.
- **`percent` is done over non-dismissed.** Dismissed work was descoped,
  so it leaves the denominator — otherwise a finished project is capped
  below 100% forever.
- **Containers count.** An epic is an item, so it sits in `total` and
  drags the percentage until its children close. Use `--under <epic>`
  when you want the branch, not the repo.
- `blocked_items` / `overdue_items` are full item objects in `--json`;
  the human rendering truncates each to 10 rows and points at
  `knit list --blocked` / `--overdue`.
- `health` is only reported when the scope resolves to **exactly one**
  project — three projects' signals have no defensible way to merge.
- An unknown `--project` **errors**; it never reports a silent 0-item
  scope that would read as "this project is empty".
- Same rollup backs the web UI's summary strip (`knit web`), so the two
  never disagree.

## Status report

`knit status report` writes `project-status.md`: a plain-markdown page for
readers who don't use Knit (in one minute, needs attention, projects,
deadlines, designs), linking into `.knit/`. Derived by code, no prose of its
own; same data on the same day gives the same bytes.

```bash
knit status report                 # writes status_report.path (default project-status.md)
knit status report --out docs/s.md # this run only
knit status report --stdout        # print instead of writing
```

- **Never overwrites a hand-written file.** The page's first line is a
  generated marker; a target without it is refused (exit 4) until
  `--force`. The web UI's menu entry ("Write status report") never forces.
- **`status_report.path`** (repo-only) sets the location, relative to the
  repo root. It and `--out` must stay inside the repo and outside `.knit/`;
  links are relative to the page's own folder. Anything else is `--stdout`.
- **Under a redirect the page lands in the knit root's repo**, not the
  checkout you ran it from.
- No scope flags — one repo-wide page. Knit writes it, never commits it.
- **Stale hint.** When a page knit wrote exists and its `Generated` date is
  older than `status_report.stale_days` (repo-only, default `1`), `knit
  status` prints one dim line, `knit verify` adds a hint (never a failure,
  not even with `--strict`), and the web UI shows a dot on the menu. Both
  `--json` forms carry `status_page` only while the page exists. No page, no
  hint — deleting it is how a repo opts out.
- The web UI shows the page read-only (menu > View status report), its links
  opening the item, project, design or KB entry they name.
