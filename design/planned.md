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

**Progress** — paused after the check job; delete this block when the entry lands.

- Done (`ci.yml`, `Directory.Build.props`):
  - check job: the five-RID matrix, the full suite on four RIDs, AOT publish +
    `knit version` smoke on all five, `workflow_dispatch` with an RID choice
  - MinVer versioning, as specified below
  - NuGet lock files + audit
  - `artifacts/` output folder, gitignored
  - notices: `tools/gen-notices.mjs` (renamed from `gen-npm-notices.mjs`) covers
    NuGet + npm; `--check-licenses` in `Knit.Web.Tests`, `--check` ready for the
    release job; CLAUDE.md / CONTRIBUTING.md rule replaced
  - README > Install and `INSTALL.md`: all five RIDs, the per-RID archives
  - `release` skill (`.claude/skills/release/`), `CHANGELOG.md`, the CLAUDE.md
    mention
  - rulesets: `main` requires a PR and the five `check (<rid>)` jobs, owner
    bypass; `v*` tags can't be created, moved or deleted but by the owner
  - README badges: CI (`main`), latest release (stable only)
- Open:
  - release job on `v*` tags: written (tag validation in the `matrix` job,
    version assert in smoke, notices `--check`, changelog notes, `gh release
    create`); dry run with `v0.12.0-rc.N` tags (deleted afterwards) not yet done
  - when the entry lands: decisions to `decisions.md`, the entry deleted
  - before `v0.12.0`: the release job's `npm audit` fails today (seroval
    critical, zod high; both have non-major fixes) — its own PR
  - first release `v0.12.0` from `main`

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
  - The full suite (incl. `Knit.Web.Tests`: vitest, eslint, license check) runs
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
  `StripSymbols`. `OptimizationPreference` stays at the default — a CLI's
  runtime is I/O and parsing, so `Speed` would cost size for no measurable gain.
- **Runner needs:** Node on every runner (`BuildSpa` runs `npm ci` + Vite);
  checkout with `fetch-depth: 0` for MinVer. The AOT publish is what enforces
  `IlcTreatWarningsAsErrors`, incl. warnings from dependencies.
- **Release assets: one archive per RID** — `knit-<rid>.tar.gz` (`knit-win-x64.zip`
  on Windows) holding `knit` / `knit.exe`, `LICENSE` and `THIRD-PARTY-NOTICES.md`,
  flat. The command is `knit` on every OS, and tar keeps the executable bit.
  A `SHA256SUMS` file beside them — what `knit update self` verifies a download
  against.
  Version-less names, so `/releases/latest/download/<asset>` is a stable link.
  - **Rejected:** bare binaries named `knit-<rid>` (every user renames them, and
    a download loses the executable bit); the version in the asset name (no
    stable latest link).
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
- **A `release` skill** owns the flow: version proposal, changelog roll,
  notices, tag. Before tagging it runs the release job's gates locally
  (`gen-notices.mjs --check` after regenerating, `npm audit --omit=dev
  --audit-level=high`, `dotnet list package --vulnerable --include-transitive`),
  so a failure shows before a tag exists.
  - The version is proposed from the `[Unreleased]` categories (Removed /
    breaking → minor before 1.0, major after; Added / Changed → minor; only
    Fixed / Security → patch); the user decides. Empty `[Unreleased]` → ask.
  - One run, on `main`: the release commit (changelog roll + notices) and its
    tag are pushed together (`--atomic`), the owner bypassing the `main`
    ruleset. The tag run tests that commit on every RID before publishing.
    A prerelease tags any pushed commit, no release commit.
  - No `gh`: no CI-status check before tagging — a red tag run publishes
    nothing, and an unpublished tag may be deleted.
  - **Rejected:** a `release/vX.Y.Z` PR for the release commit (a second run
    after the merge, and a squash merge changes the commit to tag).
  - A project skill under `.claude/skills/`, not in `skills/` — that is the
    bundle shipped to consumers.
- **Vulnerability gates:** NuGet audit fails high / critical advisories on every
  CI build; `npm audit` (production dependencies, high+) runs in the release job
  only.
  - **Rejected:** `npm audit` per PR — an advisory published overnight turns
    every unrelated PR red; Dependabot security updates cover the in-between.
- **`THIRD-PARTY-NOTICES.md` is regenerated per release, not per dependency
  change.** A bump (Dependabot security update, hand bump) no longer has to
  touch it, so it no longer fails `dotnet test`.
  - The generator covers both halves: npm as today, and NuGet from the shipped
    projects' `packages.lock.json` (resolved closure) plus each package's
    `.nuspec` in the NuGet cache (license expression, copyright, project URL).
    An unresolved or non-permissive license fails it, as on the npm side;
    gaps get an `OVERRIDES` entry.
  - The `release` skill runs it before tagging and commits the result with the
    changelog; the release job runs `--check` and refuses a stale file, so every
    published binary embeds the text of its own dependencies.
  - The check splits in two:
    - `--check-licenses` stays in `Knit.Web.Tests`: every npm and NuGet
      dependency resolves to a permissive license, without comparing the file —
      so a bad license still fails its PR, and a plain bump passes.
    - `--check` (file matches the regenerated output) runs only in the release
      job.
  - Replaces the "a dependency change updates THIRD-PARTY-NOTICES.md in the
    same change" rule in CLAUDE.md and CONTRIBUTING.md.
  - **Rejected:** regenerating per PR (every patch bump needs a manual commit;
    Dependabot can't run the script); generating at release without committing
    (the repo's file would drift from what ships).
- **`CHANGELOG.md`** per Keep a Changelog, starting with the first release.
  Entries are written with the change, under `[Unreleased]` — its author has
  the context; the release only rolls the section. CLAUDE.md names it and the
  `release` skill, replacing the "remind to tag after a version bump" note.
- Every pipeline output goes to one gitignored `artifacts/` folder.
- **Repo rules once the check job is green** (rulesets; `main` already blocks
  force pushes and deletion):
  - `main` requires the check job to pass. A check only exists after a push, so
    this means changes land through a pull request — the PR is the vehicle for
    the check, not for review. The owner may bypass — used only for the
    release commit.
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

---

## Quote refs: point an agent at selected text

Select text in a rendered doc (item, KB, design, project doc), copy a ref to
it, paste it into an agent chat ("discuss this"). The section link's
counterpart for a passage. Pasted into the jump box, it opens the doc and
highlights the passage.

Not a link format for stored text: a quote ref lives as long as the chat, so
it is never written into a doc, never checked by `knit verify`, and nothing
needs to keep it valid.

### Format

```text
kb:doc/auth#limits "⟦tokens expire after 1 h⟧"
kb:doc/auth#limits "retry after ⟦5⟧ seconds, then"
item:<id>#matrix "bob dev ⟦yes⟧" (column "Admin")
```

- **The section ref, unchanged, then the quote** — the same `<ref>#<anchor>`
  the heading button copies, so an agent reads the section with
  `knit ref show` and finds the passage in it.
- **`⟦…⟧` marks the selection, always present**; text around it is context.
  Never in real text, so no escaping, and it tells a quote ref apart from the
  cited form `` `item:<id>` "Title" ``.
- **No `>` separator:** an agent pasting the ref into a shell command would
  redirect into a file. Here a pasted quote only adds stray arguments, which
  the CLI rejects.
- **The quote is the rendered text** the user saw, whitespace collapsed to
  single spaces, on one line — inputs drop newlines on paste. Not raw
  markdown: the highlight matches rendered text, and the agent reads the
  section anyway.
- **Scope = the section's span** (heading plus subsections, as `knit ref
  show` prints it): the deepest h2/h3 whose span holds the whole selection.
  None (above the first heading, or across two h2s) → the ref without an
  anchor, scope the whole doc.
- **Long selection** (over ~120 characters): `⟦first words … last words⟧`;
  matched as the start, then the nearest end after it.
- **Rejected:** URL text fragments (`#:~:text=`) — browsers strip them from
  `location.hash`, the SPA would need a second URL form, and percent-encoding
  is unreadable in chat; `path:line` ranges — the SPA renders without source
  positions, lines drift during the discussion, and a list or table is one
  token, so the range is coarse.

### Uniqueness — never take the first match

- **At copy time** the SPA counts the selection in the scope. Unique → bare
  `⟦…⟧`. Not unique → add a word of context on either side until it is, up
  to ~10 words. Still ambiguous (identical table rows) → copy anyway; the
  jump reports it.
- **Column name:** a selection inside one table cell appends
  `(column "<header>")` — positional context pins the cell, the header tells
  the agent what the value means. Parse the column form first, so a `"` in
  the quote can't swallow it.
- **At jump time**, matches of context + selection in the scope:

  | Matches | Behaviour |
  |---|---|
  | 1 | Highlight exactly the `⟦…⟧` part |
  | 0 | Flash the section, toast "quoted text not found" |
  | > 1 | Highlight every match, toast "N matches — ambiguous" |

### Matching

- **Whitespace-insensitive**, plus the existing case / diacritics fold:
  quote and rendered text are compared with all whitespace removed. That is
  what makes tables (cells separated by tabs / newlines in the selection,
  separate text nodes in the DOM) and lists (bullets not in the selection)
  match without per-element handling.
- An explicit normalised-offset → text-node map turns a match back into a
  `Range`; `searchHighlight` paints with the CSS Highlight API and relies on a
  length-preserving fold, which whitespace removal breaks — extend it, don't
  build a second highlighter.
- Where the Highlight API can't paint (SVG / Mermaid text, older engines):
  the existing region flash.

### Surface

- **Copy:** a floating icon button at a non-empty selection inside a rendered
  doc body (read mode only; the editor has no rendered DOM), lucide icon with
  `title` + `aria-label`, toast on copy.
- **Jump box:** accepts the quote ref; the part before the quote takes the
  existing ref path unchanged.
- **Skill:** one sentence in the `knit` skill > Sections: what the form means
  and that `knit ref show <ref>#<anchor>` reads the passage's section. The CLI
  itself doesn't parse it.
- **Acceptance:** vitest over a shared case set: a unique phrase in prose; a
  repeated phrase gets context and resolves to the selected occurrence; a
  table cell resolves to the selected column and carries its header; a
  selection across list items and across table rows; text above the first
  heading; a long selection; quote not found → section flash + toast; two
  identical rows → all highlighted + toast.
