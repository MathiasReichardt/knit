# Mounts

`mount` (bare: list), `mount add` / `set` / `remove`, each taking `--local` or
`--global`. Named read-only directories outside the repo root that KB file
sources may point into; values are repo-root-relative so a committed config
stays portable. A *mount* is not a *root*: one root holds knit state (read +
write), any number of mounts are view-only targets. The source-side workflow
is in the `knit-kb` skill.

- **Three config layers.** Without a flag the value goes into the committed
  `.knit/config.json` (shared with everyone). With `--local` it goes into
  `.knit/config.local.json` — uncommitted, gitignored by `knit init`, same
  schema, merged over the committed file key by key (`mounts` merge per
  name, so an override repoints one mount without restating the others).
  With `--global` it goes into the user-level config, for a directory every
  repo on the machine should see (one docs checkout used from several
  projects); that path must be absolute or `~`-anchored.
- **Shadowing is legitimate, and listed rather than warned about.** A repo
  mount overriding a global one of the same name is the point ("here, `docs`
  is the vendored copy"); `mount list` shows the overridden row so it is
  visible. A global mount missing on this machine only counts as a problem
  where a KB source actually rides on it.
- **A local value always wins when present**, with no disk probing: the
  effective path never depends on whether a directory happens to exist,
  so "why is my mount pointing there?" always has an answer. `mount list`
  names the file each value came from; `mount remove <name> --local`
  drops the override and hands the mount back to the committed value.
- Ask the user which layer they mean when it isn't obvious — committed is
  a change for the whole team, `--local` is only their machine.
- **Pass the real path, not the split form.** `kb add source --path` and
  `kb set <ref> path` take a path that leaves the repo root (`../Docs/x.pdf`,
  or absolute), find the mount covering it, and store
  `path: <rest> + mount: <name>` — the chosen mount is named on stderr.
  `--mount` is only needed to disambiguate overlapping mounts; a plain
  repo-relative path is stored verbatim. This is also how an entry written
  before mounts existed is adopted: `knit kb set <ref> path <real path>`.
- **A stored `path` is relative to its `mount`,** so it resolves against
  nothing on its own. `kb show` prints a `resolved` line (or the mount's
  reason instead), `--json` carries `resolved_path` / `mount_reason`, and
  `kb show <ref> --source-path` prints just the resolved path for
  `$(...)` — non-zero with the reason on stderr when the mount is broken.
  Don't reconstruct a path by reading `config.json`.
