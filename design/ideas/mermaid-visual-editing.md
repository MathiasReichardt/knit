# Visual mermaid editing in the SPA — idea

*From the mermaid click-to-pointer discussion (2026-10-01).*

Status: **idea / not scheduled.** Follow-up to mermaid click-to-pointer, which
makes diagram elements addressable from the SPA.

## Brief idea

Once a click identifies a diagram element, edit it in place: change a label,
a node's colour or stroke width, draw an edge between two nodes, add a node.
Possibly limited to a few diagram types.

## What holds it back

Mermaid parses but does not print, and its classic parsers keep no source
positions. Every edit is a text rewrite of the fence without knowing where the
element was written.

| Edit | Source change | Robust? |
|---|---|---|
| Label | find `A[...]` and replace — but a node may be defined several times, inline in edges too | no |
| Label, append-only | append `A["New"]`; a later definition wins | yes, leaves clutter |
| Node colour / stroke | append `style A stroke:#f00,stroke-width:3px` | yes |
| Edge colour / stroke | `linkStyle N …`, N = edge order in source; any edge add/remove shifts it | no |
| Draw edge | append `A --> B` | yes |
| Add node | append `n1["…"]`; auto-layout ignores where it was dropped | works, feels wrong |
| Other types | own syntax each | effort multiplies |

## Parser landscape (as of 2026-10-01)

- **`@mermaid-js/parser`** (official, MIT, Langium): an AST with exact source
  ranges (`$cstNode.offset` / `end`) — the right base for surgical edits.
  2.0.0 (2026-09-10) covers architecture, cynefin, eventmodeling, gitGraph,
  info, packet, pie, radar, railroad, treeView, treemap, wardley. **Not**
  flowchart, sequence, class, state, ER: they stay on Jison. The tracking
  issue [#4401](https://github.com/mermaid-js/mermaid/issues/4401) is open
  with no per-diagram plan; the one flowchart attempt ("[WIP] Trying to add
  flowchart Langium parser") was closed in 2026-04; the xychart port has been
  open since 2025-07. New diagrams land on Langium; old ones aren't moving.
- **`@emily/mermaid-ast`** (MIT): parse + print for ~18 types on vendored
  Jison grammars. Prints an equivalent diagram, not the original text —
  comments and formatting are lost, no source positions. Single author,
  ~1 download/week. Rejected: a print rewrites the user's whole fence.
- Forking `flow.jison` to record positions, or a tree-sitter grammar, means
  owning a second parser that drifts from mermaid's.

## Why not now

- **Against Phase 10's thesis.** The SPA points, the agent edits the source
  cleanly. A partial visual editor competes with that and writes worse source
  (stacked `style` overrides, duplicate definitions).
- **Scope.** A visual diagram editor is a product; Mermaid Chart (commercial)
  is one, and Excalidraw imports mermaid one-way.
- "Limited diagrams" means flowchart only in practice.

## If it comes back

- **Trigger A:** flowchart lands on `@mermaid-js/parser` — then rewrites can
  target exact source ranges.
- **Trigger B:** Phase 10 in use shows the agent round-trip is too slow for
  trivial tweaks. Then the candidate is **append-only, flowchart-only**: draw
  an edge, style a node, relabel by override. Never rewrite an existing line.
  The write path exists already (the task-list checkbox toggle rewrites the
  body and the host persists it).

Open: is append-only clutter acceptable in a hand-authored design doc, or does
every visual edit owe a clean source — which only Trigger A delivers?
