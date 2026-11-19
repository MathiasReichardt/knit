# Tech Stack for KNIT

## CLI tool

To build a professional, **Native AOT**-compatible, **single-binary** tool that features both shell completion and rich UI components, use the following architectural pattern:

### 1. The Engine: `System.CommandLine` (v2.0+)

Use the **Source Generator** version of this library to handle command-line parsing.

* **AOT Role:** Unlike traditional reflection-based parsers, the source generator creates the parsing logic at compile-time, making it fully compatible with Native AOT and ensuring near-instant startup times.
* **Shell Completion:** It provides the infrastructure for "dotnet-suggest" hooks, allowing users to tab-complete commands and options in Bash, Zsh, and PowerShell.

### 2. The Interior: `Spectre.Console`

Use this for all terminal output and user interaction once the command is running.

* **AOT Role:** It is "Trim-Safe," meaning the compiler won't delete the code that renders your UI during the optimization phase.
* **Features:** Use this to render the **tables, progress bars, and trees** you requested. For streaming agentic output, use `AnsiConsole.Live()`.

### 3. The Delivery: Native AOT

Configure your `.csproj` for a self-contained, single-file distribution:

* **Single Binary:** Set `<PublishAot>true</PublishAot>` and `<PublishSingleFile>true</PublishSingleFile>`.
* **Zero Dependency:** The final output is a machine-code binary (e.g., `knit.exe` or `knit`). The user does **not** need the .NET SDK or Runtime installed.
* **Cross-Platform:** You must run the `publish` command for each target (win-x64, linux-x64, osx-arm64) to generate the specific binary for that OS.

---

### Implementation Summary

1.**Define** commands and arguments using `System.CommandLine` attributes/Source Generators.
2.**Invoke** `Spectre.Console` widgets inside the command handlers for dynamic UI.
3.**Publish** using the `dotnet publish -r [RID] -c Release` command to bake everything into a single, high-performance file.

---

## Local web UI (M1c)

`knit web` serves a read/write web UI for a single repo, bound to
`127.0.0.1` only (loopback — never exposed on the network). It is a
.NET minimal-API backend that embeds a React SPA, so the whole thing
ships inside the same single binary as the CLI.

### Backend: ASP.NET Core minimal API

* **`Knit.WebApi`** — a library of minimal-API endpoints over the same
  `Knit.Core` / `Knit.Data` layer the CLI uses (read, mutate, search,
  KB, design, health, plus a file-view endpoint). No new domain logic
  lives here — it is a thin HTTP surface over the existing use cases.
* **AOT + source-gen JSON.** All request/response DTOs are registered
  in a single `JsonSerializerContext` (`ApiJsonContext`) so the API
  serialises without reflection and stays Native-AOT-compatible, like
  the CLI. New DTOs **must** be added to that context.
* **Live refresh via SSE.** A file watcher pushes invalidation events
  over Server-Sent Events (`/api/events`); the SPA uses them to refetch.
  `--no-watch` disables both.
* **Two hosts, one library:**
  * `knit web` (in `Knit.Cli`) hosts `Knit.WebApi` in-process — the
    shipping entry point.
  * `Knit.WebApi.Host` is a standalone exe for the dev loop /
    debugging (F5, HMR against a separate Vite server).
* **Path safety.** Endpoints that map a request-supplied path onto the
  filesystem (file view, KB folder) guard through
  `Knit.Core.Paths.PathContainment` — a lexical `..` containment check
  plus a strict reject of any symlink/junction below the repo root.

### Frontend: React SPA (Vite)

Built with Vite and bundled into `Knit.WebApi` at compile time (the
`BuildSpa` MSBuild target runs `npm ci` + `npm run build` into
`Knit.Web/dist/`, then embeds it), so the published binary needs no
external web assets.

| Concern | Library |
|---|---|
| UI runtime | **React 19** + **TypeScript** |
| Build / dev server | **Vite** (HMR in dev, static bundle in prod) |
| Styling | **Tailwind CSS v4** (`@tailwindcss/vite`) + `@tailwindcss/typography` |
| Routing | **TanStack Router** |
| Server state / caching | **TanStack Query** |
| Client state | **Zustand** |
| Schema validation | **Zod** (validates API payloads at the boundary) |
| Icons | **lucide-react** (single icon set — no unicode-symbol fallbacks) |
| Markdown render | **marked** + **DOMPurify** (sanitised) |
| Diagrams | **mermaid** |
| Code / editor | **CodeMirror 6** (language packs per file type) |
| Drag & drop | **@dnd-kit** (rank reordering) |
| Unit tests | **vitest** (+ jsdom only where the DOM is touched) |
| Lint | **eslint** + `typescript-eslint` |

The SPA is unit-testable: pure logic under `src/lib/` and `src/stores/`
gets co-located `*.test.ts`. The `Knit.Web.Tests` .NET bridge folds
both the vitest suite and the eslint gate into solution-wide
`dotnet test`, so a red lint or spec fails "Run All Tests".

### CLI ↔ SPA parity note

The web UI mirrors the CLI's capabilities, but one default differs by
design: `list --sort` defaults to `rank:asc` on the CLI, while the
SPA's list mode defaults to `tree:asc` (per the design's filters &
sorting parity matrix). Both surfaces expose the same sort keys; only
the unspecified default differs.