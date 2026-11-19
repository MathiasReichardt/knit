# Installing Knit

One self-contained binary, no runtime to install. Take it from a release, or
build it from source (`dotnet publish`, see [CONTRIBUTING.md](CONTRIBUTING.md)).

| Platform | RID | Release asset |
|---|---|---|
| Windows x64 | `win-x64` | `knit-win-x64.zip` |
| Linux x64 | `linux-x64` | `knit-linux-x64.tar.gz` (glibc — see below) |
| Linux arm64 | `linux-arm64` | `knit-linux-arm64.tar.gz` (glibc — see below) |
| macOS Apple Silicon | `osx-arm64` | `knit-osx-arm64.tar.gz` |
| macOS Intel | `osx-x64` | `knit-osx-x64.tar.gz` |

Each archive holds `knit` (`knit.exe` on Windows), `LICENSE` and
`THIRD-PARTY-NOTICES.md`. `SHA256SUMS` beside them verifies a download:

```bash
sha256sum -c --ignore-missing SHA256SUMS    # Linux
shasum -a 256 knit-<rid>.tar.gz             # macOS: compare with its line in SHA256SUMS
```

```powershell
(Get-FileHash .\knit-win-x64.zip).Hash      # compare with its line in SHA256SUMS
```

## 1. Put the binary on your PATH

### Windows

```powershell
$dir = "$env:LOCALAPPDATA\Programs\Knit"
New-Item -ItemType Directory -Force $dir | Out-Null
Expand-Archive .\knit-win-x64.zip $dir -Force
[Environment]::SetEnvironmentVariable('Path', "$([Environment]::GetEnvironmentVariable('Path','User'));$dir", 'User')
```

Open a new shell afterwards — PATH changes don't reach running ones.

### macOS / Linux

```bash
mkdir -p ~/.local/bin                              # on PATH? add it if not
tar -xzf knit-<rid>.tar.gz -C ~/.local/bin knit    # <rid>: from the table above
xattr -d com.apple.quarantine ~/.local/bin/knit    # macOS only, see below
```

The archive keeps the executable bit. A bare binary copied or zipped from
Windows loses it — `chmod +x knit`.

#### Linux needs glibc

The Linux builds are linked against glibc, so musl-based distros — Alpine
above all — cannot run it. The missing ELF loader is reported as if the file
weren't there:

```
$ ./knit
/bin/sh: ./knit: not found          # or: exec ./knit: no such file or directory
```

The binary is fine; the loader it names isn't present. `gcompat` is not a
supported workaround. Use a glibc distro/image (`debian:stable-slim`, `ubuntu`,
`mcr.microsoft.com/dotnet/runtime-deps:10.0`), or build the musl binary
yourself: `dotnet publish src/Knit.Cli/Knit.Cli.csproj -c Release -r linux-musl-x64`
— a separate artifact, not a repack.

#### The macOS binary is unsigned

Knit has no Apple Developer ID signature and is not notarized (both need a paid
Apple account, so they wait for a real release pipeline). macOS tags anything
downloaded through a browser, Mail or AirDrop with a `com.apple.quarantine`
attribute; Gatekeeper then refuses to run an unsigned quarantined binary —
*"knit cannot be opened because the developer cannot be verified"*, or
`zsh: killed` on an Apple-Silicon build whose ad-hoc signature it rejects.

Three ways out, in order of preference:

```bash
xattr -d com.apple.quarantine ./knit   # drop the tag — only do this for a binary you built or trust
codesign -s - -f ./knit                # ad-hoc re-sign, if the copy across machines broke the signature
```

or once per binary: Finder → right-click → *Open* → *Open anyway* (also
reachable from System Settings → Privacy & Security after the first refusal).

A binary you copied over SSH/scp or built yourself is never quarantined — the
tag only comes from the download.

### Verify

`knit --version` prints the version. Keeping the binary next to a project
instead? Use a `.knit-bin` folder (gitignored), never `.knit`: that name marks
a knit repo.

## 2. Install the agent skills

The skill bundle ships inside the binary and documents the commands, workflows
and on-disk format for coding agents. Run it bare and it asks where to put it —
the folders of the agents it finds in the repo and home dir come pre-checked:

```
$ knit skill install
Install the Knit skills into which folders?
  [x] .claude/skills (Claude Code, OpenCode)
  [ ] .agents/skills (Codex, OpenCode)
Where?
> this repo — pins this version
  global — every repo
  custom path
```

Pick **global** for a normal install — agents see global skills in every repo.
Choose this repo only to pin a version for one repo, or a custom path for an
agent that reads skills from somewhere else.

Without the prompt, `--style` picks the folders (repeatable):

```bash
knit skill install --style agents --target global                  # Codex
knit skill install --style claude --style agents --target global   # Claude Code + Codex
```

If a global bundle already exists, a bare `skill install` doesn't re-ask the
location: it offers to use it as-is, update it to the running version, or pin a
copy in this repo.

Non-interactive (scripts, agents): `knit skill install --target global|project|<path>
[--style claude|agents] --non-interactive`. Without a `--target` it defaults to project scope — `global`
writes outside the repo, so it has to be asked for by name.

## 3. Update

Replace the binary, then refresh the skills — they version with the binary and
a stale bundle misleads agents silently:

```bash
knit skill update --check     # reports installed vs running, writes nothing
knit skill update             # refreshes every bundle it finds, one line each
```

A bare `skill update` covers repo and global, `.claude/skills` and
`.agents/skills`; `--global`, `--style` or `--target` narrow it. `update` always
overwrites, no prompt.

## 4. First repo

```bash
knit config set --global handle <you>         # once per machine — every repo inherits it
knit config set --global display "<Your Name>"
cd <your-repo>
knit init            # interactive: agents-file pointer, skills (identity inherited)
knit web             # local UI on loopback
```

Joining a repo that already uses Knit: skip `init`, just set your identity
as above. If `knit config list` shows `handle` coming from `config.json`,
the repo committed someone's identity — `knit verify` flags it; remove it
with `knit config unset --repo handle` and commit.

`knit init` checks the skill bundle: it offers to install globally or into the
repo when there is none, and says nothing when one is installed and current — so
step 2 above is optional if you start from `init`. A bundle older than the
binary is flagged with the `knit skill update` line to run. `init` also offers to
add a `## Knit` section to `CLAUDE.md` / `AGENTS.md` so agents discover the
tracker, and refreshes one that is outdated.

Non-interactively, `init` writes no skills and no pointer unless told: `--skills
global|project|skip` and `--agents-file project|skip` (default `skip`). Passed in
a terminal, they answer the prompt.

To let Claude Code run `knit` without a permission prompt per call, see the
README's "Allow `knit` without prompting".

## 5. Knit setups

Where a checkout keeps its knit state:

| Setup | Tracker lives in | Use when |
|---|---|---|
| **Local** | the repo's own `.knit/` | the default — one repo, one tracker |
| **Worktree** | the main checkout's `.knit/` | you work in git worktrees and every branch should see the same items |
| **External** | a separate knit repo | several project repos share one central tracker |

Worktree and external are redirects: `knit root` writes `knit_root` into
`.knit/config.local.json` — per machine, never committed — and every command
then works against the target's `.knit/`, settings included. `knit root` alone
shows the effective root and where it came from.

Only that mapping is per machine — `.knit/` itself is committed as usual, in
the repo that holds it:

| Setup | Items are committed in | The checkout's own `.knit/` |
|---|---|---|
| Worktree | the main checkout, on its branch — not the worktree's branch | the branch's copy of main's; left alone |
| External | the tracker repo | committed in the project repo: `config.json` with the requirement + hint, no items |

Keep that own `.knit/` on every branch — branch from main or merge it in, just
don't work in it. It is what tells a checkout of that branch which setup
applies: under external it carries the requirement, so any clone is asked for
the tracker path; under worktree, `knit root --worktree` refuses a checkout
without one.

### Local

```bash
cd <your-repo>
knit init
```

That is [4. First repo](#4-first-repo); commit `.knit/` with the code.

### Worktree

Once per new worktree:

```bash
git worktree add ../<repo>-feat-x feat-x
cd ../<repo>-feat-x
knit root --worktree      # point at the main checkout's .knit/
```

Until it is set, every command in the worktree prints a note. `knit root
--local` keeps the worktree on its own `.knit/` instead, on purpose. A branch
without `.knit/` should merge main first — a `knit init` there would conflict
with main's `config.json`.

**Bare repo** (every branch a worktree, no main checkout): `--worktree`
refuses and names the default branch's worktree. Point every other worktree at
it — `knit root ../main` — and keep that folder on its branch: the redirect
is a path.

Don't edit a redirected worktree's own `.knit/` files by hand: nothing reads
them, yet the edit is committed with the branch.

### External

The project repo commits only the requirement and a hint where to get the
tracker; the path to it stays per machine.

```bash
# once: create the tracker repo (if not existing)
git init tracker && cd tracker && knit init

# once: the project repo — commit the .knit/ it writes so it knows about the tracker
cd <project-repo>
knit init --external ../tracker --hint "clone <tracker-url> next to this repo"

# every other clone of the project repo
knit config setup                          # asks for the path
knit config setup --knit-root ../tracker   # non-interactive
```

Until a clone has run `config setup`, every command except `root`, `config`,
`mount` and `init` refuses and names the fix. A contributor without access to
the tracker runs `knit root --local` instead.

A project repo that already has its own `.knit/`: `knit root ../tracker`, plus
`knit config set requires.knit_root "<hint>"` so other clones are asked too.
Its existing items are hidden, not moved — move or re-create what belongs in
the tracker.

A redirect that can't be resolved — usually a share that isn't connected —
refuses every command; reconnect before reaching for `knit root --off`.

More in the README's "Shared and worktree roots".

## Uninstall

Delete the binary, remove its folder from PATH, and drop the `knit*` skill folders plus
`.knit-bundle.json` from `~/.claude/skills` / `~/.agents/skills`. Repo data lives in each repo's `.knit/` — deleting the tool
leaves it untouched.

## The skills are the manual

Once installed, the six `knit*` skills are the living documentation — they ship
inside the binary, so they always match the version you are running. Rather than
reading docs, ask your agent (the skill that answers in brackets):

- *"What is Knit, and which skill covers what?"* → `knit`
- *"How should I do issue management the Knit way?"* → item types, the
  project ⊃ milestone ⊃ slate ⊃ item planning layers, what belongs in an item
  versus the KB
- *"Set up a roadmap for this project"* → `knit-projects`
- *"Work the next item"* → `knit-work`
- *"Capture this decision"* → `knit-kb`
- *"Design this feature and scaffold the plan"* → `knit-design`
- *"Which flag does X need?"* → `knit-cli`, plus `knit <cmd> --help`

Keep them current (`knit skill update`) after every binary upgrade —
a stale bundle misleads an agent silently.
