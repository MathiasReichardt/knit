# `Knit.Core.Kb` — knowledge-base code

Contributor-facing notes on the KB code layout. The user-facing surface
is documented in the `knit-kb` skill ([skills/knit-kb/SKILL.md](../../../skills/knit-kb/SKILL.md));
the decisions behind it are in
[design/decisions.md](../../../design/decisions.md) > Knowledge base.

## Files

| File | Responsibility |
|---|---|
| `KbLayout.cs` | Path constants under `.knit/kb/`. Resolves bucket folders, flat vs folder form (`<slug>.md` vs `<slug>/readme.md`), index file paths. Enumerates authored artifact files (skips generated index files). |
| `KbFrontmatter.cs` | Strongly-typed YAML frontmatter records per bucket (`AdrFrontmatter`, `DocFrontmatter`, `FeatureFrontmatter`, `LogFrontmatter`, `IdeaFrontmatter`, `SourceFrontmatter`) plus `KbFrontmatterMap` parser/serializer. Mirrors the YAML shape described in the design doc. |
| `KbTemplates.cs` | Embedded body templates per bucket. Used by `knit kb add <bucket>` to scaffold a new entry. Identical pattern to `Knit.Core.Templates` for items — embedded resources, no IO at template-render time. |
| `KbStore.cs` | File IO over the layout. Read / write / exists / delete for a KB entry by `Ref`. Returns plain records; does not touch the index. |
| `KbIndexer.cs` | Regenerates `kb-index.md` (master TOC) and per-bucket `index.md` files. Rebuild is whole-folder, not incremental. Called from `KbStore` writes via the CLI's mutator helpers. |
| `KbRename.cs` | `knit kb rename` core: moves the file, rewrites `kb_refs:` rows on every dependent item (via the injected `ItemRefRewriter`), and reports the touched entries in `KbRenameResult`. Symmetric with `Knit.Data.Reindexer` in spirit — derived state must stay consistent with the authored state. |
| `KbDelete.cs` | `knit kb delete` core: containment-guards the target under `.knit/kb/`, strips matching `KB ref` rows from items (via the injected `ItemRefRemover`) **before** the unlink, then removes the file/folder. `Preview` is the read-only dry run (blocker list + affected count) that drives the CLI prompt and SPA dialog; folder-form entries with sibling files require `deleteFolder`. |

## Storage shape

```
.knit/kb/
  kb-index.md                  — master TOC (generated)
  adr/
    index.md                   — bucket TOC (generated)
    0001-supplier-choice.md    — flat form
    0002-glue-supersede.md
  feature/
    auth/
      readme.md                — folder form (when an entry grows assets)
      diagram.png              — sibling assets, referenced from readme.md
  doc/
  log/
  idea/
  source/
```

- **Flat form** is the default: `<bucket>/<slug>.md`. Created by
  `knit kb add` until the entry needs siblings.
- **Folder form**: `<bucket>/<slug>/readme.md`. Use when an entry
  owns assets (diagrams, snippets, data). Promote with
  `knit kb split <bucket>:<slug>`.
- Bucket-level `index.md` and the root `kb-index.md` are generated;
  `KbLayout.IsArtifactFile` filters them out of enumerations.

## Frontmatter shape (terse)

Every entry has at minimum `bucket:`, `slug:`, `title:`, `created:`,
`updated:`. ADRs add `status:` + `superseded_by:` / `supersedes:`.
Sources add `url:`. See `KbFrontmatter.cs` for the exact records and
the design doc for field semantics.

## Refs (`bucket:slug`)

The unified ref model lives in `Knit.Core.Refs`, not here. KB code
takes `Ref` values as input and converts them to disk paths via
`KbLayout.FilePath(...)`. Items reference KB entries via their
`kb_refs:` frontmatter rows (bare or annotated); item↔item links live
in the `## Links` table inside item bodies. The CLI's `knit ref show`
command resolves any `bucket:slug` (item or KB) to a file via the same
`Ref` model.

## Where the rest lives

- `Knit.Cli.Commands.KbCommand` — argument parsing and human / JSON
  output for `knit kb …`.
- `Knit.Cli.Commands.RefShowCommand` — universal `bucket:slug` viewer.
- `Knit.Core.Refs` — ref parser, bucket enum, bucket → folder
  mapping, slug validation.

## Conventions worth knowing

- **Indexes are derived state.** Treat `kb-index.md` and per-bucket
  `index.md` as cache — never read them to make decisions, regenerate
  from the authored files.
- **Renames go through `KbRename`.** Don't move files directly from
  command handlers — link rewriting is centralised here. For folder-form
  entries the whole folder moves, so sub-doc refs (`feature:foo/wiring`)
  are rewritten alongside the parent's `readme.md` ref, not just it.
- **Deletes go through `KbDelete`.** Never `File.Delete` /
  `Directory.Delete` a KB artifact from a command handler — incoming
  `KB ref` rows must be stripped first, and the path must pass the
  containment guard. `KbDelete` does both; folder-form entries with
  siblings only go when the caller opts into `deleteFolder`.
- **Folder vs flat is transparent.** Callers ask for an entry by
  `Ref`; `KbLayout` resolves to whichever form exists on disk.
