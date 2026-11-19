# Links

## Branch, repo, PR and commit rows

Four rels point outside the repo. `knit link <id> --branch <name> |
--repo <mount|dir|git-url> | --pr <url> | --commit <url>`, and the same
flags on `knit kb link <ref>` for a KB entry. Several rows of one rel are
legitimate — work splits across branches, a feature gets a follow-up PR.

- `--branch` — any name, **never validated** (knit doesn't shell out to
  git), stored as written.
- `--repo` — a declared mount name, a directory, or a git URL (ssh,
  scp-like `git@host:org/x.git`, or https).
- `--pr` — the full http(s) URL. Forge-agnostic; displayed as
  `org/repo#42`.
- `--commit` — a SHA, stored as written (its repo is the item's `Repo`
  row), or the full http(s) URL, displayed as `org/repo@abc1234`. Link one
  only when no PR row covers it (direct push, hotfix, backport) or when one
  commit in a large PR is the answer. A rewritten SHA dangles silently.
- **The target isn't a path**, so these rows carry no relative link and
  `verify` never reads them as broken; `relink` leaves them alone. A
  browsable URL is the one form that also becomes a link, under a
  shortened display.
- `knit unlink <id> <target>` takes the target **as displayed**
  (`feature/x`, `org/repo#42`, a SHA) or a PR's / commit's full URL.
- They surface in `knit context` and inherit down the parent chain like
  KB refs — the branch an epic is built on is the branch its tasks
  belong on. **Commit rows don't inherit**: a fix belongs to the item
  that names it.

## Linking to KB entries

```bash
knit link <item-id> --kb-ref doc:architecture --note "guides the rewrite"
```

Idempotent — re-adding the same target updates the row in place. Remove
with `knit unlink <item-id> doc:architecture`.

## Notes

Every row has a Notes cell, set with `--note` on `knit link` and
`knit kb link`. Re-linking an existing target edits it: `--note "<text>"`
replaces the note, `--note ""` clears it, no `--note` keeps it. On a
`Blocked by` row the note is why it blocks (`knit block --by <id>
--reason`), so it goes when the link does.
