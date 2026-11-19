# Contributing to Knit

How to build, test and find your way around the source. For using Knit, see
the [README](README.md).

## Build

Needs the .NET SDK pinned in [global.json](global.json) and Node (for the
web SPA).

```bash
dotnet test                                    # run tests
dotnet build                                    # debug build
dotnet publish src/Knit.Cli/Knit.Cli.csproj \
  -c Release -r <linux-x64|win-x64|osx-arm64>    # AOT single-binary
```

`dotnet build`, `run`, and `test` need no extra setup — AOT only
kicks in on `dotnet publish`. The first build runs `npm ci` and the Vite build
in `src/Knit.Web` and embeds the result; `dotnet clean` removes the SPA build
output but keeps `node_modules`. The `Knit.Web.Tests` bridge runs the SPA's
vitest suite, its eslint gate and the dependency license check.

For the web UI's development loop (HMR, standalone host, build details) see
[src/Knit.WebApi.Host/README.md](src/Knit.WebApi.Host/README.md).

### AOT on Windows

The AOT linker (`link.exe`) and Windows SDK ship with Visual Studio,
not the .NET SDK. One-time setup:

1. Install **VS 2022 Build Tools** with the **Desktop development with
   C++** workload:
   ```powershell
   winget install Microsoft.VisualStudio.2022.BuildTools `
     --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
   ```
2. Open a **Developer PowerShell for VS 2022** (Start menu) — this
   shell sets `PATH`, `INCLUDE`, `LIB` for the toolchain.
3. From that shell:
   ```powershell
   dotnet publish src/Knit.Cli/Knit.Cli.csproj -c Release -r win-x64
   ```

Plain PowerShell will fail with `LNK1104` or "linker not found" — the
dev shell is required because the env vars don't persist.

## Dependencies and notices

Only permissively licensed dependencies (MIT, Apache-2.0, BSD, ISC, Unlicense,
MPL-2.0); `dotnet test` fails on anything else, or on a license it can't
resolve — pin those in the `OVERRIDES` of `tools/gen-notices.mjs`.
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) is regenerated for each
release, so a dependency change doesn't touch it.

## Dogfooding: PM simulation

`demo/pm-simulation-driver.md` is a reusable brief for an autonomous,
AI-driven dogfooding run: a subagent role-plays a small team (PM +
devs) running a real project through the CLI under *consumer-only*
conditions (installed skills + `knit --help` only, no source access),
while an operator with repo insight validates the produced `.knit/`
against ground truth. Each run emits a `simulation-findings.md`
(missing-in-skill / gap / bug / wrong-behaviour). See the brief for how
to launch and what evidence a run must leave behind.

## Design docs

- [design/overview.md](design/overview.md) — thesis, principles, storage shape
- [design/decisions.md](design/decisions.md) — decisions in force, with why and what was rejected
- [design/roadmap.md](design/roadmap.md) — milestones
- [design/planned.md](design/planned.md) — committed work not yet started
- [design/ideas/](design/ideas/) — parked ideas, each with the open question that holds it back
- [design/techstack.md](design/techstack.md) — tech choices
- [design/test-strategy.md](design/test-strategy.md) — test layers and what not to build
- [src/Knit.WebApi.Host/README.md](src/Knit.WebApi.Host/README.md) — standalone web host (dev loop)
