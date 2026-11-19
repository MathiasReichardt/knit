# Projects & milestones

A **project** has two halves: a tracker **item** of type `project`, and
an optional **doc folder** under `.knit/projects/<slug>/`. The folder
follows the shared doc-folder model — see
`knit` skill > Conventions > Doc folders. It holds
`readme.md`, an optional `roadmap.md`, milestone sub-docs under
`milestones/`, and any free-form `.md` files / subfolders you drop in
(browsable + searchable, inert to the link machinery — a project is
"KB-like" that way). Planning workflow lives in the
`knit-projects` skill; this is the command surface.

```bash
knit project new relaunch --title "Site relaunch" --roadmap --goal "Ship the new site by Q3."
knit project list                                            # doc folders + health + milestone counts
```

- `project new <slug>` co-creates both halves in one action: scaffolds
  `readme.md` (+ `roadmap.md` with `--roadmap`, `--force` overwrites) **and**
  mints a `project` item linked via `project=<slug>`. `--item <id>` links an
  existing project item instead; `--no-item` scaffolds the folder alone (an
  orphan — `verify` warns). The two halves must coexist; don't hand-assemble
  them with `add project` + `set project=`.
- **The scaffolds ship with prompt prose**, not empty headings — a line under
  each section saying what belongs there. `--goal "<text>"` replaces the
  readme's `## Goal` prompt with the real thing, so a non-interactive create
  doesn't have to be followed by a hand-edit.
- `project roadmap <slug>` scaffolds `roadmap.md` **standalone** for an
  existing project — the way to add a roadmap *after* creation without
  `project new --roadmap` (which forces a readme overwrite). Errors if a
  roadmap is already present (`--force` overwrites); never touches the readme.
- Free-form docs need no create command: drop the `.md` (or a subfolder) under
  `.knit/projects/<slug>/` and it shows up. **Rename/move** one with
  `knit project doc rename <slug> <old-path> <new-path>` (paths are
  project-folder-relative; a folder-form doc is addressed by its
  `<name>/readme.md`). Markdown links to it, and its own relative links, are
  repointed (`files_relinked` in `--json`). `readme.md` / `roadmap.md` / `milestones/` are reserved
  (their own commands), rejected as rename targets.

Remove a whole project with `knit project delete <slug>` (`-y` skips the
confirm; without a TTY `-y` is required). It is **not** a cascade:

```bash
knit project delete relaunch -y               # item + doc folder gone; children re-rooted
knit project delete relaunch -y --scrub-links  # also strip back-refs pointing here
```

- Deletes the project **item** *and* the doc folder, then **re-roots the
  direct children** — clears their `parent` so they surface at root
  (a parentless item is a valid, first-class state). Deeper descendants
  keep their parent. Re-home a re-rooted item later by setting a parent.
- Back-references pointing here (`Milestone` rows to its sub-docs,
  relation links to the item) are **left dangling by default** — matching
  `knit delete` and the "nothing vanishes" verify/doctor invariant; run
  `knit verify` to find them. `--scrub-links` opts in to removing them.
- Keys on the folder `<slug>` (the item's `project:` field). A
  folder-less `project` item is removed with plain `knit delete` instead.

**Milestones** are sub-docs under `milestones/`, with frontmatter
`title`, optional `target` (date), and `state` (`planned` → `reached` /
`dropped`; new milestones start `planned`):

```bash
knit project milestone new relaunch beta --target 2026-09-01
knit project milestone list [relaunch]           # all projects, or one
knit project milestone show beta                 # the sub-doc + its backlinks
knit project milestone show relaunch/beta        # qualified when the slug is ambiguous
knit project milestone set beta --state reached  # advance state; --target/--title too
knit project milestone rename beta beta-2        # slug change; re-points backlinks
knit project milestone move beta --to relaunch-2 # to another project; --rename on collision
knit project milestone delete relaunch beta -y   # leaves backlinks dangling by default
```

- `<milestone>` for `show`/`set`/`rename`/`move` is the bare `<slug>` (when
  unambiguous across projects) or the qualified `<project>/<slug>`.
  `delete` takes `<project> <slug>` as two positional args.
- `milestone show` is the **backlinks surface** — every item carrying a
  `Milestone` row targeting the sub-doc. `--path-only` prints the file
  path for `$EDITOR $(...)`.
- `milestone set` patches `--state` (`planned|reached|dropped`),
  `--target` (`YYYY-MM-DD`, or `--target ""` to clear), and `--title` in
  place; body + `created` are preserved. State advances `planned →
  reached` (shipped) or `dropped`.
- `milestone rename` moves the sub-doc and **re-points** the `Milestone`
  back-links in linked items and markdown links to it (roadmap, other docs) —
  reports both (`files_relinked` in `--json`). Contrast `delete`, which **leaves back-links
  dangling** by default (surfaced by `knit verify`); `--scrub-links`
  opts into stripping them, `-y` skips the confirm.
- `milestone move --to <project>` is the cross-project form — same
  re-pointing, plus `--rename <new-slug>` for a slug already taken in the
  destination (a collision without it errors and names the flag). Linked
  items are **not** dragged into the destination project; the command
  reports how many now link across a project boundary, and `knit verify`
  lists those as informational (`cross_project_milestones` in `--json`).
- The roadmap links to milestones with ordinary relative markdown
  (`[Beta](milestones/beta.md)`) — `roadmap.md` and `milestones/` are
  siblings. Author it by hand; `milestone rename` / `move` repoint those links.

**Todos** are the project axis's leaf item type — non-code checklist
items (`register the trademark`, `book the venue`), peer to `task` in
shape:

```bash
knit add todo "Register the trademark" --parent <project-id>
```

- `knit add` accepts `todo` like any other type. **Hierarchy is
  enforced:** a `todo`'s parent must be a `project` or another `todo` —
  it can't sit at root, and code items (`task`/`feature`/…) never nest
  under it. A hand-edit that breaks this (a todo at root, or under a
  task) surfaces as a `knit verify` hierarchy violation.
- Todos are ordinary items otherwise: states, `blocked`, `due`,
  assignee, links all work. Dev-facing views (`knit tree` without a
  project scope) hide them by default; scope to a project to surface
  them. Full planning workflow:
  `knit-projects`.

## Linking to milestones

```bash
knit link <item-id> --milestone q1/beta   # one per item — replaces any existing
```

`<slug>` resolves within the item's own project; `<project>/<slug>`
addresses any project (and disambiguates). **One milestone per item** is
the convention — the write path replaces any existing `Milestone` row
(hand-edited multiples surface via `knit verify`). Remove with
`knit unlink <item-id> q1/beta`; see backlinks via
`knit project milestone show <slug>`.

**Inherited at read time**, like slates: an item's effective milestone is
its own link, else the nearest ancestor's — a dangling own link still
counts as its own. `--milestone` filters (`list`, `tree`, `timeline`,
`status`) match the effective one; `--json` items carry it as
`effective_milestone` (`<project>/<slug>`, absent when none; the own link
stays in `links`); `knit context` shows it with `inherited_from`.
Backlinks (`milestone show`) list own links only.
