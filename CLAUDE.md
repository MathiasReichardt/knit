# Knit — repo guide for Claude

Knit is a git-native task tracker (markdown items in a git repo, single-binary
CLI, optional local web UI).

**Item domain model** — the canonical semantics of items (project / epic /
feature / bug / task / todo on a *value + altitude* axis, the planning layers
project ⊃ milestone ⊃ slate ⊃ item, the "items structure work, not truth"
purpose, frozen-on-close lifecycle) live in
[skills/knit/SKILL.md](skills/knit/SKILL.md) > Conventions > Glossary. That is
the single source of truth; consult it when shaping CLI / web / data behavior,
and don't duplicate the definition here.

**Status:** Milestones 1a (CLI) and 1c (local web UI) complete. CLI is
dogfoodable end-to-end, AOT publish verified in CI on win-x64,
linux-x64, linux-arm64, osx-arm64 and osx-x64. `knit web`
serves the React SPA in-process over loopback. The M1-polish wave added
NanoID ids, due dates, priority, tags, slates, the deadline timeline
(CLI + SPA), and projects/milestones/todos (unified tree). Open work is in
[design/planned.md](design/planned.md); milestones in
[design/roadmap.md](design/roadmap.md).

**CI** — [.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every
PR and push to `main`: `dotnet test` plus an AOT publish and `knit version`
smoke run for every RID. A `v*` tag push runs the same, then a release job
publishes the GitHub release. The solution-wide gate is `dotnet test`: the
`Knit.Web.Tests` bridge folds the SPA's vitest suite *and* its eslint gate
into "Run All Tests" (see `tests/Knit.Web.Tests` below). The version comes
from the latest `v*` tag via MinVer — there is no version number to bump.

## Design docs

All design docs live under [design/](design/).

- [design/overview.md](design/overview.md) — product thesis, goals /
  non-goals, standing principles (incl. the no-silent-fails error rules),
  storage shape. Not a surface reference — fields, commands and types live
  in the skills.
- [design/decisions.md](design/decisions.md) — decisions still in force,
  with why and what was rejected. A decision lands here when its work
  lands; an overturned one is replaced in place.
- [design/roadmap.md](design/roadmap.md) — milestones (M1 … M3), a few
  lines each.
- [design/planned.md](design/planned.md) — committed work not yet
  started (update check, `knit update self`), decisions inline until it
  lands.
- [design/test-strategy.md](design/test-strategy.md) — what to test at
  which layer and, mostly, what **not** to build: no filesystem mock, no
  second framework, no FsCheck / Verify, what containers are and aren't
  for. The mechanics stay here; that doc holds the reasoning.
- [design/ideas/](design/ideas/) — parked ideas: raised by a real gap,
  deliberately not taken, each with the open question that holds it back.
  Not a backlog — nothing here is committed. The M2 hub design is
  [ideas/hub.md](design/ideas/hub.md).
- [design/techstack.md](design/techstack.md) — chosen tech across
  **all** Knit applications. Covers the CLI and the local web UI
  (`knit web` host + React SPA, M1c); will grow to cover WebKnit (M3
  hosted server) as that is designed.

**Using them:**

- **Before changing behaviour**, check
  [decisions.md](design/decisions.md) for the area: an entry says why it
  is this way and what was already rejected. Going against one is a
  conscious call — raise it, don't silently undo it.
- **When a change settles a decision** (a real alternative was weighed),
  add or replace the entry in the same change — decision as title, one
  line of what, **Why**, **Rejected**. Only the why: the surface lives in
  the skills. No history: an overturned entry is replaced, not annotated.
- **Before starting planned work**, read its entry in
  [planned.md](design/planned.md); when it lands, its decisions move to
  `decisions.md` and the entry is deleted.
- **A new idea** that isn't taken goes to [ideas/](design/ideas/README.md)
  with the open question that holds it back; a milestone change updates
  [roadmap.md](design/roadmap.md).
- Code, comments and docs never cite plan sections, phase numbers or design
  labels (`(26.3)`, `Phase 24`, `D15`) — plans are deleted when done, so the
  reference dangles. A comment states the why itself; point to
  `decisions.md > <area>` only when the why doesn't fit in a line.

## Repo layout

- `src/Knit.Core` — domain (`Item`, enums, `Hierarchy`), parsing
  (`ItemFile`, `BodySections`, `IFrontmatterSerializer`), ids
  (`IdGenerator` / `NanoId`, `Slug`, `Rank`),
  templates, config, repo discovery. AOT-friendly; no Spectre /
  System.CommandLine deps.
- `src/Knit.Data` — repo storage + access + derived-data layer (not a
  cache-only "index": holds the authoritative store too). `ItemStore`
  (folder IO), `LinkGraph` (parent/children/blockers/ready, subtree), `RepoVerifier`
  (broken-target / title-h1 / hierarchy / prose scans + relink repair,
  shared by `knit verify`/`relink` and the web health endpoints).
- `src/Knit.Cli` — `Program.Main`, every command under `Commands/`,
  output helpers under `Output/`, `CliContext`, `IdShortener`,
  the embedded skill bundle (every `skills/**/*.md`). Hosts `knit web` (`WebCommand`) which
  runs `Knit.WebApi` in-process.
- `src/Knit.WebApi` — minimal-API library (read/mutate/search/kb/
  design/health endpoints + SSE), AOT-published with source-gen JSON
  (`ApiJsonContext`). Path-mapping endpoints guard via
  `Knit.Core.Paths.PathContainment`. Embeds the built SPA from
  `Knit.Web/dist/` via the `BuildSpa` MSBuild target.
- `src/Knit.WebApi.Host` — standalone host exe for the dev loop /
  debugging; the same library `knit web` hosts in-process.
- `src/Knit.Web` — React 19 + TypeScript SPA (Vite, Tailwind v4,
  TanStack Router/Query, Zustand, Zod). Unit-testable; specs are
  co-located `*.test.ts`.
- `tests/Knit.Core.Tests` — unit tests for domain / parsing /
  ids / sections / path containment.
- `tests/Knit.Cli.Tests` — in-process CLI smoke tests via
  `TempKnitRoot`; run sequentially in the `CliCollection` because cwd
  is process-global.
- `tests/Knit.Web.Tests` — bridge that shells out to `src/Knit.Web`'s
  npm scripts so a solution-wide "Run All Tests" / `dotnet test` covers
  the SPA too: one `[Fact]` runs `npm test` (vitest), a second runs
  `npm run lint` (eslint) — keep lint green or `dotnet test` goes red.
  A third runs `node tools/gen-notices.mjs --check-licenses`, so a
  dependency with an unresolved or non-permissive license fails the build.
  The SPA's own specs live
  in `src/Knit.Web/src/**/*.test.ts`. **The SPA is unit-testable** —
  new pure logic (`src/lib/`, `src/stores/`) should get a co-located
  `*.test.ts`; the vitest `include` glob picks it up automatically (no
  wiring) and the bridge runs it. Use jsdom-dependent tests only when
  the code touches the DOM (e.g. DOMPurify).

## Build & test

- `dotnet build` and `dotnet test` need only Node on PATH: the
  `BuildSpa` target runs `npm ci` (when `node_modules` is missing) and the
  Vite build, so a fresh clone builds the SPA and embeds it. `dotnet clean`
  removes `dist/` and `routeTree.gen.ts`, not `node_modules`.
- AOT publish on Windows needs the VS 2022 Developer PowerShell — see
  [CONTRIBUTING.md](CONTRIBUTING.md) > AOT on Windows. Don't try plain PowerShell;
  `link.exe` won't be on PATH.
- **Releases go through the `release` skill**
  ([.claude/skills/release/SKILL.md](.claude/skills/release/SKILL.md)):
  version, changelog, notices, local gates, tag. Pushing the `v*` tag is the
  release — CI builds and publishes, never a local machine.
- New CLI behavior tests go in `tests/Knit.Cli.Tests` with
  `[Collection(nameof(CliCollection))]` and use the `TempKnitRoot`
  fixture (auto-cleanup, captures stdout + stderr).
- **End-to-end dogfooding** beyond `dotnet test`:
  `demo/pm-simulation-driver.md` is a reusable brief for an autonomous
  AI run that role-plays a PM + devs driving a real project through the
  CLI under *consumer-only* conditions (installed skills only, no
  source), emitting a `simulation-findings.md`. Use it to shake out
  skill gaps / wrong-behaviour the unit suite can't. Runs against a
  throwaway repo, never this one.

## Changelog entries

[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/): `## [Unreleased]`,
then version / date headings; Added / Changed / Deprecated / Removed / Fixed /
Security. Written for someone deciding whether to upgrade, or diagnosing
behaviour after they did — the why and the how go in the commit, the design doc
or `decisions.md`.

- **Lead with the effect**, not the mechanism: what changed for a user, in the
  first clause.
- **1–3 lines.** More only where the reader must act (breaking change,
  migration, new required setting), never more than ~6. A new config key may
  spend more to list the keys and defaults.
- **Breaking changes** lead the entry: what stops working, and what to do.
- **Fixes**: the symptom the user saw, then the trigger — enough to tell "was I
  affected?", not the root cause.
- **One entry per user-visible change** — CLI, `--json` shape, on-disk format,
  `knit web`, the shipped skills, release assets. Refactors, tests, CI and
  internal renames get none.
- **Name only what the reader can touch** — commands, flags, config keys,
  `--json` keys, file formats. Not classes, methods or source paths.
- **No justification.** One clause of context is fine when it prevents a wrong
  assumption. If a line answers "how" or "why is that right", it belongs in the
  commit.

## Invariants worth remembering

- **Errors go to stderr**, not stdout. Use `Out.Error(msg, code, json)`
  — it picks the right stream and shape. Stdout must stay capture-clean
  for `$(knit ...)` substitution and pipes.
- **ANSI is auto-stripped** when stdout is redirected, `NO_COLOR` is
  set, `--no-color` is passed, or `--json` is requested. Wired in
  `Program.ConfigureAnsi`. Anything emitting styled output must go
  through `AnsiConsole` (not raw escapes).
- **CLI glyphs must be monochrome-safe.** For `tree`/`list`
  etc., pick symbols that inherit the terminal's default foreground
  (`•`, `◆`, `▸`, `✓`, `✗`, `◯`, `●`, box-drawing) and apply color
  deliberately via `AnsiConsole`. Never use intrinsically-colored emoji
  (🟢 🔴 ✅ ❌) — they stay colored even when ANSI is stripped, so a
  single item reads as if highlighted. (This is the CLI counterpart to
  the SPA lucide-react rule below.)
- **Short ids vs full ids**: human output uses `Out.Short(ctx, id)`
  (shortest-unique prefix, 4-char floor, sourced from item folder
  names via `IdShortener`). `--json` always emits the full uuid.
- **Mutator `--json` returns `JsonItem`** for every mutator (`add`,
  `start`, `done`, `block`, `assign`, `set`, `move`, `edit`, `link`,
  `unlink`) **except** `comment`, which returns `JsonCommentResult`
  (`{ item_id, comment: {...} }`) so the new comment id is directly
  accessible. A call that closes an item (`done`, `dismiss`, `set
  state=`) adds `blocked_dependents` — the open items still blocked by
  it; the key is absent on every other call.
- **`show --json` is hand-written** via `Utf8JsonWriter` in
  `Output/ShowJson.cs` because section keys are dynamic (one key per
  `## h2`, snake-cased). A leading `# heading` line is stripped — it
  duplicates the frontmatter `title`.
- **Item folders are the only item store.** There is no derived item
  index: every read (id shortening, link graph, filters, the web API)
  enumerates the folders, so nothing can go stale. The web server holds
  an in-memory `ItemCache`, validated against the files (mtime + size) on
  every request; doctor verify bypasses it. A leftover
  `.knit/index.jsonl` in an old repo is reported by `knit verify` as a
  stray file.
- **No file under `.knit/` may be silently hidden.** A product /
  acceptance rule: any authored file Knit can't load or address (bad
  id, a folder in `items/`, wrong design-name suffix, unknown KB bucket,
  malformed frontmatter, …) must be surfaced by `knit verify` + the
  SPA doctor view with a path + reason — never just skipped, or trust
  in the tool erodes. `RepoVerifier`'s `ScanItemFiles` (`invalid_items`)
  and `ScanStrayFiles` (`stray_files`) enforce this across `items/`,
  `designs/`, and `kb/`. Any new file type or enumeration path must
  keep the "nothing vanishes" guarantee.
- **A new config key lands with its `ConfigKeys` entry — including its
  scope.** `ConfigScope.Inheritable` means the user-level config
  (`UserPaths.ConfigDir`, `KNIT_CONFIG_HOME`) may carry it; `RepoOnly` means
  it describes *this* repo and a machine-wide value would be false everywhere
  else; `Personal` (identity) is inheritable, and a write naming no layer goes
  to the uncommitted `config.local.json`. The parameter has no default, so a new key that ignores the question
  fails to compile. `KnitConfig` is the
  typed record; `Knit.Core/Config/ConfigKeys.cs` carries the metadata the
  record can't express (description, rendering, parsing/validation, whether
  `knit config set` may touch it). A key without an entry is invisible to
  `knit config list` / `get` / `set` and to the SPA settings view —
  `ConfigKeyTableTests` fails on it. Config is read through the non-null
  `ConfigLoader.Load`; absent means defaults, unreadable is surfaced.
- **`knit pick` needs stderr to be a TTY**. Prompt UI is rendered on a
  separate stderr-pointed `AnsiConsole`; stdout receives only the
  chosen full uuid so `$(knit pick ...)` substitution works.
- **SPA icons** (`src/Knit.Web`). Use [`lucide-react`](https://lucide.dev)
  for all glyphs — don't introduce a second icon set, don't fall back
  to unicode symbols. Icon-only buttons (no visible text label) must
  always carry both a `title=` tooltip and an `aria-label` — the
  tooltip surfaces meaning on hover, the aria-label is what assistive
  tech reads. Buttons that already show a text label don't need the
  tooltip.
- **No commands in SPA tooltips.** A native `title` can't be selected or
  copied. Explanation may live there; a command the user is meant to run goes
  in a click popover (`InlinePopover`) as code with a `CopyCommandIconButton`.
- **[SECURITY.md](SECURITY.md) is a public contract.** Any change to what it
  claims — outbound network calls, `knit web` bind / host checks / cross-site
  write guard, CSP, markdown sanitization, Mermaid mode, path containment —
  updates it in the same change, including its in-scope / out-of-scope lists.

## Conventions in design docs

- Markdown linked file references use `[text](path)` form.
- YAML is used **only** for item frontmatter. All config files are JSON
  (`config.json`, `config.local.json`, the user-level `config.json`).
- Milestones are valid prefixes of each other — the on-disk format
  never changes between M1a and M3.
- "M1a" = CLI only, no web, no git shell-out, single repo.

## Working style

- Keep edits surgical. Preserve the existing terse-but-opinionated
  tone in docs; default to no comments in code (well-named identifiers
  carry the meaning).
- When adding a section, link it from the right neighbor.
- Push back on additions that don't earn their place — both design and
  code already explicitly trim scope.
- No backwards-compat shims, no half-finished implementations, no
  abstractions for hypothetical future requirements. This is a single
  repo at M1a; rename/refactor freely.
- For non-obvious "why" — past incident, hidden constraint, framework
  gotcha — one short comment is welcome. Don't narrate what the code
  does.
- **Library licensing & popularity.** Only pull in libraries with
  permissive licenses (MIT, Apache-2.0, BSD, ISC, Unlicense, MPL-2.0).
  Reject GPL / LGPL / AGPL / SSPL / BUSL and source-available
  licenses. Prefer well-known, widely-used libraries with active
  maintenance. If you can't find a good permissive option — or the
  best fit is obscure — ask the user before adding it instead of
  silently picking the closest match.
- **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) is regenerated per
  release, not per dependency change** — `node tools/gen-notices.mjs` (NuGet +
  npm), embedded in the binary (`knit licenses`); the release job refuses a
  stale file. A dependency change doesn't touch it, but a dual license or
  missing license field goes into the script's `OVERRIDES` in the same change
  (`dotnet test` fails until then). A dependency that ships a `NOTICE` file
  must be reproduced (Apache-2.0 §4(d)).
- **Examples and fixtures use invented, neutral names** — in tests, skills,
  docs and design examples alike. Never a real customer, employer, colleague,
  internal library or private skill, and no real domain specifics (spec titles,
  protocols, market roles) — not even when a bug report supplies them; restate
  the repro with neutral values. Reuse the existing sets: alice/bob/carol,
  acme, BotCo, shop / Orders / Billing.
- **`skills/` is the shipped bundle, not config for this repo.** The
  six skills (`skills/knit/`, `skills/knit-cli/`, `skills/knit-kb/`,
  `skills/knit-design/`, `skills/knit-work/`, `skills/knit-projects/`)
  are what `knit skill
  install` writes into *consumer* repos. They document the full
  intended product surface, which runs **ahead** of the current CLI —
  so a skill describing a command the CLI doesn't implement yet is
  expected, not a bug. **This repo does not use Knit on itself:** there
  is no `.knit/` here, design docs live in `design/`, and the documented
  `knit ...` commands are not meant to be run against this repo.
- **Every user-visible change adds its [CHANGELOG.md](CHANGELOG.md) entry
  in the same change**, under `## [Unreleased]` — a change without one is
  unfinished, like one without its skill update. Written now, while the
  context is at hand; the `release` skill only turns the section into the
  version's and writes no entries. What counts and how to write it:
  [Changelog entries](#changelog-entries).
- **Keep skills in sync with the CLI.** The skill bundle above is the
  agent-facing contract for the CLI. Any change to command behaviour,
  flags, output shape, gotchas, or workflow assumptions must be
  reflected in the relevant skill in the same change. `knit-cli` owns
  command-level idioms and gotchas; the workflow skills own
  task-level guidance. Stale skills mislead agents silently — treat
  them as part of the public surface, not docs.
- **Skill edits are brief and forward-looking.** The bundle is embedded
  in the binary and read into every agent's context, so size is a cost
  paid on every run. Write the behaviour as it is now: never a changelog,
  never "this used to…", never a past bug or its fix. If a sentence only
  makes sense to someone who knew the old behaviour, cut it.
- **Skill reference files.** A skill's `SKILL.md` holds what every run needs;
  detail an agent looks up goes in `<skill>/references/*.md` (the Agent Skills
  layout), linked from `SKILL.md`, one hop deep — reference files link nothing.
  Other skills link the `SKILL.md` and name the area (`knit-cli` skill > Timeline),
  never a reference file. New CLI surface lands in the matching `knit-cli`
  reference file, not the core. `skill install` ships every file under
  `skills/`; `SkillInstallBundleTests` fails on a dangling link.
- **Skills cover the CLI only.** The web UI (SPA) is for humans: no
  instructions or workflows about it in the skills — agents drive Knit
  through the CLI and `--json`. A one-line mention is fine, so an agent
  can point a human (e.g. a new user) to `knit web`.
