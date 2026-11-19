# Containerized test sandbox — idea

Status: **idea / not scheduled.** Worth it once the untested seed script or host-side agent scratch repos bite.

The design decisions below are kept as they were settled.

## Design

- Two problems, one primitive. (a) Agents create throwaway knit repos on the
  host and `rm` them again — containment is a promise, not a boundary. (b)
  Nothing executes `demo/seed-robot-factory.ps1`; it is only parse-checked, yet
  it is the first thing a new user runs. A disposable Linux container fixes
  both: agent scratch repos stop touching the host disk, and the same image
  gives the seed script a real end-to-end run.
- **Off-the-shelf image, no Dockerfile:** `mcr.microsoft.com/powershell:lts-debian-12`.
  pwsh is itself a .NET app, so the image already carries the native prereqs a
  self-contained .NET binary needs (libicu, libssl) — a bare `debian:slim`
  does not, and that surfaces as a confusing startup crash. M1 does not shell
  out to git, so nothing else needs installing.
- **Binary is not baked into the image.** Publish **self-contained, non-AOT**
  (`-r linux-x64`, seconds) and copy it in at run time
  (Testcontainers `WithResourceMapping`), so no image build happens per run and
  a source edit costs a publish rather than a rebuild. AOT stays a separate,
  occasional check — this task is about the seed path and a safe scratch repo,
  not trimming.
- **Scratch lives in the container's writable layer** (`/work`), not a volume
  and not a bind mount: throwaway is then the default, nothing lands on the host
  in any form, and evidence leaves only via a deliberate `docker cp`. Explicitly
  **not** a gitignored folder inside this repo — repo discovery walks *up* for
  `.knit/`, so a nested scratch repo would be discoverable from the source tree
  and break the "this repo does not use Knit on itself" invariant.
- **Copied in:** the published binary, `demo/seed-robot-factory.ps1` (invoked
  with `-Yes` so the destructive-clear prompt never blocks). Config is *not*
  copied — `knit init` writing it is part of what the test asserts.
- **Step 1 — investigate (do first):** whether `knit init` is interactive about
  the handle; if it is, the non-interactive path is itself a finding (an agent
  in a container cannot answer a prompt).
- **Route agents into it — the sandbox is worthless if nobody uses it.** Nothing
  about building the image changes an agent's default habit of creating scratch
  repos under `%TEMP%`. Ship a short CLAUDE.md rule in the same change: throwaway
  knit repos go in the container, never on the host filesystem, with the
  one-liner that starts it. Without this the task delivers a capability and
  changes no behaviour.
- **Acceptance:** an opt-in test (env var / `[Trait]`, so default `dotnet test`
  stays fast and Docker-free) starts the container, runs `init` → seed → asserts
  `verify` is clean and `status --json` / `list --json` match the counts the seed
  claims; it fails loudly rather than skipping when Docker is absent. CLAUDE.md
  carries the agent instruction + the invocation.
  Testcontainers.NET is MIT. Note for the operator: Docker Desktop is free only
  below the 250-employee / $10M-revenue threshold — Podman and Rancher Desktop
  both work with Testcontainers.
- Raised by an AI dogfooding run. Uses `knit web --host` to reach the UI from
  the host.
