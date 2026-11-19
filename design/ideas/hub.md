# Knit hub (M2)

The M2 design: a server, REST API and web UI for people without git. Parked,
not planned — the client model is undecided (see below), and nothing starts
before that. The milestone itself is on the [roadmap](../roadmap.md).

**What M1 already covers.** The knit root redirect gives M1 a git-only shared
knit repo: several code repos or worktrees point at one `.knit/`, and people
sync through their git remote as usual. The hub is what that can't give:

- one authority that serializes writes, with no git merge in the user's path;
- people without git (PMs, designers) in a browser;
- server-side auth;
- cross-repo aggregation without every user holding a clone.

The hub is **self-hostable from the same `knit` binary**. Its storage is a
normal git repo in M1 shape: git history is the audit log, and disaster
recovery is a `git clone`.

Phases, each shippable:

- **M2a: hub server + CLI client.** REST API, optimistic concurrency,
  `knit hub serve/login/logout/status/migrate`, in-memory cache and batched
  commits on the server. CLI users get multi-repo. No web UI yet.
  - It also adds a `knit verify` class for an `assignee` that matches no
    handle in `users.json`. Warning severity, no relink repair.
- **M2b: hub-hosted web UI.** The M1c SPA, served by the hub. `knit web` opens
  the hub URL deep-linked to the current repo.
- **M2c: real-time + browser merge.** Sub-second updates and a browser 3-way
  merge. Build only when real users feel the gap.

## Client model — decision pending

> **⚠ Decision pending — client model.** Two incompatible client designs are
> on the table. Everything under [Shared hub design](#shared-hub-design) holds
> for both. Pick one before M2a code, then delete the other.
>
> | | [A: local mirror + sync](#client-variant-a-local-mirror--sync) | [B: remote-only, disposable cache](#client-variant-b-remote-only-disposable-cache) |
> |---|---|---|
> | `.knit/items/` on the client | gitignored, hand-editable mirror | none |
> | Offline | reads from the mirror; writes fail | only what the LRU happens to hold |
> | `## Links` clickable in GitHub / VS Code | yes, for cached targets | no; resolve via the web UI or `knit show` |
> | Agents `rg` across the tree | yes | after `knit export <dir>` |
> | Extra machinery | sync verb, etag/hash store, conflict files, bulk sync endpoints, mode-migration path rewrites | none beyond 412 handling |
> | Estimated M2a effort | ~4–6 person-weeks | ~2 person-weeks |
>
> Variant B's case: it cuts the highest-risk surface (sync correctness) and
> halves the test and doc matrix. It also doesn't foreclose A: if people run
> `knit export` constantly or really need offline edits, *that* is the signal
> to build the mirror, against real use cases.

## Shared hub design

### Deployment: `knit hub serve`

```text
knit hub serve --port 8080 --config /etc/knit/hub.json
```

- `--config` names a JSON file with the data repo path, OIDC/auth settings, the
  `repos.json` / `users.json` location, rate limits, CORS origins and (for
  topology B) remote-sync settings.
- It is Native AOT, the same publish target as the CLI. Lifecycle (systemd,
  Docker, Windows service) is not Knit's problem.

### Two hub topologies

Both use the same binary, REST API and CLI surface. They differ in *where the
data repo lives* and *how many hubs write to it*.

**Topology A: shared hub server.** One hub instance that everyone can reach.
It is the only writer to its data repo.

- Concurrency is hub-internal (a per-item in-process lock). A 412 means two
  clients hit the same item.
- Commits come from a bot identity with an `Acted-by: <handle>` trailer.
- Real-time across users is trivial, since one process holds the truth.
- It needs hosting: a laptop, a VPS or a container.

**Topology B: a personal hub against a shared git remote.** Each member runs
`knit hub serve` locally against their own clone of a shared remote (GitHub,
Gitea, GitLab). The hub commits with the **user's own git identity** and
pushes with their PAT.

```text
shared git remote (e.g. github.com/team/knit-data)
        ↑                       ↑
        | git push/pull, user PAT
   alice's laptop          bob's laptop
   knit hub serve          knit hub serve
        ↑                       ↑
   knit CLI / web          knit CLI / web
```

- No hosting cost: the git remote is the only shared piece.
- Honest commit identity, with no bot and no trailer.
- Every user needs push permission on the remote.
- Freshness across users is bounded by the fetch interval. Default to a
  `git ls-remote` peek every 5–10 s, which transfers no objects and makes most
  polls a no-op. The alternatives were weighed and rejected for v1:
  - webhooks can't reach localhost without a tunnel or relay;
  - a pub/sub bus like ntfy.sh adds a third-party dependency (revisit in M2c);
  - shared-drive watchers are fragile.

**Push-race handling (topology B).** When a push is rejected as
non-fast-forward, the hub:

1. fetches;
2. merges each file that both sides changed, in memory against the fetched
   HEAD:
   - **frontmatter** gets a field-level merge: different fields merge cleanly,
     and the same field with different values returns a 412 carrying the merged
     current state;
   - **comments** append by distinct comment id and always merge cleanly;
   - **body** gets a standard text merge, and a real conflict returns a 412
     with the conflict-markered file as `current_state`;
3. commits the result and retries the push.

Clients never see git-level merges. There is no `git merge` checkout.

`hub.json` declares the topology:

```json
{ "topology": "personal", "repo_path": "/home/alice/.knit/data",
  "remote": { "url": "https://github.com/team/knit-data.git", "branch": "main", "fetch_interval_seconds": 30 } }
```

Topology A drops `remote` and uses `"topology": "shared"`. In topology B,
`repo_path` is the user's *clone*; the real central is the remote.

| | A: shared | B: personal |
|---|---|---|
| Hosting | required | none (a git remote is enough) |
| Commit identity | bot + `Acted-by` | the user's own |
| Concurrency | hub-internal lock | git + in-hub 3-way merge |
| Freshness across users | instant | bounded by the fetch interval |
| Best for | larger teams, a shared web UI URL | small teams, OSS, teams with a git host |

### Server: in-memory cache + batched commits

- The hub keeps an in-memory index of items by id, refreshed by a file watcher
  on its data repo, so out-of-band edits (an admin running the CLI on the
  server) propagate.
- Commits are batched within a window (default 500 ms): a burst from one user
  lands as one commit. The API answers once in-memory state is updated and the
  commit is queued. A failed commit marks the items dirty and retries, and the
  files are still on disk.
- Writes are serialized per item. Writes to different items batch
  independently, with no global lock.
- On startup the hub rebuilds from the data repo, the same path as a cold
  cache.
- Search sits behind a server-side `ISearchIndex` (SQLite FTS5 first, Postgres
  later). This is the only DB in the system, and it lives only in the non-AOT
  hub.

### REST API

- Bearer PATs for the CLI and agents, and OIDC + a cookie session for humans
  in M2b. Hypermedia via Siren (RESTyard).
- Endpoints mirror the CLI: items (a since-cursor list, get with an ETag,
  create, patch, comments), KB the same way, plus repos and users.
- Every mutation carries `If-Match: <etag>`. The first writer wins; the second
  gets **412** with `current_state` and `your_attempt`.
- A per-write commit-message convention makes the server's `git log` read like
  a local-mode user's history.
- Rate limits and caching headers on reads. Errors follow
  [the error rules](../overview.md#error-reporting-no-silent-failures).
- M2b web UI: last writer wins on the body in v1. M2c adds a browser 3-way
  merge.

### Config split

- **`.knit/config.json` (committed):** `mode` (`local` | `hub`) and `hub_url`.
  Cloning the repo carries the binding.
- **User-level profile (never in git):** known hubs, and a PAT + handle per
  hub. With no token for the bound hub, the CLI prompts for `knit hub login`.

### Data repo layout and aggregation

- `repos.json`: the registry of code repos (name, url, prefix, default branch).
  The hub doesn't clone them; they are identifiers for cross-reference.
- `users.json`, items under `<repo>/` and `_general/`, and KB with the same
  split.
- Templates: the central default, with per-repo overrides resolved first.
- All items live in one repo, so cross-repo links are local relative paths.
  Reverse views are computed at read time.
- **KB follows items**: same storage shape, same API, same concurrency. The
  trade-off: an ADR amendment no longer rides in the same PR as the code it
  describes. The mitigation is cross-referencing both ways (`Refs: <pr-url>` in
  commits, refs in the entry).

### Hub commands

- `knit hub serve`
- `knit hub login <url>`: store the PAT in the user profile, and set `mode=hub`
  and `hub_url` in `.knit/config.json`.
- `knit hub logout [<url>]`: remove the credentials. The repo binding stays.
- `knit hub status`: connection, last refresh, and a warning when the repo is
  bound to a hub the user has no token for.
- `knit hub migrate --to <url> | local`: see below.
- `knit web` in hub mode opens the hub URL deep-linked to the cwd's repo.
  `--local` forces the local UI (read-only, and only meaningful in variant A).

### Migration: local → hub

1. Verify the hub is reachable and the token can write.
2. POST every item, then every KB entry. The hub creates the files and
   commits.
3. Verify counts and a sample of contents.
4. Switch `.knit/config.json` to `mode=hub` as the **atomic last step**. A
   failure before this leaves the repo in local mode.
5. Offer the `.gitignore` update.
6. **Local files are never deleted**, which keeps the migration reversible.

Idempotent, with `--dry-run`. When `.knit/` has tracked history, the CLI
*prints* the `git rm -r --cached` / `git filter-repo` options. It never runs
git itself.

`--to local` is the reverse: clone the hub's data into `.knit/`, switch the
mode back, and remove the gitignore entries.

## Client variant A: local mirror + sync

*Pending; see the [decision box](#client-model--decision-pending).*

The CLI keeps a gitignored mirror at `.knit/items/` (+ `.knit/.cache/`). In
hub mode `config.json` is the only tracked Knit file.

**Reads auto-refresh on every command.** A conditional GET goes first:

- 304 → read the cache;
- 200 → overwrite the cache file atomically, then read it;
- network failure → read the stale cache, with a stderr warning ("offline;
  last refresh 4 min ago").

Bulk reads use the since-cursor.

**Writes are synchronous and fail fast offline.** The hub returns the updated
item and the CLI overwrites its cache. A 412 shows a diff and prompts:
refresh and retry, abort, or view the current state. Offline writes fail with
no queueing.

**Cache consistency.** Whole-file atomic writes, and an ETag store at
`.knit/.cache/etag.json`. Losing `.cache/` must be a non-event, and
`knit refresh` rebuilds it from the hub.

**Hand-edit sync.** The mirror is not read-only: humans `vim` it and agents
`sed` it. Knit reconciles on request and never overwrites silently.

- Explicit verbs, never automatic:
  - `knit sync` (pull, then push);
  - `sync status`;
  - scoped `sync push|pull [<target>]`;
  - `sync resolve <id> --ours|--theirs|--merged`.
- Per file, the client tracks `etag_at_last_fetch` and `hash_at_last_fetch`,
  a hash over normalized LF text so cosmetic editor touches don't create
  phantom conflicts.
- Classification on sync:
  - clean + same → no-op;
  - clean + remote changed or deleted → pull or delete safely;
  - dirty + same → push;
  - dirty + remote changed or deleted → **conflict**, never overwritten.
- **Conflicts land on disk, git-style.** Next to the untouched `<id>-<slug>.md`
  go `<id>-<slug>.md.theirs` and `<id>-<slug>.md.base` (two-way if the base is
  unavailable).
  `.cache/conflicts.json` blocks further sync on those items until they are
  resolved. `--ours` / `--theirs` print what they discard and require `--yes`.
- Scope: items, KB and `users.json`. `config.json` and `.cache/` stay local.
- Bulk endpoints, to avoid a per-item request storm:
  - `GET /sync/state?since=<cursor>` returns metadata only, plus tombstones.
    The cursor is persisted only after a clean run.
  - `POST /sync/push` carries a per-entry `if_match` and answers
    `207 Multi-Status`.
- **Freshness nudge.** A global `hub_state_etag` comes back on
  `HEAD /sync/state`. Each command does one cheap HEAD. On a mismatch it prints
  one stderr line (`hub has changes — run 'knit sync'`) and never auto-syncs.

**Link-table tensions:**

- A link click in GitHub or VS Code resolves only when the target is cached,
  so `knit show` and the web UI pre-fetch first-degree link targets.
- Local paths are `../<id>-<slug>/`, while hub paths are
  `../<repo>/<id>-<slug>/`, so `knit hub migrate` rewrites the Target cells.

## Client variant B: remote-only, disposable cache

*Pending; see the [decision box](#client-model--decision-pending).*

The hub is the only source for hub-mode items, and the CLI is a thin REST
client.

- No `.knit/items/` on the client. No etag store, no `sync` verb, no conflict
  files.
- Reads hit the hub, optionally through a per-process memory cache or a tiny
  on-disk LRU (keyed by id + etag, about 100 entries, invalidated by the global
  ETag). Losing it is a non-event by design.
- Writes are synchronous with optimistic concurrency; a 412 prompts refresh
  and retry.
- Humans and agents work through `knit` subcommands and the web UI, not
  through grep across `.knit/items/`.
- The escape hatch: `knit export <dir>` dumps the hub state as a read-only
  markdown tree. `knit import <dir>` (later, if needed) replays it item by item
  through the same 412 path. There is no standing sync contract.
- **What B gives up:**
  - offline reads beyond the LRU;
  - clickable `## Links` in GitHub or VS Code for hub-mode repos;
  - agents `rg`-ing the tree without an export first.

## Costs of M2 (both variants)

- Running a real process: a single binary, but it has to be kept alive.
- Writes need a connection.
- Per-command HTTP latency.
- Two modes mean two test paths and two doc paths. Variant B halves that for
  item commands.
- M2b: ~2–3 person-weeks on top of M2a, reusing the M1c SPA.

## Open questions for M2

- Protected `main` on the data repo: commit signing for the bot account, push
  policy
- Body-edit conflicts on large diffs: resolution UX beyond conflict markers
- Soft-delete vs hard-delete for comments and items
- SSO onboarding (auto-created draft users), email aliasing in `users.json`
- Agent token issuance
- The concrete `ISearchIndex` interface
- Deferred-write queueing for offline clients (post-M2)


## Notes carried from M1

- **Search.** M1 dropped SQLite FTS5 because its native library broke the
  single AOT binary, and searches with a live in-memory scan behind
  `ISearcher` (entity-keyed, no reindex / readiness members). A hub index
  should implement that same seam, keyed by entity id. Open: the server
  section above calls the hub both Native AOT and "non-AOT" — settle which
  before picking SQLite / Postgres.
- **Hub-mode storage.** Items folder by code repo: `.knit/items/<repo>/…`, with
  `_general/` for items not tied to one; KB follows the same split. Until then
  a folder in `items/` is an invalid item (`knit verify`); M2 redefines that
  rule for its repo folders.
- **Identity.** A central `users.json` on the hub; PATs map to handles. Agents
  are first-class identities (`"type": "agent"`). SSO matches on the `email`
  claim. v1 provisioning is manual. M1 has no registry: `handle` / `display`
  come from config.
- **Assignee check.** `knit verify` gains a class for an `assignee` that
  matches no handle in `users.json` (warning, no relink repair). Deferred to M2
  because M1 has no registry, and a heuristic check without one is noise.
