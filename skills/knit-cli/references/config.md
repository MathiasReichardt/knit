# Config

`config` (bare: list), `config list` / `get <key>` / `set <key> <value>` /
`unset <key>`, the write verbs taking one of `--local` / `--repo`
(`config.json`) / `--global`. `list` prints
every key with its effective value, its default and the **source** — the only
way to answer "did I set that, or is it the default?" — plus a legend naming
the file behind each layer.

- **Precedence, once: `config.local.json` > `config.json` > global >
  built-in default.** A committed repo setting beats a personal global (the
  team's policy wins in the team's repo); the uncommitted local overlay still
  beats everything. The same layers `mount` uses.
- **The global layer is per person, not per machine-wide policy** —
  `handle`, `display`, `due_soon_days`, `initial_state`, a shared mount. It lives in the OS
  config directory (`knit config list` prints the path; `KNIT_CONFIG_HOME`
  overrides it), never beside the binary.
- **Repo-only keys don't inherit.** `format_version` / `knit_root` / `repo_name` / `status_report.*` are refused by
  `config set --global`, and a hand-edited one is listed as ignored rather
  than silently dropped. A global `mounts` path must be absolute or
  `~`-anchored — a repo-relative one would mean a different directory in
  every repo.
- **`handle` / `display` never belong in the committed `config.json`** — it
  beats every cloner's global layer, so they would all record as you.
  `config set` / `unset handle` with no layer flag act on `config.local.json`
  (this checkout, like `git config`); `--global` sets it for every repo.
  Only an explicit `--repo` commits one. `knit init` inherits the global
  identity; one it is given or asked for that differs goes to
  `config.local.json`. Interactively, with no global config, it offers once
  to save the identity for every repo on the machine. `knit verify` warns
  (`committed_identity`) on one found in `config.json` — `knit config unset
  --repo handle` removes it.
- **Read a setting with `knit config get <key>`** — bare value on stdout, so
  `$(knit config get handle)` substitutes. Don't parse `config.json`: a key
  absent from the file is not a key without a value, and the local overlay
  may be the one in force.
- `format_version` is not settable (it is the on-disk contract), `mounts`
  is declared with `knit mount` and `knit_root` with `knit root`; all are
  listed, and all refuse a `set` with the reason. Keys knit does not model are listed too, marked
  `(unknown key)` — a typo'd `due_soon_day` is inert, not an error, so it has
  to be visible.
- `repo_name` is the name `knit web` shows in its header (and tab title);
  unset means the repo's folder name. The web UI renames in place, writing
  `config.json` — refused while `config.local.json` sets it.
- `tree_sort` (`rank` / `state`, default `rank`) — sibling order for `tree`,
  `list --sort tree`; a reading preference, settable `--global`.
- A config file that does not parse is refused rather than reported as a
  table of defaults, and every writer (`config set` / `unset` / `setup`,
  `mount`, `root`) refuses to write over it; `knit verify` lists it as well.
- **`requires` — settings every checkout must fill in locally.**
  `knit config set requires.knit_root "<where to get it>"` (or
  `requires.mounts.<name>`) commits the requirement and a hint, never the
  value. Written to the checkout's `config.json` even under a redirect;
  `--local` / `--global` are refused. A checkout that hasn't filled one in
  refuses every command except `root`, `config`, `mount`, `init`, naming the
  fix. `knit config list` shows each as set / not set here.
- **`forges` — which forge runs an origin host**, for `knit url` and the
  web UI's share button. github.com, gitlab.com, codeberg.org, gitea.com and
  Azure DevOps (`dev.azure.com`, `*.visualstudio.com`) are built in; any
  other host needs one entry: `knit config set forges.<host> <kind>`, kind
  `github` / `gitlab` / `gitea` (Forgejo too; Gitea 1.21+) / `azure` /
  `custom:<template>` (`{web}`, `{ref}`, `{path}`; must yield an http(s)
  URL). The name splits at the first dot, so a dotted host needs no quoting.
  `--web <url>` when the origin's host isn't the web address (an SSH alias,
  a separate SSH host, a sub-path install): the repo path is appended to it.
  `--global` answers once for every repo on the machine; an entry beats the
  built-in for its host. `get` / `unset forges.<host>` likewise; `list` shows
  the entries as rows. Links always name `HEAD`, so a file not yet on the
  default branch 404s.
- **`knit config setup`** fills in every unmet requirement, plus your handle
  when none resolves — asked one at a time on a terminal, or as flags:
  `--knit-root <path>` (`.` = own `.knit/` on purpose), `--mount
  <name>=<path>` (into `config.local.json`), `--handle` / `--display` (saved
  globally). With `--non-interactive`, whatever is still missing is an error
  naming the flag for it. Ask the user for a path or handle you don't know;
  never invent one.
- **No handle is a state, not `""`.** Commands that record you refuse
  without one — `add`, `start` (not with `--keep-assignee`), `comment`,
  `project new`, and every `--mine`; reads still work. Fix once with
  `knit config set --global handle <you>`. A hand-edited `"handle": ""`
  counts as unset and doesn't shadow the global one.
