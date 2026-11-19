# Planned

Committed work that hasn't started. Each entry keeps its decisions inline; they
move to [decisions.md](decisions.md) when the work lands, not before. Wishes
without a commitment live in [ideas/](ideas/README.md); milestones in
[roadmap.md](roadmap.md).

The two update entries depend on the [release pipeline](#release-pipeline): it
publishes the binaries and the manifest they read.

---

## Release pipeline

GitHub Actions builds, tests and publishes every release; nothing is released
from a local machine.

- **Releases come from CI only, on a `v*` tag push.** Every other push / PR runs
  a check job — build, test, AOT publish for every RID below — that publishes
  nothing. Git refuses a duplicate tag and GitHub a duplicate release,
  so a version can't be released twice.
- **Build matrix** — Native AOT doesn't cross-compile between OSes, so each RID
  builds on its own OS's runner:

  | RID | Runner |
  |---|---|
  | `win-x64` | `windows-latest` |
  | `linux-x64` | `ubuntu-22.04` — the binary needs the build host's glibc or newer, so the older image reaches more distros |
  | `linux-arm64` | `ubuntu-22.04-arm` — same glibc reason |
  | `osx-arm64` | `macos-latest` (Apple Silicon) |
  | `osx-x64` | `macos-latest`, cross-arch — AOT targets x64 from an arm64 Mac |

- **Prereleases** — a tag with a suffix (`v0.13.0-rc.1`) is a prerelease:
  - Only `-rc.N` and `-beta.N` are accepted; the release job refuses any other
    suffix, so semver order (`beta < rc < final`) holds.
  - Its GitHub release is marked prerelease, never "Latest" — the badge,
    `/releases/latest` and download links keep pointing at the last stable.
  - It updates only the manifest's `prerelease` channel. A stable release
    updates `stable` and also overwrites an older `prerelease` entry, so RC
    users aren't stranded on an old RC.
  - No changelog section of its own; entries stay under `[Unreleased]` until the
    stable release.
- **Stable releases only from `main`:** the release job refuses a stable tag
  whose commit isn't reachable from `main`. A prerelease tag may sit on any
  branch. No automatic dev builds — nothing publishes without a tag.
- **The release runs the check job first** (`needs: check`): the same matrix on
  the tag commit, and the publish job runs only when every leg passed — a red
  test means no release, all-or-nothing across RIDs.
  - The full suite (incl. `Knit.Web.Tests`: vitest, eslint, notices check) runs
    natively on `win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`; `osx-x64`
    is build + publish only — the same code is tested on macOS arm64.
  - Tests exercise the JIT build; each AOT binary gets a `knit version` smoke
    run on its runner.
- **The check job runs on demand too** (`workflow_dispatch`), with no tag and no
  release, and an input choosing which RIDs run (default: all) — for a test on
  one OS without a full matrix.
- **A failed release publishes nothing.** A flaky leg is re-run in place
  ("Re-run failed jobs", same commit). A failure that needs a code fix gets the
  next patch version on the fixed commit, built for every RID — one version is
  always one commit for all binaries. A tag whose release never published may
  be moved instead, since nothing points at it yet.
- **Publish flags live in the project files, not the workflow:** the job runs
  `dotnet publish src/Knit.Cli/Knit.Cli.csproj -c Release -r <rid>`, so a local
  publish is the same build. Already set there: `PublishAot` (implies full
  trimming), `IlcTreatWarningsAsErrors`, `InvariantGlobalization`,
  `StripSymbols`. Add `OptimizationPreference=Speed` — AOT already gives
  JIT-free startup; this trades some size for faster code.
- **Runner needs:** Node on every runner (`BuildSpa` runs `npm ci` + Vite);
  checkout with `fetch-depth: 0` for MinVer. The AOT publish is what enforces
  `IlcTreatWarningsAsErrors`, incl. warnings from dependencies.
- **Release assets:** one binary per RID plus `LICENSE` and
  `THIRD-PARTY-NOTICES.md` beside them.
- **Versioning via [MinVer](https://github.com/adamralph/minver)** (MIT): the
  version comes from the latest tag on every build, local or CI. Drop
  `Version` / `InformationalVersion` from `Directory.Build.props`; keep
  `IncludeSourceRevisionInInformationalVersion=false`.
  - `MinVerDefaultPreReleaseIdentifiers=dev`, `MinVerAutoIncrement=minor`:
    untagged builds read e.g. `0.13.0-dev.5`, never a release version.
  - `MinVerMinimumMajorMinor=0.12` until the first tag exists, so early builds
    don't report `0.0.0-dev.N`.
  - The tag decides the version, not MinVer: only fixes since the last tag →
    patch; any feature or behaviour change → minor.
- **A `release` skill** owns the flow: changelog, version recommendation, tag.
- **`CHANGELOG.md`** per Keep a Changelog, starting with the first release.
- Every pipeline output goes to one gitignored `artifacts/` folder.
- **Repo rules once the check job is green** (rulesets; `main` already blocks
  force pushes and deletion):
  - `main` requires the check job to pass. A check only exists after a push, so
    this means changes land through a pull request — the PR is the vehicle for
    the check, not for review.
  - `v*` tags can't be deleted or moved, with the owner as bypass — a tag whose
    release never published may still be moved.
- README > Install and `INSTALL.md` list all five binaries and their RIDs.
- README badges beside the license badge, landing with the workflow so neither
  is ever broken: CI status (the workflow's `badge.svg`), latest release
  (shields.io `github/v/release`).
- Local builds stay plain `dotnet build` / `dotnet publish` and are never
  released.
- **Acceptance:** a dry run on a throwaway tag publishes all five RIDs, and each binary runs `knit version` on its own OS /
  arch; the
  first real release is `v0.12.0`, and `knit version` on its binaries prints
  `0.12.0`; an untagged build prints a `-dev.N` version.

---

## Update check (notify-only)

The *signal*: a manifest, a daily stderr notice, the SPA settings row.
Replacing the binary is [`knit update self`](#knit-update-self).

### Design

- **Non-goals:** downloading or swapping a binary, provenance detection,
  version selection, rollback — all `update self`. Everything the manifest
  carries beyond `version` / `notes_url` exists for `update self` and is
  written by the release job.
- **Static manifest fetched at runtime; only its URL is baked in.** A string
  constant has zero AOT / single-file cost — nothing native, nothing bundled.
- **Manifest shape** — both channels, per-RID assets for `update self`; this
  client reads `version` / `notes_url` and ignores the rest, so the shape can
  grow without breaking deployed binaries:

  ```json
  {
    "stable":     { "version": "1.4.0", "notes_url": "…",
                    "assets": { "win-x64":   {"url": "…", "sha256": "…"},
                                "linux-x64": {"…": {}}, "osx-arm64": {"…": {}} } },
    "prerelease": { "version": "1.5.0-rc.1", "assets": { "…": {} } }
  }
  ```

- **No server.** A fixed-tag release asset: the release job force-updates
  `latest.json` on a pinned `latest` tag every release, giving the stable URL
  `…/releases/download/latest/latest.json` with both channels in one file.
  Assumes a public release repo (anonymous GET).
- **Privacy:** the only network call is that anonymous GET — no telemetry, no
  identifiers. Documented in [SECURITY.md](../SECURITY.md) when it lands;
  honours `check: false` and offline.
- **`manifest_url` is user-level only** (a new `ConfigScope.UserOnly`), because
  `Inheritable` would let a cloned repo repoint the machine's update check at a
  URL its author chose — a phishing primitive (a version string and a link
  rendered into your terminal) delivered by a file you never read. The
  legitimate uses (fork, internal mirror, self-hosted) are all per-machine.
- The update cache is disposable and must not live in `UserPaths.ConfigDir`;
  config layering must never take `update.manifest_url` from the repo layer.

### Manifest, version compare, the one notice

- Bake only the **manifest URL** (MSBuild prop → compile-time constant).
- Semver compare; user-level cache (`%LOCALAPPDATA%` / `$XDG_CACHE_HOME` /
  `~/Library/Caches`, not `.knit/.cache/`) storing `{last_check, latest_seen}`;
  daily throttled background fetch, never blocking, offline-silent.
- **Config:** `update` object `{check:true, prerelease:false}`, both
  `Inheritable`, plus `update.manifest_url` on `ConfigScope.UserOnly` — read
  only from `UserPaths.ConfigDir` / `KNIT_CONFIG_HOME`, never from a repo's
  committed `config.json`. Write-time validation: absolute, https only, no
  credentials. Lands with its `ConfigKeys` entry and `ConfigKeyTableTests`
  coverage like any other key.
- **One notice renderer, one daily throttle, two causes** — the same stderr
  channel; splitting them would build the mechanism twice:
  1. *A newer knit exists* — from the manifest. `knit 1.5.0 available (you
     have 1.4.0) — <notes_url>`, naming the host when `manifest_url` is not the
     baked-in default.
  2. *This repo was last opened by a different knit* — see below.
  Two causes in one run collapse into **one block**, never two lines from two
  subsystems.
- `knit update check` forces a check now and prints the result.

**Cause 2 — `last_written_by`.** The manifest answers "is there a newer knit?";
it doesn't answer "did this repo just get opened by a newer/older binary than
last time?" — the question behind the real chores: refresh the skill bundle
after an upgrade, and announce a config default the previous binary didn't
have.

- Written **once per version change**, not per mutation: knit compares the
  stored value to its own version and only writes when they differ, so a
  stable install never touches the file.
- **Not `format_version`.** That is the on-disk contract and stays `1` across
  the M-series; this is the *binary* version, noisy by design. The two must not
  share a key.
- **Lives in `config.local.json`**: per-machine, gitignored — the same shape as
  the fact it records. In the committed `config.json` it would be churn in
  everyone's diff whenever one person upgrades; the user-level cache would lose
  the signal whenever it's cleared.
- **Downgrade is a real case too** — a repo opened by an *older* binary than
  last time gets the same one-liner; it is how a stale copy on PATH gets
  noticed. (The hard refusal stays `format_version`'s job.) Re-shown once on
  the next day, as a reminder.

**Acceptance:**

- semver compare, including prerelease gated behind `prerelease:true`; silent
  when current ≥ latest.
- throttle honoured across processes (two runs in the same day fetch once).
- `update.check:false` short-circuits **before** any URL is read.
- the notice never touches stdout — asserted under `--json`.
- offline → silent, no non-zero exit, no added latency.
- unknown / extra manifest fields are ignored.
- `manifest_url` set in a repo's `config.json` is not honoured and is refused
  by `knit config set` in-repo; a non-https or credential-bearing URL is
  refused at write time.
- first run after a version change emits the notice once, the second run is
  silent, and the stored value isn't rewritten when the version is unchanged.

### `knit bundle install`

Check against today's `knit skill install` / `skill update` before starting —
they shipped after this was planned and may already cover part of it.

- One idempotent verb for everything the binary carries into a repo: the
  skills and the default templates. Installs on day one and re-installs after
  an upgrade — which is why it is **not** under `knit update *`, a namespace
  that stays about the binary. (`knit refresh` is reserved for the M2 hub
  cache rebuild.)
- Interactive by default, one prompt per asset group, showing drift (which
  skills differ from the embedded copies, which templates were edited locally).
  Nothing is clobbered silently — an edited template is a conflict the prompt
  names.
- `--skills` / `--templates` scope it (neither = both); `--yes` for agents and
  CI; `--dry-run` reports drift and exits.
- `knit skill install` folds into it as `knit bundle install --skills` — no
  shim. `knit init` calls the same path for the first write.
- **Acceptance:** a fresh repo and an upgraded repo take the same command;
  `--dry-run` names every drifted file and writes nothing; `--yes` never
  prompts; a locally edited template is reported as a conflict, not
  overwritten.

### SPA surface + README

- **SPA:** a dot on the **settings gear** plus a persistent row in the settings
  view carrying the version, the release-notes link, and this platform's
  update instructions. Dismissing hides **only the dot** — the settings row
  stays, so the check is never unreachable from the UI; the dismissal clears on
  the next *version*, not the next day. No execute button yet; that is the
  hook `update self` fills.
- **README:** per-OS update instructions and the post-update bundle refresh.
- **Acceptance:** the `knit-cli` skill documents `knit update check`,
  `knit bundle install`, and the `update.*` keys including `manifest_url`'s
  scope; the settings row is reachable after dismissal.

---

## `knit update self`

The *action*: replacing the running binary. The committed follow-up to the
update check, not a maybe. The manifest already carries what it needs
(per-RID asset URL + sha256 per channel), so the contract doesn't change.

### Install provenance + refusal

- The universal rule across `gh` / `rustup` / `deno` / `bun`: **a binary owned
  by a package manager must never be overwritten in place.** Detect the install
  method (brew, winget, scoop, apt/dnf, a user-dir download) and, when one owns
  the file, **refuse and name the right command**.
- Detection is path- plus probe-based (where the binary sits, whether the
  manager reports it). Unknown provenance is treated as a plain download —
  self-updatable — but refusal-biased: when detection is ambiguous and the
  target is outside a user-writable directory, refuse.
- **Acceptance:** a managed install prints the manager's own update command and
  exits non-zero without touching the file; a user-dir install proceeds.

### Download, verify, swap

- Resolve the RID's asset from the manifest, download over https, **verify the
  sha256** before anything is replaced; a mismatch aborts with the file
  untouched.
- **Per-OS swap.** On Windows a running exe can't be overwritten: rename-self →
  write the new binary at the original path → delete the renamed old copy on
  the next run. On Unix, write beside and `rename(2)` over, preserving the
  mode. Elevation is never attempted — if the target isn't writable, refuse and
  say so.
- Expect a freshly downloaded unsigned binary to meet SmartScreen / Gatekeeper
  / AV; document it rather than work around it.
- `--version <v>` selects a specific release, so **downgrade / rollback** falls
  out for free — the update check already treats an older binary as a
  legitimate, notice-worthy state.
- `--dry-run` resolves and verifies without swapping.
- **Acceptance:** a corrupted download never replaces the binary; a successful
  swap leaves a working `knit version` reporting the new version on the next
  invocation, on all three RIDs; the Windows leftover is cleaned on that next
  run; `--version` reaches an older release.

### Bundle refresh + SPA button

- **A successful swap runs `knit bundle install`** for the current repo, so the
  binary and the repo's copies of the skills + templates never diverge
  silently. Interactive by default; `--yes` carries through; skipped outside a
  knit repo.
- **Global skill bundles refresh too** — every bundle a bare `skill update`
  finds, inside a knit repo or not, each named. A machine running two agents
  off global bundles is the common case and would otherwise drift.
- **SPA:** the settings row grows an execute button, shown **only** when the
  host binary reports itself self-updatable (provenance above). A managed
  install shows the manager's command as text instead.
- **Acceptance:** the post-swap prompt appears once and honours `--yes`; an
  outdated global bundle is refreshed and named after the swap, also outside a
  knit repo; the SPA button is absent for a managed install and drives the
  swap for a self-updatable one, reporting progress and the final version.

### Open decisions

- **Code signing** (Authenticode / notarization) — removes the AV and
  Gatekeeper friction; costs money and release-job complexity. Decide before
  the first self-update ships: an unsigned auto-replacing binary is a worse
  experience than a manual download.
- **Whether to keep the replaced binary** for one release as a local rollback,
  or rely on `--version` re-downloading. Leaning on `--version` — a spare copy
  on disk is state to explain.

---

## "Edited" time from git, not the file

**Condition:** only if we decide to shell out to git — Knit has no git
shell-out today, and this would be the first.

The "edited" time is the file's mtime: the landing "recent" rows (items, KB,
designs), the detail footers, and `modified` in the CLI `--json`. git keeps no
mtimes, so a clone, pull, checkout, rebase or stash pop stamps every touched
file "now" — after a pull, a teammate's week-old edit reads "edited just now"
and floods the recent rows.

- **Clean tracked file → its last commit date; modified or untracked → mtime.**
  Your own unsaved work stays current, pulled work keeps its real time.
- **One `git log --name-only` pass** builds the path → date map, cached until
  HEAD moves (the file watcher already sees HEAD); never one `git log` per file.
- **Fallback to mtime** when git is missing, the repo isn't a git checkout, or
  the clone is shallow and the file's history is cut off.
- **Rejected:** an `updated:` frontmatter field written by every mutator (a
  merge conflict on every concurrent edit; hand edits and `git revert` skip
  it); relabelling mtime as "changed on this disk" (honest, but the recent
  rows stay flooded).
- **Acceptance:** after a fresh clone the recent rows order by commit date; a
  locally edited file shows its mtime; with git off PATH everything still
  renders from mtime.
