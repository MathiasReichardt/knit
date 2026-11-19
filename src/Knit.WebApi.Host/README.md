# Knit.WebApi.Host

Standalone executable that hosts the Knit web API + embedded SPA.
The same `Knit.WebApi` library is also hosted in-process by
`knit web`; this binary exists for development and debugging.

## Run

```
dotnet run --project src/Knit.WebApi.Host -- --root <path> --port 5174
```

Then open `http://127.0.0.1:5174`. The embedded SPA is served from
`Knit.Web/dist/` (bundled into the assembly at build time).

Optional: pass `--config <path>` to load `rootPath` / `port` /
`openBrowser` from a JSON file. See [`webapi.config.sample.json`](webapi.config.sample.json)
for the shape. Without `--config`, the file is ignored.

## Prerequisites

- .NET 10 SDK.
- **Node + npm in PATH.** The SPA in `src/Knit.Web/` is built into
  the host assembly via an MSBuild target (`BuildSpa` in
  `Knit.WebApi.csproj`). First build runs `npm ci` + `npm run
  build`; incremental builds skip both when SPA sources haven't
  changed.

## Dev loop

**.NET-only changes:** F5 / `dotnet run` on this project. The
MSBuild `BuildSpa` target keeps the embedded SPA fresh
automatically (incremental — fast when SPA hasn't changed).

**SPA changes with HMR:** run Vite separately for hot reload.

```
# terminal 1
dotnet run --project src/Knit.WebApi.Host -- --root <path> --port 5174

# terminal 2
cd src/Knit.Web && npm run dev      # Vite dev server on 5173
```

Open `http://127.0.0.1:5173`. Vite proxies `/api/*` and `/sse` to
the .NET host on `5174` (see `src/Knit.Web/vite.config.ts`).

To stop the .NET build from rebuilding Vite output while you're
using HMR, create `Directory.Build.user.props` at the repo root
(gitignored) with:

```xml
<Project>
  <PropertyGroup>
    <SkipNpmBuild>true</SkipNpmBuild>
  </PropertyGroup>
</Project>
```

## Production binary

`dotnet publish -c Release -r win-x64` (or `linux-x64`, `osx-arm64`)
produces a single-file AOT binary with the SPA embedded. No
`wwwroot/` is needed on disk at runtime.

The repo-root `Directory.Build.targets` checks the published
binary size and **warns at 35 MB / fails at 50 MB** per the design
budget.
