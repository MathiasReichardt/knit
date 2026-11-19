# Catch-up diff — idea

Status: **idea / not scheduled.** Waits on the open question in
[worktree-drift-and-reftable.md](worktree-drift-and-reftable.md): may Knit run
read-only git commands?

A user returning after a few days wants to know what changed: which items, KB
entries and designs, and what in them. Today the answer is
`git log -p -- .knit` or a forge's compare view: a raw text diff, path-keyed,
frontmatter as lines. The value here is the *semantic* view only — changes
grouped by item, field-level diffs, renames followed. Without that, the raw
diff already exists.

## Shape

- **Anchor.** Nobody knows a commit hash. The common case is "since I last
  looked": the SPA remembers the last seen `HEAD` per browser. A date is next
  (`git rev-list -1 --before=<date> HEAD`); an explicit revision is for power
  users. A date also survives a rebase / squash or a shallow clone, where a
  remembered commit can vanish — a missing anchor is a note naming the
  fallback, never an empty result.
- **Changed set.** `git diff --name-status -M <anchor> -- .knit`, working tree
  included, so uncommitted edits count.
- **Diff by parsing, not by text.** The old version comes from
  `git show <anchor>:<path>` through the existing item parser; old and new
  items are compared as records. Frontmatter diffs per field, the body per
  `## section`, text-diffed within a section.
- **CLI first.** `knit list --changed-since <rev|date>` (plus `--json`) is the
  cheap step and arguably the more useful one: an agent resuming a session
  asks exactly this. The SPA filter and diff view are the second step.
- **One change vocabulary.** The SPA activity feed is the live, since-server-start
  version of the same thing; the catch-up view reuses its categories (created,
  state change, comment, KB, edit) instead of a second change model.

## Mapping cases

| Change | Mapping |
|---|---|
| Item renamed | easy: the file is `<id>-<slug>.md`, so key old and new by id — a `title` field change |
| Link added / removed | easy: links are frontmatter on the source item — a field change there; the other side ("X now links to you") is not shown |
| Item created / deleted | easy: present on one side only |
| Comment added | easy: a body diff in the comments section, shown as a comment |
| KB / design renamed or moved | hard: the path is the identity — git's rename detection, otherwise delete + create |
| Item moved under another parent | a `parent` field change; the tree view can mark both old and new position |

## Open

- Does the remembered anchor live per browser (localStorage) or per user in
  the repo? Per browser is enough to start with and leaves nothing in git.
- Does a filtered tree keep the unchanged ancestors of a changed item as
  context (greyed out), as the timeline does?
