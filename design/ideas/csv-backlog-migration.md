# CSV backlog migration — idea

Status: **idea / not scheduled.** Waits until a team actually wants to bring an existing backlog in.

The design decisions below are kept as they were settled.

## Design

- **IM1. A standalone markdown doc that *is* the agent prompt — not a skill,
  not a command.** A one-time migration earns neither a permanent slot in the
  always-indexed `/knit-*` roster nor a parser maintained forever; the agent
  supplies the judgment (type/state mapping, hierarchy, HTML → markdown) a
  parser would hard-code. The source vendor is named only in that doc.
- **IM2. Not repeatable** — re-running double-creates. A `knit import csv`
  with `import_id` round-trips waits for a real repeatable-sync need.
- **Recipe (public CLI only):** per row `knit add` under the resolved parent,
  `knit set` for the mapped fields, `knit link` for blocker / related edges;
  then a per-row created / skipped summary naming every column it could not
  map.
- **IM3. Default mapping** — unmapped data is parked, never dropped:

  | Source column | Knit target | Notes |
  |---|---|---|
  | Work Item Type | `type` | Epic→epic, Feature→feature, Bug→bug, User Story→task, Task→task |
  | Title | title | |
  | State | `state` | New/Proposed→todo; Active/In Progress/Committed/Resolved→in-progress; Done/Closed→done; Removed→dismissed |
  | Assigned To | `assignee` | email local-part, else slugified display name; check `knit handles` first |
  | Parent (id) *or* Title-N indent | parent | see hierarchy |
  | Iteration Path | `slate` | last path segment; `knit slate add` if missing |
  | Priority (1–4) | `priority` | 1→critical, 2→important, 3→normal, 4→someday |
  | Target/Due Date | `due` | normalised to `YYYY-MM-DD` |
  | Description, Acceptance Criteria | body sections | HTML → markdown |
  | Tags + any other column | `## Imported notes` | preserved, never dropped |

- **Hierarchy:** prefer an explicit Parent-ID column; otherwise infer from a
  tree query's Title 1 / Title 2 / … columns (depth = the populated Title-N,
  parent = nearest preceding row at depth − 1). The doc tells users to export
  one of the two.
- **Open:** User Story → `task` (keeps epic › feature › task) vs `feature` —
  confirm. Validate the recipe against a real sample export before shipping.

## Task — Migration prompt doc + sample
- Write `docs/csv-migration.md`: export instructions (tree query or flat +
  Parent column), the field-mapping table (type/state/assignee/parent/iteration→
  slate/priority/due/body/`## Imported notes`), hierarchy guidance, and a
  **paste-ready agent prompt** that drives only public CLI verbs. Ship
  `demo/sample-import.csv`; add a README "Importing an existing backlog" pointer.
- **No skill, no command** (design IM1); vendor named only in this doc.
- **Acceptance:** doc walks end-to-end against the sample CSV; no new CLI
  surface.
