# Verify / relink

## Repair — `verify` / `relink`

After a hand-rename or any broken reference, `knit verify` finds the breaks and
`knit relink` repairs them interactively. Each finding prints its
path, reason, and a `hint:` line, so run the command for the specifics
rather than relying on this list — it stays authoritative, this
doesn't. What matters for driving it:

- **Two severities.** *Hard errors* (exit 4) cover structural /
  invisible-file problems: files that won't load or resolve
  (`invalid_items`, `stray_files` — nothing under `.knit/` may silently
  vanish: a file no body links, an SVG no doc in its own folder embeds, a file
  beside items without its item's id prefix or whose item is gone), broken / malformed link targets, title-vs-H1 drift,
  hierarchy violations, dangling ADR origins, stale identity fields
  (`identity_drift` — a KB `id:` or design `slug:` that disagrees with its
  own filename, so the declared value resolves nowhere). *Warnings* (exit 0) cover
  hand-authored body prose (`prose_refs`, `prose_links`) — renames repair
  the refs they can find, a hand edit nothing does — plus reachable-but-smelly states like a `done` item
  with an open direct child (`done_with_open_children` — closing does not
  cascade, so this is normal-use drift, not corruption; a *dismissed*
  parent is exempt — fix by closing the child or reopening the parent), an
  open item still blocked by a closed one (`stale_blockers`; `sole` ⇔ the row
  is its only impediment — `unblock` it, else `unlink` the blocker, or point
  the row at a new blocker if the block stands), and
  `svg_files`: an SVG under `.knit/` that isn't well-formed, lacks a `viewBox`,
  holds what `knit web` strips (`<script>`, `<style>`, `<foreignObject>`, `on*`,
  an external `href`), repeats an id, has an edge end naming no id, or a node
  or group without a `<title>`.
- **Prose refs** are scanned in item, KB, design and project-doc bodies,
  outside code and `## Links` rows, plus the `click` / `link` lines of mermaid
  fences. A code-span with a `<placeholder>` or
  `*` is never a ref, so it is not checked. An `item:` ref must carry the full id:
  a prefix is flagged even while unique (`short_id`); an `ambiguous_id`
  finding lists the ids it matches.
- **Section refs** (`<ref>#<anchor>`): `missing_section` when no heading has
  the anchor; `suffixed_anchor` when it targets a repeated heading's `-2` /
  `-3` anchor, which shifts when a same-named heading is added above — make
  the heading unique. `reserved_anchor` flags a heading whose anchor reads
  `mermaid-<n>`: a link to it is read as a diagram pointer. A pointer
  (`#mermaid-N…`) is checked for its doc only; an SVG pointer
  (`#<file>.svg…`) for the file next to it.
- **Local targets** — `prose_links` (relative markdown links / images) and
  `source_paths` (a KB source's `path:`) are warnings. Inside the repo root or a
  mount that resolves here, a missing target is flagged; a target that leaves
  the repo root with no mount covering it is flagged as `outside_repo` (often
  one `../` too many), existing or not. Under an unresolved mount it is left to
  the `mounts` row. URLs are never checked.
- **`--strict`** promotes the prose warnings to hard errors (exit 4) —
  use it in CI.
- **`--json`** emits one array per check class; run `knit verify --json`
  to see the current shape. Exit is 4 when any hard-error array is
  non-empty, or (with `--strict`) any array is.
- **`relink`** auto-repairs broken link targets with an unambiguous match
  and expands a `short_id` ref to the full id in place; everything else is
  fix-by-hand (the finding's `hint:` says how).
- **`relink --ids`** is the separate repair for `identity_drift`: it sets
  each drifted `id:` / `slug:` from the filename, which is authoritative.
  Opt-in on purpose — a plain `relink` never touches an identity field.
- **`mounts`** is reported once per mount with the number of KB sources
  riding on it, never once per source. A mount that doesn't resolve is
  *machine state*, not corruption — informational, and `--strict` leaves
  it alone; a source naming a mount **nothing declares** is broken
  content and `--strict` fails on it. Repair with `knit mount set <name>
  <path>` (committed) or `--local` (this machine only).
- **`committed_identity`** — a `handle` / `display` in the committed
  `config.json`, which makes everyone who clones the repo record as that
  person. A warning (`--strict` fails on it); fix with `knit config unset
  --repo <key>`, and each person sets their own with `knit config set --global handle <you>`.
- **`global_config`** names what the *user-level* config declares that applies
  to no repo — a repo-only key or a relative mount path someone hand-edited in.
  Reported, never counted: the file belongs to the machine, so the repo still
  verifies clean (exit 0) and `--strict` leaves it alone.
