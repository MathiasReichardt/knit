# Git integration in the SPA — idea

Status: **idea / not scheduled.** Lowest-value convenience: the team already uses git directly.

The design decisions below are kept as they were settled.

## Design

- **G1. Opt-in, default off — a contained exception to "Knit never invokes
  git":** only when enabled, only `.knit/`, never a merge. Lowest-value phase;
  the team already uses git directly, so it is convenience and goes last.
- **G5. SPA-only** — CLI parity waived; CLI users have git.
- **G2. Shell out to system `git`.** No LibGit2Sharp: a native asset breaks
  the single AOT binary, and its licence is copyleft-with-exception. No `git`
  on PATH, or not a git repo → the feature disables itself with a message.
- **G3. Commit stages `.knit/` only** — `git add -- .knit`, never `-A`
  (`.knit/.cache/` is excluded by its gitignore). File list from
  `git status --porcelain -- .knit`, per-file `git diff` / `git diff --cached`.
  Auto-generated editable message (`knit: update 3 items, add 1 KB entry`),
  written to a tempfile for `git commit -F`. Branch from
  `git branch --show-current`; detached HEAD, a merge / rebase in progress, or
  unmerged paths block the commit with "resolve in your git client". Author is
  the repo's git identity, not the knit handle.
- **G4. Pull / push, zero merge logic.** Ahead / behind from
  `git rev-list --count --left-right @{u}...HEAD`. Pull is `--ff-only`; a
  diverged branch gets "reconcile with your git client, then return". Push
  only when an upstream exists, the tree is clean outside `.knit/`, and the
  branch is not behind. A commit never auto-pushes.
- **Security:** loopback only; every call an arg array (no shell);
  `GIT_TERMINAL_PROMPT=0` so auth fails fast instead of hanging, stderr
  surfaced; credentials stay with the user's helper / SSH agent — knit stores
  none.
- **Open:** nested `git.enabled` (leaning — room for later git settings) vs a
  flat `git_enabled`.

## Step 1 — Git wrapper + config gate
- `Knit.Git` shell-out wrapper (arg arrays, `GIT_TERMINAL_PROMPT=0`, tempfile
  commit messages, `.knit/`-only pathspec). `config.json` `git` object
  `{enabled:false}` (default off). `knit init` offers to enable (`--git` /
  `--no-git`). Endpoints **not mounted** when disabled.
- **Acceptance:** wrapper unit tests (staging pathspec, detached-HEAD detection,
  ahead/behind parse); disabled → `/api/git/*` 404.

## Step 2 — API `/api/git/*`
- `GET /status` (branch, ahead/behind, upstream-exists, clean-outside-knit flag,
  changed `.knit` files + diffs), `POST /commit`, `POST /pull` (ff-only),
  `POST /push`. Guards: clean-outside-`.knit` for pull/push; block on
  detached-HEAD / unmerged / mid-operation; commit never auto-pushes.
- **Acceptance:** guard-path tests (diverged → no merge; dirty-outside → pull/
  push disabled).

## Step 3 — SPA panel + skills + README
- Git panel: read-only diff preview (reuse existing diff rendering), editable
  auto-generated commit message, branch shown, pull/push actions with the
  divergence/unmerged warnings.
- **Acceptance:** `/knit-cli` (or a web-usage note) documents the config flag,
  the `knit init` prompt, and the defer-merges-to-git-client posture; README
  documents the feature + security posture.
