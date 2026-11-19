# Diagrams

How to draw a Mermaid diagram that reads well in `knit web`, on GitHub, in an
image export and as source.

A diagram serves engineering and understanding: it is clear and gets its
message across, with links to other diagrams or sections where the reader
needs to go on. Beauty comes second.

## Shape the diagram

- **Pick the type for the content.** Take the first that works out:
  1. the type that fits — sequence for interactions, state for lifecycles,
     ER for data, class for structure, flowchart for flows; flowchart isn't
     the default for everything;
  2. `block-beta` when the layout is a grid: layers, stacks, racks, page or
     memory layouts (see Block and architecture);
  3. `architecture-beta` for infrastructure: services, groups, the side an
     edge attaches to;
  4. an SVG file, when the layout must be free (a floor plan) or mermaid still
     doesn't work out after editing (see SVG file).
- **One level of abstraction**, like code. Split a large diagram into an
  overview and detail diagrams; at around 15–20 nodes it is getting too much
  (see Large flowcharts).
- **Structure in the diagram, reasoning in the prose.** The main element, or
  something exceptionally important, may carry one short clause (a second
  label line or a note) so the diagram alone gives a first look. It repeats
  the prose, never replaces it.
- **`title:` in the frontmatter** when the surrounding text gives too little
  context or the diagram stands alone.

## Labels

- **Quote labels with punctuation:** `db["Orders (primary)"]`. Unquoted `()`,
  `[]`, `:` or `;` break parsing.
- **Line breaks help clarity:** `<br>` in a label, or a markdown string
  (`` id["`**Orders** _primary_`"] ``) for emphasis and real line breaks.
- **Unicode glyphs** highlight, or carry a kind or a similarity: 🔒 auth,
  ⚠ risk, numbered markers (`①`, `1.`) for step order. No `fa:` or icon packs — they don't
  render alike in the SPA, on GitHub and in an export.

## Visual meaning

Each channel — shape, edge style, colour, glyph — carries one meaning picked
for this diagram, and only where it serves what the diagram is meant to say.
Don't overdo it: a handful of meanings at most, else the diagram does too much
— split it or drop the least important.

- **Defaults need no legend:** cylinder = store, diamond = decision,
  dashed = optional / planned.
- **Consistent across the project:** before drawing, check for the
  `doc:diagram-conventions` KB entry and reuse its marks. When a mapping recurs
  in a second diagram, offer to start it (a mark | meaning table).
- **Legend** for every other shape, colour, line style or glyph with a
  specific meaning, project marks included — exports and GitHub don't carry the doc.
  Mark → meaning pairs, never prose. Each pair starts with the mark itself —
  a colour swatch, a glyph, a line or a shape character — so the legend scans
  at a glance; don't name the shape ("stadium"). One line or bullet per class
  of related marks, never all classes in one row:

  ```
  ━ exists · ┅ planned
  🟦 inbound · 🟩 outbound
  ⬭ partner · ⬡ business rule · ▱ reference data
  ```

  More pairs in a class go in a small table. No legend for what labels already
  say (id formats, names) or for glyphs that only highlight.

The examples below are illustrations, not a fixed vocabulary:

- **Node shapes** only for the few kinds that matter; everything else stays a
  plain box. E.g. store `[()]`, external `([])`, decision `{}`.
- **Edge styles:** e.g. `-->` normal, `-.->` optional or async, `==>` the main
  path.
- **Colour, sparingly:** `classDef` + `class` on nodes, and on edges through an
  edge id — e.g. a critical path in red:

  ```mermaid
  flowchart LR
    api e1@==> orders[("Orders")]
    classDef critical stroke:#d33,color:#d33
    class e1 critical
  ```

  Prefer edge ids to `linkStyle <index>`, which shifts when an edge is added
  before it. Use stroke-only colour, or a fill with an explicit `color:`, so it
  reads in light and dark.
- **Draw for `knit web`.** It renders Mermaid 11.6 at least, so edge ids,
  `@{ shape: … }` and other recent syntax are fine; other viewers don't set the
  bar.
- **Subgraphs** for boundaries (service, team, trust zone), with meaningful ids.

## Layout and theme

- **Direction** suits the content: `LR` for pipelines, `TB` for hierarchies.
  The flowchart header (`flowchart LR`), a top-level `direction LR` for state
  and class.
- **Layout keys** only when the default looks bad — `curve` (`linear`,
  `stepBefore` for straight lines), `nodeSpacing` / `rankSpacing`,
  `wrappingWidth` for long labels. Write them in the fence's frontmatter, the
  form the SPA's layout menu writes, never `%%{init}%%`:

  ```mermaid
  ---
  config:
    flowchart:
      curve: linear
  ---
  flowchart LR
    ...
  ```

- **Keep existing direction and layout keys.** A human may have set them in
  the SPA's layout menu; change them only when asked.
- **No `theme` or `themeVariables` per fence.** The SPA themes diagrams from
  its palette and exports switch light / dark; a fixed theme breaks one.
- **No `layout: elk`.** `knit web` doesn't load the ELK layout; stay with the
  default (dagre).

## Large flowcharts

The default layout places nodes by the edges; these steer it.

- **Split, connect with placeholders.** Each part under its own heading (the
  heading names the diagram). A node drawn in another diagram appears as a
  dashed grey placeholder: its name, `⇢` and that diagram's heading.

  ```mermaid
  flowchart LR
    order["Order"]
    customerRef["Customer<br>⇢ <i>Customers</i>"]
    order -->|placed by| customerRef
    classDef ref stroke:#8a8a8a,color:#8a8a8a,stroke-dasharray:4 3
    class customerRef ref
  ```

  - Every edge into another diagram ends at a placeholder — none is dropped.
  - Incoming: placeholders only for the diagram's entry points, not every
    edge from outside, so the reader sees where to start.
  - Legend: `⇢ placeholder, drawn in the other diagram`.
  - **Link the placeholder** to the other diagram's heading, so `knit web`
    jumps there: a full section ref (see `knit` skill > Ref format) in mermaid's
    own syntax — `click customerRef href "design:shop#customers"` (flowchart,
    state, gantt), `link Customer "design:shop#customers"` (class). Nodes
    only; other types have no link syntax, the placeholder keeps its label.
  - The `⇢` label stays: exports and GitHub carry no working link.
  - Renaming a heading breaks the links to it: grep the old ref and update
    them in the same change, then run `knit verify`.
- **Repeat nodes that aren't part of the model** — placeholders, files,
  external targets — once per subgraph, when that cuts a long edge across the
  diagram. Model nodes stay unique.
- **`a ~~~ b`** (invisible link) puts `b` after `a` in the flow direction —
  to order or pull related nodes along. Not for stacking: in `LR` a chain
  `a ~~~ b ~~~ c` staggers diagonally. Between whole subgraphs it is mostly
  ignored once they have outside edges.
- **Borderless subgraph to group or stack nodes** in one column (in `LR`):

  ```mermaid
  flowchart LR
    subgraph entry [" "]
      a["A"]
      b["B"]
    end
    style entry fill:none,stroke:none
  ```

  A subgraph's `direction` is ignored once its nodes link outside it; the
  grouping comes from the cluster, not the direction.
- **Declare nodes inside their subgraph first.** A node lands where it first
  appears; an earlier edge outside pulls it out of its subgraph.
- **Subgraph colours are assigned in order** — adding one, borderless too,
  shifts the next ones'. `style` them when colour carries meaning.
- **Check the render, not the source** — `~~~` and subgraph effects are hard
  to predict. In `knit web`, or `npx -y @mermaid-js/mermaid-cli -i x.mmd -o x.png -s 2`
  when Node is at hand.

## Block and architecture

Both give the author more say over placement than a flowchart; neither places
freely.

**`block-beta`** lays blocks out on a grid:

- `columns N`; `id["Label"]:n` spans n columns; `space` / `space:n` leaves a
  gap. No row span, no coordinates.
- Shapes as in a flowchart (`[("…")]` store, `(["…"])`, `{"…"}`, …), and block
  arrows: `down<["&nbsp;"]>(down)`.
- **Nesting structures the diagram:** `block:id:n … end` is a frame with its
  own `columns`, for containment and boundaries — as subgraphs are in a
  flowchart.
- Edges (`a --> b`, `a -- "label" --> b`) run straight and cut through blocks
  in between: keep connected blocks adjacent, or arrange the grid so edges
  cross nothing.
- **Layer stack** — for anything stacked on each other: modules of one
  codebase (core lib at the bottom, apps on top), a software stack
  (hardware → OS → runtime → middleware → apps), layered services, a 3-tier
  architecture (presentation / logic / data):
  - one row per layer, written top to bottom, the foundation last;
  - a nested block per layer as its frame, one block per module; widths by
    span, the foundation spans the full grid;
  - shapes keep their meaning; `<br>` for a second label line;
  - a `classDef` for marked layers, with a legend line when the mark means
    something;
  - no dependency arrows: the stacking carries the meaning.

  ```mermaid
  block-beta
    columns 4
    web["Web shop"]:2 admin["Admin portal"] mobile["Mobile app"]
    block:svc:4
      columns 3
      orders["Orders"] billing["Billing"] shipping["Shipping"]
    end
    block:shared:4
      columns 4
      auth["🔒 Auth"] msg["Messaging"] persist[("Persistence")] cfg["Config"]
    end
    core["Core lib<br>results · ids · logging"]:4
    classDef app stroke:#2563eb,stroke-width:2px
    class web,admin,mobile app
  ```

- **A nested block shows no name**, and `title:` in the frontmatter doesn't
  show. When the layers need names, open each row with a narrow borderless
  label block:

  ```mermaid
  block-beta
    columns 5
    l1["Services"] block:svc:4
      columns 3
      orders["Orders"] billing["Billing"] shipping["Shipping"]
    end
    l2["Base"] core["Core lib"]:4
    style l1 fill:none,stroke:none
    style l2 fill:none,stroke:none
  ```

**`architecture-beta`** lays out automatically; the author steers it:

- `service id(icon)[Label] in group`, `group id(icon)[Label] in parent` for
  nesting (plant ⊃ line), `junction id` to split an edge.
- Edges name the side they attach to: `api:R --> L:db`; right angles when the
  sides differ. `a{group}:R --> L:b` starts the edge at a's group.
- `align row a b c` / `align column a b` line services up.
- Built-in icons only: `cloud`, `database`, `disk`, `internet`, `server`.
- The layout is stable between renders; when it is bad, side hints and
  `align` fix more than the layout keys.

**Layout keys**, in the frontmatter as for flowchart: `block.padding` (gap
between blocks); `architecture.nodeSeparation`, `padding`, `iconSize`,
`fontSize`, `idealEdgeLengthMultiplier`, `edgeElasticity`.

## SVG file

The last resort, only after the mermaid steps don't work out: it adds a file,
is far more text, and is harder to reason about than mermaid source.

- **Where:** a file next to the markdown, embedded with an image link on a line
  of its own: `![Assembly hall](assembly-hall.svg)`. `knit web` draws it with
  pan, zoom, pointers and export; elsewhere it is a plain image.
- **Meaning in attributes**, so nobody has to read geometry:
  - `<desc>` first: what the diagram says, in one or two sentences;
  - `data-kind="node"`, `"edge"`, `"group"` or `"label"`, with an `id`;
  - an edge carries `data-from` / `data-to`, naming ids;
  - a group is a `<g>` that contains its members;
  - `<title>` as an element's first child: its name and the pointer label;
  - one `transform="translate(x,y)"` per node; coordinates on a grid of 8.
- **Colors by class:** `k-node`, `k-edge`, `k-arrow` (an arrowhead marker),
  `k-group`, `k-label`, plus `k-muted` / `k-accent`. `knit web` colors them for
  its theme; neutral `fill` / `stroke` attributes keep the file readable
  elsewhere.
- **Not allowed** (stripped by `knit web`, flagged by `knit verify`):
  `<script>`, `<style>`, `<foreignObject>`, `on*` handlers, an `href` other than
  `#id`. A `viewBox` is required.
- The rules above hold too: purpose, self-explaining labels, visual meaning
  with a legend, one level of abstraction.
- **Check the render** before handing over: open it in `knit web`, or render
  the file (resvg, a headless browser) and look. Label width is the usual
  failure — leave room. Without a renderer, say it wasn't looked at.
- **Before editing, read the outline:** `knit ref show <ref>#<file>.svg` prints
  the `<desc>`, every node, edge and group, and where each node sits (its
  `translate`). It finds what to change; for paths, sizes and free room, read
  the file.

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 64">
  <desc>Orders go from the shop to billing.</desc>
  <g data-kind="node" id="shop" class="k-node" transform="translate(8,16)">
    <title>Shop</title>
    <rect width="72" height="32" rx="4" fill="#f4f4f5" stroke="#71717a"/>
    <text x="36" y="20" text-anchor="middle" font-size="12">Shop</text>
  </g>
  <g data-kind="node" id="billing" class="k-node" transform="translate(160,16)">
    <title>Billing</title>
    <rect width="72" height="32" rx="4" fill="#f4f4f5" stroke="#71717a"/>
    <text x="36" y="20" text-anchor="middle" font-size="12">Billing</text>
  </g>
  <path data-kind="edge" id="placed" data-from="shop" data-to="billing" class="k-edge"
        d="M80,32 H160" stroke="#52525b" fill="none"><title>order placed</title></path>
</svg>
```

## Type idioms

- **Sequence:** `autonumber`, `alt` / `opt` / `loop` / `par`, `box` to group
  participants, `Note over`.
- **State:** composite states, `[*]` for start and end, notes.
- **ER:** crow's-foot cardinality (`||--o{`), `PK` / `FK` on attributes.
- **Block:** a nested block per boundary or layer; spans for widths.
- **Architecture:** groups for boundaries, side hints on every edge.

## Pointers — how a human points at an element

Clicking an element of a diagram in `knit web` copies a pointer, which the
human pastes into the chat:

```
design:auth#mermaid-2/node/db["Database"]
item:G7H8K2M4P9Q1R5T3VX#mermaid-1/edge/authSvc->db["reads"]
design:auth#mermaid-2(flowchart)
```

- `<ref>` — the body holding the diagram (`knit ref show <ref>`). A body without
  a ref (milestone, non-readme project doc, KB folder index) gives its `.knit/…`
  path instead.
- `#mermaid-N` — the Nth mermaid fence in that body, 1-based.
- `/<kind>/<id>` — the element with that DSL id; an id outside mermaid's id
  syntax is quoted (`/node/"[*]"`). The kind follows the type: `node` and
  `cluster` (a subgraph, composite state or nested block), `service`, `group`
  and `junction` in architecture, … Types without DSL ids (mindmap, timeline,
  pie, journey, …) give only a kind + label: match the label.
- `/<kind>/a->b` — an edge or message, by its endpoints. The arrow style isn't
  in the pointer; read it from the source.
- `["<label>"]` — a hint, cut at 80 characters, `"` escaped as `\"`; the id wins
  when they disagree.
- No element part, only `(<type>)` — the whole diagram.
- An SVG file: `<ref>#<file>.svg/<kind>/<id>`, from its `data-kind` and `id`;
  `<ref>#<file>.svg` is the whole diagram.

**Editing from a pointer.** Change only what the request asks; don't reflow or
rename the rest of the diagram — the diff stays reviewable and other pasted
pointers stay valid. Adding a fence before an existing one shifts its `#N`.
