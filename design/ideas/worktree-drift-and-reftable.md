# Worktree drift check + reftable support — idea

Status: **idea / not scheduled.** Both are follow-ups to
bare-repo worktree support, and both hinge on
the same open question.

**Open: may Knit run read-only git commands?** The standing principle "local
mode never invokes git" ([overview.md](../overview.md) > Principles) covers two
things: changing git state (add, commit, merge — stays forbidden) and reading
it (`status`, `worktree list`). Narrowing it to "never changes git state" would
make both ideas below cheap and exact. Conditions if it is narrowed: only in
rare commands (`verify`, `knit root` setup), never on the per-command path;
no `git` on PATH or no repo → a note, never an error; argument arrays,
`-c core.fsmonitor=false` (an untrusted repo's config can run code on
`status`), `GIT_TERMINAL_PROMPT=0`. Needs a recorded decision in
[decisions.md](../decisions.md) first.

## Drift into a redirected worktree's own `.knit/`

A redirected worktree still has the branch's own `.knit/` on disk, hidden. An
agent editing it by hand instead of through the CLI writes where nothing reads,
silently — and the edit lands on the branch when committed.

| Option | Needs git call | Verdict |
|---|---|---|
| `git status --porcelain -- .knit` in `verify` | yes | exact, simplest |
| Parse the worktree's git index (stat vs entry, untracked files) | no | exact for common cases; index v2–v4 + extensions are real work |
| Pre-commit hook refusing staged `.knit/` changes in a redirected worktree | no (git calls knit) | exact at the point drift does harm; per-clone hook setup; uncommitted drift unseen |
| mtime newer than the redirect | no | rejected: every merge / checkout trips it |
| Compare local items with the target's | no | rejected: local is an older copy, differences are normal |

Without git calls: the hook. With them: `verify` + `porcelain`, the hook
optional.

## Reftable ref storage

Under the reftable format (git ≥ 2.45, opt-in; planned as the default in
Git 3.0) `HEAD` and `worktrees/*/HEAD` are placeholders
(`ref: refs/heads/.invalid`), so a worktree's branch can't be read from files.
Phase 11 detects it from that placeholder in the bare repo's `HEAD` and
degrades: the nudge names no path.

Full support is `git worktree list --porcelain` (paths + branches, any ref
format) — trivial once read-only git calls are allowed; without them, parsing
the binary reftable blocks, which is not worth it. Trigger to revisit: reftable
becomes git's default, or a user hits the degraded note.

Related, same dependency: a per-command check that a redirect target still has
its branch checked out (`knit_root` is a path, so switching the target folder's
branch silently moves every redirected worktree).

Also waiting on it: the [catch-up diff](catch-up-diff.md), an explicit user
action rather than a per-command check.
