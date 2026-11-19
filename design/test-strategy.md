# Knit — Test Strategy

What to test where, and — mostly — what **not** to build. The mechanics
(projects, fixtures, how to run the gate) live in [CLAUDE.md](../CLAUDE.md);
this doc is the reasoning behind them, which the code cannot carry.

## The layers

| Layer | What | Where | When |
|---|---|---|---|
| 1. Unit | Parsing, ids, slugs, ranks, frontmatter, sections, path containment | `Knit.Core.Tests` | every change |
| 2. Integration | Core + Data against a temp `.knit/` | `Knit.Core.Tests` | every change |
| 3. In-process CLI | `Program.Main(args)` with captured stdout/stderr; assert output *and* on-disk state | `Knit.Cli.Tests` | every change |
| 4. SPA | vitest specs, eslint, the third-party-notices check | `Knit.Web.Tests` (shells out to npm) | every change |
| 5. Out-of-process AOT smoke | the **published** binary against a temp `.knit/` | manual publish | before a release, after touching serialization / trimming / source-gen |

Layers 1–4 are the standing gate: one `dotnet test` runs all of them.

**Layer 3 is the workhorse.** It costs no process spawn yet exercises the
actual command parsing, exit codes, output formatting and error messages —
the user-visible surface, and where the large majority of real regressions
land. System.CommandLine is built for it: invoke with a `string[]` against a
captured console.

**Layer 5 exists because AOT fails in ways nothing else sees:** a
reflection-using dependency that wasn't obvious (silent serialization failure
or a startup crash), trimming removing runtime-needed code
(`MissingMethodException`), source-generator output diverging from what the
runtime expects, and cross-RID issues (path separators, line endings, Windows
file locking).

## What not to do

- **Don't mock the filesystem.** An `IFileSystem` abstraction is tempting and
  almost always net-negative for a tool this small. Use temp dirs.
- **Don't assert on `--help` output for surface coverage.** Test behaviour,
  not docs.
- **Don't write a custom test harness.** xUnit plus the temp-dir fixtures is
  plenty.
- **Don't add a second test framework.** xUnit is the only one in the
  solution; keep it that way unless a real gap appears.
- **Property-based (FsCheck) and snapshot (Verify) testing were considered and
  not adopted** — hand-written cases have covered the surface so far. Don't
  reach for either without a concrete need.

## Containers

Containerised testing is not a way to run the suite — it is how a test gets a
*disposable machine*. That earns its keep where a test must write outside the
repo or execute something end to end: the agent scratch sandbox and the
seed-script acceptance run
([ideas/test-sandbox-container.md](ideas/test-sandbox-container.md)), and later an M2 Gitea
container to test sync against a real git server and an M3 Postgres container
for the hub index. It buys nothing for layers 1–4, which are pure file IO into
a temp directory.

## Related

Console output rules — monochrome-safe glyphs, ANSI auto-strip, errors to
stderr, short-vs-full ids — are enforced invariants, not design topics: see
"Invariants worth remembering" in [CLAUDE.md](../CLAUDE.md).
