# Roadmap

Milestones only. Committed work that hasn't started is in
[planned.md](planned.md); parked ideas in [ideas/](ideas/README.md); why the
shipped parts are built the way they are in [decisions.md](decisions.md).

## M1: single repo, no server

| | Scope |
|---|---|
| ✅ M1a | CLI, items, links, verify / relink, agent skills, AOT binaries |
| ✅ M1b | Knowledge base (ADRs, docs, …) + design docs |
| ✅ M1c | `knit web`: loopback SPA, SSE live refresh, in-memory search |
| ✅ M1d/e | NanoID ids, due / priority / tags, slates, timeline, projects / milestones / todos, knit root redirect, conventions, share links, status report |
| 🔄 Release | First public release; then [update check and `knit update self`](planned.md) |

## M1f: agent steering (local)

Drive coding agents from the web UI, each run bound to a Knit item — the item
*is* the brief. Local and loopback-only: spawning processes is an RCE surface.
Needs a design pass before any code. Layers, each usable on its own: observe +
notify → trigger + kill → steer + review → multi-agent.
Full capture: [ideas/agent-steering.md](ideas/agent-steering.md).

## M2: knit hub

A server, REST API and web UI for people without git — what the M1 knit root
redirect can't give: one write authority with no git merge in the user's path,
a browser for non-git users, server-side auth, cross-repo aggregation without a
clone each. Self-hosted from the same binary; storage stays a git repo in M1
shape. Phases: M2a server + CLI client, M2b hub-hosted web UI, M2c real-time +
browser merge. Blocked on the client-model decision.
Design: [ideas/hub.md](ideas/hub.md).

## M3: notifications, integrations, fine-grained authz

Build only when real users feel the gap.

- **Notifications:** email / Slack on assignment, mention, a new blocker, or a
  state change on items the user owns. Hub-side, per-user preferences.
- **Fine-grained authz:** per-repo roles, token scopes, agent capability scopes.
  Needed once cross-team data shares a hub.
- **Multi-tenant hub:** one hub serving isolated teams.
