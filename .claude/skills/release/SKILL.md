---
name: release
description: Cut a Knit release — propose the version from CHANGELOG.md, roll the changelog, regenerate THIRD-PARTY-NOTICES.md, run the release job's gates locally, commit and tag on main. Use when the user asks to release, cut / tag a version, or ship a prerelease (rc / beta).
---

# Release

CI publishes; this skill prepares the tag. A `v*` tag push runs the full check
matrix on the tagged commit, then the release job
([ci.yml](../../../.github/workflows/ci.yml)), which creates the GitHub release.
A red leg publishes nothing. Nothing is published from this machine.

## Rules

- Run exactly the git commands below; push only after the user confirms.
- Changelog entries are written with the change, not here (CLAUDE.md >
  Changelog entries). This skill only rolls `[Unreleased]` into the version.
- Stop on any failed gate: report it and what fixes it — a fix is a change of
  its own, never part of the release commit.
- Commit messages: no attribution lines.

## Stable or prerelease

Decide first, from the request and the current branch:

- The user named one (`v0.12.0`, "rc", "beta") → that one.
- Not on `main` → only a prerelease is possible (a stable release must be on
  `main`); say so and offer `vX.Y.Z-rc.N`.
- On `main`, nothing named → ask, recommending stable.

For a prerelease, `X.Y.Z` is the next stable version (stable steps 2–4) and `N`
is one above the highest existing `vX.Y.Z-rc.*` tag
(`git ls-remote --tags origin "vX.Y.Z-*"`), else 1.

## Stable release

1. **Preconditions** — on `main`, clean working tree, up to date:
   `git fetch origin --tags`, `git status -sb` (no ahead / behind).
2. **Last release** — `git describe --tags --abbrev=0 --match "v[0-9]*" --exclude "*-*"`
   (none yet: the first release is `v0.12.0`).
3. **`[Unreleased]` empty** → ask whether to release anyway; show
   `git log <last>..HEAD --oneline` so the user can spot a change that should
   have had an entry.
4. **Version** — proposed from the `[Unreleased]` categories, the user decides:

   | Categories present | Before 1.0 | From 1.0 |
   |---|---|---|
   | Removed, or a breaking entry | minor | major |
   | Added, Changed, Deprecated | minor | minor |
   | only Fixed / Security | patch | patch |

5. **Tag free** — `git tag -l vX.Y.Z` and `git ls-remote --tags origin vX.Y.Z`
   both print nothing.
6. **Roll the changelog**:
   - `## [Unreleased]` → `## [X.Y.Z] - YYYY-MM-DD` (today), a new empty
     `## [Unreleased]` above it
   - link references at the bottom:
     `[Unreleased]: https://github.com/MathiasReichardt/knit/compare/vX.Y.Z...HEAD`
     and `[X.Y.Z]: https://github.com/MathiasReichardt/knit/compare/v<last>...vX.Y.Z`
     (first release: `.../releases/tag/vX.Y.Z`)
7. **Gates** — the release job's, so a failure shows before a tag exists:
   ```bash
   npm ci --prefix src/Knit.Web
   dotnet restore
   node tools/gen-notices.mjs                     # regenerate
   node tools/gen-notices.mjs --check             # must pass now
   npm audit --prefix src/Knit.Web --omit=dev --audit-level=high
   dotnet list package --vulnerable --include-transitive
   ```
   The last one exits 0 either way: any "has the following vulnerable packages"
   is a stop.
8. **Commit and tag**:
   ```bash
   git add -- CHANGELOG.md THIRD-PARTY-NOTICES.md
   git commit -m "chore(release): vX.Y.Z"
   git tag -a vX.Y.Z -m "vX.Y.Z"
   ```
9. **Push** — after the user confirms; `--atomic` pushes both or neither:
   ```bash
   git push --atomic origin main vX.Y.Z
   ```
   The `main` ruleset refuses a direct push for anyone but the owner (bypass).
10. Point the user at the run: https://github.com/MathiasReichardt/knit/actions

## Prerelease

`vX.Y.Z-rc.N` / `-beta.N` on any pushed commit, any branch; no changelog
change, `X.Y.Z` is the version the next stable release will get.

1. The commit is pushed (`git status -sb`: not ahead of its upstream) and
   carries the release job — a branch without it tags nothing. Tag free
   (stable step 5); gates (stable step 7) on that commit. A stale
   notices file fails the release job — regenerate it in a commit on that
   branch first, via its PR.
2. `git tag -a vX.Y.Z-rc.N -m "vX.Y.Z-rc.N" <sha>`, push after the user
   confirms: `git push origin vX.Y.Z-rc.N`.

## When the release job fails

Nothing was published. A flaky leg: "Re-run failed jobs" on the run. A failure
that needs a code change: fix it via PR, then either release the next patch
version, or — since no release points at the tag — delete it
(`git push origin :refs/tags/vX.Y.Z`, owner bypass of the tag ruleset;
`git tag -d vX.Y.Z`) and tag the fixed `main` again: stable steps 5, 7, 9 and
the `git tag` line of step 8 — the changelog is already rolled.
