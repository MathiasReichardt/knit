# Root

## Root resolution — `--root` / `KNIT_ROOT` / `knit root`

Precedence: `--root <path>` → `KNIT_ROOT` → the walked-to checkout's
`knit_root` → the walk itself. `--root` / `KNIT_ROOT` name a root directly and
never follow a `knit_root`.

- **A root is a `.knit/` with a `config.json`.** A directory merely *named*
  `.knit` (binaries unpacked into a dotfolder, a half-finished `init`) is not a
  root: the walk skips it, prints a `note: skipped <path>` to stderr, and
  `verify` reports it as a stray directory.
- **The walk stops at the enclosing git repo root** — a knit repo is a git
  repo's tracker, so a `.knit/` above the project you are standing in is never
  picked up implicitly. Outside a git repo the walk is unbounded.
- **`using knit root: <path>` on stderr** whenever the resolved root is neither
  cwd nor cwd's git root. It is the only signal that a write is landing outside
  the project you are in; stdout and `--json` payloads are unaffected. A
  working `knit root` redirect is the exception: set on purpose, so quiet unless
  `--verbose`.
- **A set-but-unresolvable override errors as itself** — "`KNIT_ROOT` points at
  X, which is not a knit repo … the walk was not attempted" — never as "no
  repo". No fallback to the walk: an override is a declaration.
- **Not found** names the directory the search started from and the boundary it
  stopped at, so "it looked in the wrong place" is readable from the error.

## `knit root` — redirect a checkout's knit state

Writes `knit_root` into the checkout's `.knit/config.local.json` (never
committed) and nothing else. Two uses: a **git worktree** sharing its main
checkout's state, and a **shared knit repo** several project repos work
against.

- **Flags are the primary surface** (agents cannot answer the interview bare
  `knit root` offers on a terminal):
  - `knit root` — effective root, origin, checkout; `--json` has
    `root` / `origin` (`walk` | `local` | `redirect` | `explicit` |
    `environment`) / `checkout` / `failure`.
  - `knit root <path>` — point at another knit repo.
  - `knit root --worktree` — point at the main checkout (from the `.git` file).
    A worktree left unset while main has a `.knit/` gets a stderr note on every
    command until `--worktree` or `--local` decides it.
  - **Bare repo** (every branch a worktree): no main checkout, so `--worktree`
    refuses. The note and the refusal name the default branch's worktree — run
    `knit root <that path>` in each other worktree. That folder must keep its
    branch checked out; the redirect is a path. Under git's reftable format no
    path can be read, so the note says `knit root <path>` without one.
  - `knit root --local` — writes `"."`: this checkout's own `.knit/`, *on
    purpose*. Distinct from unset, which means undecided.
  - `knit root --off` — remove the key (back to undecided).
- **The target is validated before writing** — it must exist and hold a
  `.knit/config.json`; a refusal writes nothing. No `.knit/` in the checkout
  itself → refused too: `knit init` for a tracker of its own, `knit init
  --external <path>` for a shared one.
- **`knit init --external <path> --hint "<where to get it>"`** starts a repo
  whose tracker lives elsewhere: `config.json` holds only `requires.knit_root`
  and the hint, then the checkout points at `<path>`. No items, templates or
  identity — the target owns those. Validated first; a refusal writes nothing.
  Commit `.knit/`; every other clone runs `knit config setup`.
- **`--local` satisfies a declared `requires.knit_root`** — for a contributor
  without the share — and `knit status` shows `local (declared external,
  overridden here)` (`local_override` in `--json`).
- **A target that redirects is flattened**: the end of the chain is written, and
  the output says so. Resolution is one hop; a chain met at runtime refuses.
- **A broken redirect refuses every command** — never a fallback to the
  checkout's own `.knit/`. The error ranks the fixes: reconnect / restore →
  repoint → `knit root --off`. A disconnected share is the usual cause, so don't
  reach for `--off` first. Bare `knit root` and `--off` still run.
- **The target's `.knit/` governs every other setting**; `knit config list`
  shows the target's values and says the checkout redirects.
- **Redirecting a checkout that has items hides them — not moved.** The output
  counts them; move or re-create what should live in the target. Outside a
  worktree it also suggests declaring `requires.knit_root` for clones.
- **Never edit a redirected checkout's own `.knit/` files.** Nothing reads them,
  yet the edit is committed with the branch. Go through the CLI; for a file Knit
  leaves to hand-editing, edit it under the real root (`knit root --json` →
  `root`).
- `knit init` refuses below a redirecting checkout; `knit status` shows an
  active redirect. Other commands stay quiet about it — pass `--verbose`, or
  check `knit root --json`, when you need to know.
