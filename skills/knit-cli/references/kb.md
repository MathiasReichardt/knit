# KB

## KB sub-docs use a nested slug

For `feature:`, `doc:` and `source:` entries that have been split into
folder form, `knit kb add <bucket> <slug>/<sub>` creates the sub-doc at
`<slug>/<sub>.md` alongside the parent's `readme.md`. The nested form
**refuses** if the parent is still flat — run `knit kb split
<bucket>:<slug>` first. A source's id is its slug, so a nested source
stores the **leaf** as its `id:`. `adr` / `log` / `idea` don't nest.

`kb split` repoints what its own move invalidated — `KB ref` rows in
items and prose markdown links in items, KB entries, designs and project
docs — and reports how many references it touched, so `verify` stays silent
without a follow-up `relink`.

## `kb rename` rewrites every ref it can find

Besides `KB ref` rows in items, `kb rename` rewrites:

- **KB-to-KB frontmatter refs** — `supersedes` / `superseded_by` (full
  `bucket:slug` refs) and `feature` (a bare slug) in every other KB entry.
- **Body prose** in items, KB entries, designs and project docs, on the
  lines `knit verify` scans (outside code and `## Links` rows): a code-span
  that is exactly the old ref, or the old ref + `/<sub>`, and markdown links
  into the moved file or folder. A similar ref (`doc:old-namesake`) is left alone.

`--json` adds `kb_entries_affected` (refs of KB entries whose frontmatter
changed) and `files_relinked` (paths of files whose prose changed)
alongside `items_rewritten`. A ref spelled any other way — a short ADR
number, a path in plain text — is not found; `knit verify` flags what is left.

## Deleting a KB entry — `knit kb delete`

Don't `rm` a KB file/folder — incoming `KB ref` rows in items break
silently. Use `knit kb delete`, which strips every matching row first,
then removes the entry.

```bash
knit kb delete <bucket>:<slug>                 # prompts; rewrites incoming kb_refs
knit kb delete <bucket>:<slug> --force         # skip prompt (non-interactive / agents)
knit kb delete <bucket>:<slug> --force --delete-folder  # folder-form with sub-docs
```

- A folder-form entry with sub-docs / attachments refuses to delete
  unless `--delete-folder` is given; the error lists the blocking files.
  `--force` skips the confirmation but does **not** bypass this guard.
- Like `pick`, a delete refuses when stderr isn't a TTY (or `--json`)
  unless `--force` is passed — so it never blocks waiting on a prompt.
- `--json` returns the deleted paths plus the affected-item and
  affected-KB-entry counts (detailed below).
- Delete also clears **KB-to-KB frontmatter refs** — `supersedes` /
  `superseded_by` and `feature` (bare slug) — in every other KB entry
  that pointed at the deleted one; `kb_entries_affected` lists their
  refs. Folder-form deletes also clear refs to the entry's sub-docs.
  The confirmation prompt and the dry-run preview both report the count.
- Deletes are containment-guarded: a ref that resolves outside
  `.knit/kb/` is rejected without touching the filesystem.
