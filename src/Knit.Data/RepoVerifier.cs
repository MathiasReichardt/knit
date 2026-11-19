using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Knit.Core.Config;
using Knit.Core.Designs;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.IO;
using Knit.Core.Kb;
using Knit.Core.Paths;
using Knit.Core.Projects;
using Knit.Core.Refs;
using Knit.Core.Slates;
using Knit.Core.Svg;

namespace Knit.Data;

/// <summary>
/// Pure repo-consistency scans shared by <c>knit verify</c> / <c>knit relink</c>
/// (CLI) and the web repo-health endpoints. Parameterised over the primitives
/// it needs (<see cref="ItemStore"/>, repo root, frontmatter serializer) so it
/// carries no Spectre / System.CommandLine / ASP.NET dependency.
///
/// Four classes, mirroring the CLI contract:
///   1. Broken <c>Target</c> paths in <c>## Links</c> tables (hard).
///   2. Frontmatter <c>title</c> vs body leading <c>#</c> h1 drift (hard).
///   3. Hierarchy violations — parent/child type combinations a hand-edit
///      sneaks past (hard).
///   4. Dangling body-prose refs / md links / images (warning).
/// </summary>
public static class RepoVerifier
{
    public sealed record BrokenLink(string SourcePath, LinkRow Row, string ExpectedPath);

    /// <summary>
    /// A <c>## Links</c> row whose target is shaped like a sibling item link
    /// (a <c>.md</c> path not under <c>kb/</c>) but whose extracted id is not
    /// a valid NanoID — so it will never resolve as a parent/blocker even
    /// though the file exists on disk. A path-existence-only broken-target
    /// scan stays green on this silent-failure class; this turns it red.
    /// No safe auto-fix (guessing intent) — <c>relink</c> leaves it alone.
    /// </summary>
    public sealed record MalformedItemLink(string SourcePath, LinkRow Row, string ExtractedId);

    public sealed record TitleDrift(string SourcePath, string FrontmatterTitle, string BodyH1);

    /// <summary>
    /// Hierarchy violation: an item's <c>Parent</c> link resolves to an
    /// item whose type is not allowed by <see cref="Hierarchy.ParentAllowed"/>,
    /// OR a root item (no parent) is itself not a valid root for its type.
    /// </summary>
    public sealed record HierarchyViolation(
        string SourcePath,
        string ChildId,
        ItemType ChildType,
        string? ParentId,
        ItemType? ParentType);

    /// <summary>A `bucket:slug` code-span in a body that doesn't resolve, or an
    /// <c>item:</c> ref that isn't a full id. Warning-severity (drift, not
    /// corruption). <paramref name="Candidates"/> holds the full ids a short
    /// <c>item:</c> prefix matches: one means <c>relink</c> can expand it.</summary>
    public sealed record ProseRef(string SourcePath, string Ref, string Reason, IReadOnlyList<string> Candidates);

    public static class ProseRefReasons
    {
        public const string Unresolved = "unresolved";
        public const string ShortId = "short_id";
        public const string AmbiguousId = "ambiguous_id";
        public const string MissingSection = SectionCheck.MissingSection;
        public const string SuffixedAnchor = SectionCheck.SuffixedAnchor;
        /// <summary>A heading whose anchor reads <c>mermaid-&lt;n&gt;</c>: a link to it
        /// is read as a diagram pointer. <see cref="ProseRef.Ref"/> is that section ref.</summary>
        public const string ReservedAnchor = "reserved_anchor";
    }

    /// <summary>A body markdown link / image whose relative target is missing,
    /// or leaves the repo root with no mount covering it (see <see cref="ScopeOf"/>).
    /// Warning-severity.</summary>
    public sealed record ProseLink(string SourcePath, bool IsImage, string Target, string ExpectedPath, string Reason);

    public static class ProseLinkReasons
    {
        public const string Missing = "missing";
        public const string OutsideRepo = "outside_repo";
    }

    /// <summary>A KB source whose <c>path:</c> names nothing, or can't be resolved
    /// inside its mount. Warning-severity: the entry still loads, only opening
    /// it fails. A source on an unresolved or undeclared mount is not listed —
    /// <see cref="ScanMounts"/> reports that once per mount.</summary>
    public sealed record MissingSource(string SourcePath, string? Mount, string StoredPath, string ExpectedPath, string Reason);

    /// <summary>An ADR <c>origin:</c> frontmatter ref (design:/item:) that
    /// <see cref="RefResolver"/> can't resolve — the design/item it records
    /// as its origin was deleted or renamed. Hard-severity (structured ref,
    /// not prose).</summary>
    public sealed record DanglingOrigin(string SourcePath, string Ref);

    /// <summary>
    /// An entry under <c>.knit/items/</c> that won't load as a valid item, so
    /// it silently vanishes from list / tree / index (and, when it drops a
    /// parent from the graph, can flatten the tree). <see cref="Reason"/> names
    /// the specific defect. Hard-severity — an invisible item is a data-loss
    /// class, not drift. See <see cref="ScanItemFiles"/> for the caught cases.
    /// </summary>
    public sealed record InvalidItemFile(string SourcePath, string Reason);

    /// <summary>
    /// A file under <c>.knit/designs/</c> or <c>.knit/kb/</c> that exists on
    /// disk but won't appear in <c>design list</c> / the KB browser because its
    /// name or location doesn't match the addressable shape. Same silent-hidden
    /// data-loss class as <see cref="InvalidItemFile"/>, but for the path/slug-
    /// identified KB + design artifacts (no NanoID / <c>index.md</c> rules), so
    /// it's reported as its own class. See <see cref="ScanStrayFiles"/>.
    /// </summary>
    public sealed record StrayFile(string SourcePath, string Reason);

    /// <summary>
    /// Two or more KB artifacts in the same bucket that declare the same
    /// frontmatter <c>id</c> — an identity collision. The filesystem enforces
    /// slug uniqueness but nothing enforced id uniqueness, so a mutator that
    /// wrote to a divergent slug (e.g. the old <c>kb set adr:0002</c> fork)
    /// could leave two files claiming the same id; refs then
    /// resolve ambiguously to whichever the enumerator hits first. Hard-severity
    /// — same "nothing silently wrong" class as <see cref="StrayFile"/>.
    /// <see cref="Paths"/> lists every colliding file.
    /// </summary>
    public sealed record DuplicateKbId(string Bucket, string Id, IReadOnlyList<string> Paths);

    /// <summary>
    /// A document whose declared identity disagrees with its own filename:
    /// a KB entry's <c>id:</c>, or a design doc's <c>slug:</c>. The field
    /// duplicates the name, and refs resolve by name — so the field resolves
    /// nowhere, while being the only place a reader sees it. Items have had
    /// this check since the start (<see cref="ScanItemFiles"/>); KB and
    /// designs did not, which is what let `kb rename` leave a stale id behind
    /// unnoticed. The filename is authoritative, so the repair is
    /// unambiguous: <c>knit relink --ids</c>.
    /// </summary>
    public sealed record IdentityDrift(string SourcePath, string Field, string Declared, string Expected);

    /// <summary>
    /// A <c>health:</c> frontmatter field on an item that is not a
    /// <c>project</c>. Health is a project-only signal; the CLI/API write
    /// paths reject it elsewhere, so a finding here is a hand-edit or a retype that left the field behind.
    /// Hard-severity (structured drift, like hierarchy).
    /// </summary>
    public sealed record HealthOnNonProject(string SourcePath, string ItemId, ItemType Type, Health Health);

    /// <summary>
    /// A bad <c>project:</c> doc-folder pointer:
    /// either the field sits on a non-<c>project</c> item, or the slug has
    /// no <c>.knit/projects/&lt;slug&gt;/readme.md</c> behind it. Hard-severity
    /// (structured ref, like <see cref="DanglingOrigin"/>).
    /// </summary>
    public sealed record ProjectPointerIssue(string SourcePath, string ItemId, ItemType Type, string Slug, string Reason);

    /// <summary>
    /// A project doc folder (<c>.knit/projects/&lt;slug&gt;/</c>) with no
    /// <c>project</c> item pointing at it via <c>project:</c> — the other
    /// orphan direction to <see cref="ProjectPointerIssue"/>'s dangling
    /// pointer. Extends "nothing vanishes" to the item↔folder join.
    /// Soft-severity (a warning): a lone folder is authored content, not
    /// structural drift.
    /// </summary>
    public sealed record OrphanProjectFolder(string FolderPath, string Slug);

    /// <summary>
    /// A <c>project</c> item with no <c>project:</c> pointer at all, so it has
    /// no coexisting doc folder — the item-side mirror of
    /// <see cref="OrphanProjectFolder"/> (a project item and its doc folder
    /// must coexist). Distinct from
    /// <see cref="ProjectPointerIssue"/>, which flags a pointer that <em>is</em>
    /// set but dangles. Soft-severity (a warning).
    /// </summary>
    public sealed record OrphanProjectItem(string SourcePath, string ItemId);

    /// <summary>
    /// An entry under a project's <c>milestones/</c> folder that doesn't
    /// parse as a milestone sub-doc. Doc type is implicit in location —
    /// anything there must be a milestone, and malformed files are findings,
    /// never skipped ("nothing vanishes"). Hard-severity.
    /// </summary>
    public sealed record MalformedMilestone(string SourcePath, string Reason);

    /// <summary>
    /// An item carrying more than one <c>Milestone</c> link row. One
    /// milestone per item is a convention, not a parser rule — the parser
    /// stays permissive and verify warns. Warning-severity.
    /// </summary>
    public sealed record MultipleMilestones(string SourcePath, string ItemId, int Count);

    /// <summary>
    /// A <c>slate:</c> value on items with no row in <c>.knit/slates.md</c>:
    /// a hand-edit, a typo, a merge that crossed a <c>slate rm</c>, or a
    /// <c>set slate=… --force</c> forward reference not yet registered. The
    /// last is deliberate, so this is warning-severity. One finding per
    /// value; <see cref="Paths"/> lists the items carrying it.
    /// </summary>
    public sealed record UnregisteredSlate(string Slate, IReadOnlyList<string> Paths);

    /// <summary>
    /// An item whose <c>Milestone</c> row points into a project other than
    /// the one its own subtree belongs to. Legitimate — an item may serve
    /// another project's milestone, and <c>knit milestone move</c> creates
    /// this on purpose rather than dragging items along — so this is
    /// <b>informational</b>: listed, never a failure, never promoted by
    /// <c>--strict</c>. Items outside any project subtree are not reported
    /// (no scope to disagree with).
    /// </summary>
    public sealed record CrossProjectMilestone(
        string SourcePath, string ItemId, string ItemProject, string MilestoneProject, string MilestoneSlug);

    /// <summary>
    /// A <c>done</c> item with a direct child still open (<c>todo</c> /
    /// <c>in-progress</c>) — the subtree claims complete while live work hangs
    /// under it. Closing does not cascade (skills/knit glossary: "closing a
    /// parent leaves its children untouched"), so this state is reachable
    /// through normal use, not a hand-edit — hence warning-severity. A
    /// <c>dismissed</c> parent is exempt: it is abandoned, not completed, and
    /// its subtree is hidden by default, so open work under it is expected.
    /// Reporting only the direct closed→open boundary is non-redundant and
    /// transitively complete — a done item with a deeper open descendant
    /// necessarily has a closed intermediate whose own open child is flagged
    /// here.
    /// </summary>
    public sealed record DoneWithOpenChild(
        string SourcePath,
        string ParentId,
        string ChildId,
        ItemType ChildType,
        ItemState ChildState);

    public static IReadOnlyList<BrokenLink> Scan(ItemStore store, string root)
    {
        var broken = new List<BrokenLink>();
        foreach (var sourceFile in store.AllItemFiles())
        {
            var sourceDir = Path.GetDirectoryName(sourceFile)!;
            var content = AtomicFile.ReadAllText(sourceFile);
            foreach (var row in LinksTable.Parse(content))
            {
                // Branch / Repo / PR point outside the repo — a URL or a name
                // git owns. Nothing here can check them (Knit never shells out
                // to git), and reading them as paths would report every one as
                // broken. `knit link` validates what it can at write time.
                if (ExternalRefs.IsExternal(row.Rel)) continue;
                var abs = Path.GetFullPath(Path.Combine(sourceDir, row.TargetPath));
                if (!File.Exists(abs)) broken.Add(new BrokenLink(sourceFile, row, abs));
            }
        }
        foreach (var file in KbLayout.EnumerateArtifactFiles(root))
        {
            var content = AtomicFile.ReadAllText(file);
            foreach (var row in LinksTable.Parse(content))
            {
                if (ExternalRefs.IsExternal(row.Rel)) continue;
                var abs = Path.GetFullPath(Path.Combine(KbLayout.ArtifactSourceDir(file), row.TargetPath));
                if (!File.Exists(abs)) broken.Add(new BrokenLink(file, row, abs));
            }
        }
        return broken;
    }

    /// <summary>
    /// Flag <c>## Links</c> rows that look like item links but carry a
    /// malformed (non-NanoID) id. Only reported when the target file
    /// <b>exists</b> — a missing target is already a <see cref="BrokenLink"/>,
    /// so this closes the gap that plain path-existence leaves open. Item
    /// sources only (KB rows point under <c>kb/</c> and are excluded by shape).
    /// </summary>
    public static IReadOnlyList<MalformedItemLink> ScanMalformedItemLinks(ItemStore store)
    {
        var malformed = new List<MalformedItemLink>();
        foreach (var sourceFile in store.AllItemFiles())
        {
            var sourceDir = Path.GetDirectoryName(sourceFile)!;
            var content = AtomicFile.ReadAllText(sourceFile);
            foreach (var row in LinksTable.Parse(content))
            {
                if (!LinkPath.LooksLikeItemTarget(row.TargetPath)) continue;
                if (LinkPath.ItemIdFromTarget(row.TargetPath) is not null) continue; // resolves fine
                var abs = Path.GetFullPath(Path.Combine(sourceDir, row.TargetPath));
                if (!File.Exists(abs)) continue; // already a BrokenLink — don't double-report
                malformed.Add(new MalformedItemLink(sourceFile, row, LinkPath.CandidateIdFromTarget(row.TargetPath) ?? string.Empty));
            }
        }
        return malformed;
    }

    public static IReadOnlyList<TitleDrift> ScanTitles(ItemStore store, string root, IFrontmatterSerializer yaml)
    {
        var drifts = new List<TitleDrift>();
        foreach (var file in store.AllItemFiles())
            CheckOne(file);
        foreach (var file in KbLayout.EnumerateArtifactFiles(root))
            CheckOne(file);
        return drifts;

        void CheckOne(string file)
        {
            var content = AtomicFile.ReadAllText(file);
            var (fm, body) = SplitFrontmatter(content);
            if (fm.Length == 0) return;
            var fields = yaml.Parse(fm);
            if (!fields.TryGetValue("title", out var raw) || raw is null) return;
            var fmTitle = (raw.ToString() ?? string.Empty).Trim();
            if (fmTitle.Length == 0) return;
            var h1 = FirstBodyH1(body);
            if (h1 is null) return;
            if (!string.Equals(fmTitle, h1.Trim(), StringComparison.Ordinal))
                drifts.Add(new TitleDrift(file, fmTitle, h1.Trim()));
        }
    }

    public static IReadOnlyList<HierarchyViolation> ScanHierarchy(ItemStore store)
    {
        var violations = new List<HierarchyViolation>();
        var items = store.AllItems().ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var item in items.Values)
        {
            ItemType? parentType = null;
            if (!string.IsNullOrEmpty(item.Parent) && items.TryGetValue(item.Parent, out var p))
                parentType = p.Type;
            // Unresolved parent ids are already a 'broken link target' case
            // — don't double-report here.
            if (!string.IsNullOrEmpty(item.Parent) && parentType is null) continue;
            if (Hierarchy.ParentAllowed(item.Type, parentType)) continue;
            var file = store.PathOf(item.Id) ?? string.Empty;
            violations.Add(new HierarchyViolation(file, item.Id, item.Type, item.Parent, parentType));
        }
        return violations;
    }

    /// <summary>
    /// Scan KB artifact frontmatter for <c>origin:</c> refs (design:/item:
    /// on ADRs) that no longer resolve. Unlike <c>supersedes</c>/<c>feature</c>,
    /// origin refs are not auto-rewritten on rename/delete (item refs are
    /// UUID-stable; design refs survive archiving via RefResolver's
    /// completed/ fallback), so a genuine dangle here means the target was
    /// removed — worth flagging.
    /// </summary>
    public static IReadOnlyList<DanglingOrigin> ScanOrigins(ItemStore store, string root, IFrontmatterSerializer yaml)
    {
        var dangling = new List<DanglingOrigin>();
        foreach (var file in KbLayout.EnumerateArtifactFiles(root))
        {
            var (fm, _) = SplitFrontmatter(AtomicFile.ReadAllText(file));
            if (fm.Length == 0) continue;
            var fields = yaml.Parse(fm);
            if (!fields.TryGetValue("origin", out var v)) continue;
            foreach (var token in AsRefList(v))
            {
                if (!Ref.TryParse(token, out var r)) { dangling.Add(new DanglingOrigin(file, token)); continue; }
                var resolved = RefResolver.Resolve(r, root).GetValueOrThrow();
                if (!resolved.Exists) dangling.Add(new DanglingOrigin(file, token));
            }
        }
        return dangling;

        static IEnumerable<string> AsRefList(object? v)
        {
            if (v is List<object?> list)
                return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0);
            var s = v?.ToString();
            return string.IsNullOrEmpty(s) ? [] : [s];
        }
    }

    /// <summary>
    /// Flag <c>health:</c> fields on non-project items — see
    /// <see cref="HealthOnNonProject"/>. Fix by clearing the field
    /// (<c>knit set &lt;id&gt; health=</c> after a retype to project, or a
    /// hand-edit removing the line).
    /// </summary>
    public static IReadOnlyList<HealthOnNonProject> ScanHealth(ItemStore store)
    {
        var findings = new List<HealthOnNonProject>();
        foreach (var item in store.AllItems())
        {
            if (item.Health is not { } health) continue;
            if (item.Type == ItemType.Project) continue;
            var file = store.PathOf(item.Id) ?? string.Empty;
            findings.Add(new HealthOnNonProject(file, item.Id, item.Type, health));
        }
        return findings;
    }

    /// <summary>
    /// Flag bad <c>project:</c> pointers — see <see cref="ProjectPointerIssue"/>.
    /// Fix by clearing the field (<c>knit set &lt;id&gt; project=</c>) or
    /// scaffolding the folder (<c>knit project new &lt;slug&gt;</c>).
    /// </summary>
    public static IReadOnlyList<ProjectPointerIssue> ScanProjectPointers(ItemStore store, string root)
    {
        var issues = new List<ProjectPointerIssue>();
        foreach (var item in store.AllItems())
        {
            if (string.IsNullOrEmpty(item.Project)) continue;
            var file = store.PathOf(item.Id) ?? string.Empty;
            if (item.Type != ItemType.Project)
            {
                issues.Add(new ProjectPointerIssue(file, item.Id, item.Type, item.Project,
                    "project: is a project-only field"));
                continue;
            }
            var readme = ProjectsLayout.ReadmePath(root, item.Project);
            if (!File.Exists(readme))
                issues.Add(new ProjectPointerIssue(file, item.Id, item.Type, item.Project,
                    $"dangling doc-folder slug — no file at {readme}"));
        }
        return issues;
    }

    /// <summary>
    /// Flag project doc folders with no item pointing at them — see
    /// <see cref="OrphanProjectFolder"/>. Fix by linking an item
    /// (<c>knit project new &lt;slug&gt; --item &lt;id&gt;</c> /
    /// <c>knit set &lt;id&gt; project=&lt;slug&gt;</c>) or removing the folder
    /// (<c>knit project delete &lt;slug&gt;</c>).
    /// </summary>
    public static IReadOnlyList<OrphanProjectFolder> ScanOrphanProjectFolders(ItemStore store, string root)
    {
        var linked = store.AllItems()
            .Where(i => i.Type == ItemType.Project && !string.IsNullOrEmpty(i.Project))
            .Select(i => i.Project!)
            .ToHashSet(StringComparer.Ordinal);
        var findings = new List<OrphanProjectFolder>();
        foreach (var slug in ProjectsLayout.ProjectSlugs(root))
            if (!linked.Contains(slug))
                findings.Add(new OrphanProjectFolder(
                    ProjectsLayout.ProjectDir(root, slug), slug));
        return findings;
    }

    /// <summary>
    /// Flag <c>project</c> items with no <c>project:</c> pointer — see
    /// <see cref="OrphanProjectItem"/>. Fix by joining a doc folder
    /// (<c>knit project new &lt;slug&gt; --item &lt;id&gt;</c> /
    /// <c>knit set &lt;id&gt; project=&lt;slug&gt;</c>).
    /// </summary>
    public static IReadOnlyList<OrphanProjectItem> ScanOrphanProjectItems(ItemStore store)
    {
        var findings = new List<OrphanProjectItem>();
        foreach (var item in store.AllItems())
        {
            if (item.Type != ItemType.Project) continue;
            if (!string.IsNullOrEmpty(item.Project)) continue;
            var file = store.PathOf(item.Id) ?? string.Empty;
            findings.Add(new OrphanProjectItem(file, item.Id));
        }
        return findings;
    }

    /// <summary>
    /// Walk every project's <c>milestones/</c> folder and flag entries that
    /// don't parse as milestone sub-docs — see <see cref="MalformedMilestone"/>.
    /// Non-markdown files and sub-folders there are flagged too: doc type is
    /// implicit in location, so the folder must hold only milestone docs.
    /// </summary>
    public static IReadOnlyList<MalformedMilestone> ScanMilestoneDocs(string root, IFrontmatterSerializer yaml)
    {
        var findings = new List<MalformedMilestone>();
        foreach (var project in ProjectsLayout.ProjectSlugs(root))
        {
            var dir = ProjectsLayout.MilestonesDir(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                findings.Add(new MalformedMilestone(sub,
                    "sub-folder under milestones/ — milestones are flat <slug>.md files"));
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new MalformedMilestone(file,
                        "not a markdown file — anything under milestones/ must parse as a milestone sub-doc"));
                    continue;
                }
                var (_, problems) = MilestoneFile.Read(file, project, yaml);
                foreach (var p in problems)
                    findings.Add(new MalformedMilestone(file, p));
            }
        }
        return findings;
    }

    /// <summary>
    /// Warn on items carrying more than one <c>Milestone</c> row — see
    /// <see cref="MultipleMilestones"/>. Fix by removing the extra rows
    /// (<c>knit unlink &lt;id&gt; &lt;project&gt;/&lt;slug&gt;</c>).
    /// </summary>
    public static IReadOnlyList<MultipleMilestones> ScanMilestoneRows(ItemStore store)
    {
        var findings = new List<MultipleMilestones>();
        foreach (var item in store.AllItems())
        {
            var count = item.MilestoneRows.Count();
            if (count <= 1) continue;
            var file = store.PathOf(item.Id) ?? string.Empty;
            findings.Add(new MultipleMilestones(file, item.Id, count));
        }
        return findings;
    }

    /// <summary>
    /// Warn on <c>slate:</c> values the registry doesn't know — see
    /// <see cref="UnregisteredSlate"/>. Fix with <c>knit slate add</c> to keep
    /// the value or <c>knit slate rm</c> to clear it from the items.
    /// </summary>
    public static IReadOnlyList<UnregisteredSlate> ScanUnregisteredSlates(ItemStore store, string root)
    {
        var registered = SlateRegistry.Load(root)
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return store.AllItems()
            .Where(i => !string.IsNullOrEmpty(i.Slate) && !registered.Contains(i.Slate!))
            .GroupBy(i => i.Slate!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new UnregisteredSlate(
                g.Key,
                g.Select(i => store.PathOf(i.Id) ?? string.Empty)
                    .Order(StringComparer.Ordinal)
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// List items linking a milestone that belongs to another project — see
    /// <see cref="CrossProjectMilestone"/>. Informational only.
    /// </summary>
    public static IReadOnlyList<CrossProjectMilestone> ScanCrossProjectMilestones(ItemStore store)
    {
        var findings = new List<CrossProjectMilestone>();
        foreach (var item in store.AllItems())
        {
            if (!item.MilestoneRows.Any()) continue;
            var scope = MilestoneResolver.ProjectScopeOf(store, item);
            if (string.IsNullOrEmpty(scope)) continue;
            foreach (var row in item.MilestoneRows)
            {
                if (LinkPath.MilestoneFromTarget(row.TargetPath) is not { } id) continue;
                if (string.Equals(id.Project, scope, StringComparison.Ordinal)) continue;
                var file = store.PathOf(item.Id) ?? string.Empty;
                findings.Add(new CrossProjectMilestone(file, item.Id, scope, id.Project, id.Slug));
            }
        }
        return findings;
    }

    /// <summary>
    /// Walk <c>.knit/items/</c> at the <b>filesystem</b> level (not via the
    /// parsed <see cref="ItemStore"/>, which is exactly what silently skips
    /// these) and flag every entry that won't load as a valid item. Caught
    /// cases, each producing an <see cref="InvalidItemFile"/> with a specific
    /// reason:
    /// <list type="bullet">
    ///   <item>a subdirectory — items are flat files, the enumerator drops it
    ///   with zero signal;</item>
    ///   <item>an entry whose name doesn't start with a valid NanoID id;</item>
    ///   <item>a file that can't be read (IO error);</item>
    ///   <item>no YAML frontmatter block (missing / unterminated <c>---</c>);</item>
    ///   <item>frontmatter with no <c>id:</c>;</item>
    ///   <item><c>id:</c> that disagrees with the name-derived id (split-brain
    ///   identity — enumeration keys on the name, parsing trusts the field).</item>
    /// </list>
    /// Note: the frontmatter serializer is lenient and never throws, so
    /// "unparseable frontmatter" surfaces here as the missing-block / missing-id
    /// cases rather than an exception.
    /// </summary>
    /// <summary>
    /// An open item still blocked by a closed one — a <c>Blocked by</c> row
    /// left behind when its blocker closed. Closing resolves nothing, so this
    /// is reachable through normal use; warning-severity because the block may
    /// stand, and then the row should name a new blocker. <see cref="Sole"/>:
    /// the row is the item's only impediment (fix with <c>unblock</c>, else
    /// <c>unlink</c>).
    /// </summary>
    public sealed record StaleBlocker(
        string SourcePath,
        string ItemId,
        string BlockerId,
        ItemState BlockerState,
        bool Sole);

    /// <summary>Flag every <see cref="StaleBlocker"/>.</summary>
    public static IReadOnlyList<StaleBlocker> ScanStaleBlockers(ItemStore store) =>
        new LinkGraph(store.AllItems()).StaleBlocks()
            .Select(s => new StaleBlocker(
                store.PathOf(s.Dependent.Item.Id) ?? string.Empty,
                s.Dependent.Item.Id,
                s.Blocker.Id,
                s.Blocker.State,
                s.Dependent.Remaining is null))
            .OrderBy(s => s.SourcePath, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Flag <c>done</c> items with an open direct child — see
    /// <see cref="DoneWithOpenChild"/>. Fix by closing the child
    /// (<c>knit done</c> / <c>knit dismiss</c>) or reopening the parent
    /// (<c>knit reopen &lt;id&gt;</c>).
    /// </summary>
    public static IReadOnlyList<DoneWithOpenChild> ScanDoneWithOpenChildren(ItemStore store)
    {
        var items = store.AllItems().ToList();
        var childrenByParent = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Parent)) continue;
            if (!childrenByParent.TryGetValue(item.Parent, out var kids))
                childrenByParent[item.Parent] = kids = [];
            kids.Add(item);
        }

        var findings = new List<DoneWithOpenChild>();
        foreach (var parent in items)
        {
            if (parent.State != ItemState.Done) continue;
            if (!childrenByParent.TryGetValue(parent.Id, out var kids)) continue;
            var file = store.PathOf(parent.Id) ?? string.Empty;
            foreach (var child in kids)
            {
                if (child.State is ItemState.Done or ItemState.Dismissed) continue;
                findings.Add(new DoneWithOpenChild(file, parent.Id, child.Id, child.Type, child.State));
            }
        }
        return findings;
    }

    public static IReadOnlyList<InvalidItemFile> ScanItemFiles(string root, IFrontmatterSerializer yaml)
    {
        var results = new List<InvalidItemFile>();
        var itemsDir = RepoDiscovery.ItemsDir(root);
        if (!Directory.Exists(itemsDir)) return results;

        foreach (var file in Directory.EnumerateFiles(itemsDir, "*.md", SearchOption.TopDirectoryOnly))
            CheckEntry(file, ItemLayout.EntryName(file));

        foreach (var dir in Directory.EnumerateDirectories(itemsDir))
            results.Add(new InvalidItemFile(dir,
                "a folder in items/ — items are flat <id>-<slug>.md files; nothing in it loads or shows in list/tree"));
        return results;

        void CheckEntry(string file, string entryName)
        {
            var nameId = LinkPath.IdFromFolderName(entryName);
            var nameIdValid = NanoId.IsValid(nameId);
            if (!nameIdValid)
                results.Add(new InvalidItemFile(file,
                    $"name '{entryName}' does not start with a valid item id (18-char NanoID)"));

            string content;
            try { content = AtomicFile.ReadAllText(file); }
            catch (Exception ex) { results.Add(new InvalidItemFile(file, $"unreadable: {ex.Message}")); return; }

            var (fm, _) = SplitFrontmatter(content);
            if (fm.Length == 0)
            {
                results.Add(new InvalidItemFile(file, "no YAML frontmatter block (missing or unterminated ---)"));
                return;
            }
            var fields = yaml.Parse(fm);
            var fmId = fields.TryGetValue("id", out var raw) ? raw?.ToString()?.Trim() : null;
            if (string.IsNullOrEmpty(fmId))
            {
                results.Add(new InvalidItemFile(file, "missing 'id' in frontmatter"));
                return;
            }
            // Only compare when the name id is itself valid — otherwise the
            // bad-name finding above already covers it (avoid double-noise).
            if (nameIdValid && !string.Equals(fmId, nameId, StringComparison.OrdinalIgnoreCase))
                results.Add(new InvalidItemFile(file,
                    $"id mismatch: frontmatter '{fmId}' vs name '{nameId}'"));
        }
    }

    /// <summary>
    /// The <c>designs/</c> + <c>kb/</c> counterpart to <see cref="ScanItemFiles"/>:
    /// flag authored files that exist on disk but are invisible to the surfaces
    /// that list them, because their name / location doesn't match the
    /// addressable shape. KB + design artifacts are path/slug-identified (no
    /// NanoID), so the caught cases differ from items:
    /// <list type="bullet">
    ///   <item><b>designs:</b> a <c>.md</c> under <c>designs/</c> not reachable
    ///   by <c>design list</c> — active designs are <c>&lt;slug&gt;-design.md</c>
    ///   at the designs root, archived ones live under <c>completed/&lt;slug&gt;/</c>.
    ///   Anything else (wrong suffix, wrong folder) silently drops.</item>
    ///   <item><b>kb:</b> a <c>.md</c> whose first path segment under <c>kb/</c>
    ///   isn't a known bucket folder — the KB browser lists by iterating the
    ///   known buckets, so an orphan under <c>kb/</c> or an unknown-bucket folder
    ///   never shows. Files inside a known bucket (entries, folder-form readmes,
    ///   attachments, indexes) are never flagged.</item>
    ///   <item><b>.knit/index.jsonl:</b> a leftover derived cache nothing
    ///   reads or writes; gitignored, so only this scan would ever name it.</item>
    ///   <item><b>write temp files:</b> an <see cref="AtomicFile"/> temp left
    ///   by a write that died before its rename, anywhere under <c>.knit/</c>.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<StrayFile> ScanStrayFiles(string root)
    {
        var results = new List<StrayFile>();

        var designsDir = DesignsLayout.DesignsDir(root);
        if (Directory.Exists(designsDir))
        {
            var visible = new HashSet<string>(
                DesignsLayout.ActiveDesignDocs(root)
                    .Concat(DesignsLayout.CompletedDesignDocs(root))
                    .Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(designsDir, "*.md", SearchOption.AllDirectories))
            {
                if (visible.Contains(Path.GetFullPath(file))) continue;
                results.Add(new StrayFile(file,
                    "won't appear in 'design list' — active designs must be <slug>-design.md at the " +
                    "designs/ root; archived ones live under completed/<slug>/"));
            }
        }

        var kbDir = KbLayout.KbDir(root);
        if (Directory.Exists(kbDir))
        {
            foreach (var file in Directory.EnumerateFiles(kbDir, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(kbDir, file).Replace('\\', '/');
                var segs = rel.Split('/');
                if (segs.Length == 1)
                {
                    // The generated master TOC is legitimately at the kb/ root.
                    if (string.Equals(segs[0], KbLayout.MasterIndexFile, StringComparison.OrdinalIgnoreCase))
                        continue;
                    results.Add(new StrayFile(file,
                        "directly under kb/ with no bucket folder — not addressable as bucket:slug; " +
                        "absent from the KB browser"));
                    continue;
                }
                if (!BucketFolders.TryFromFolder(segs[0], out _))
                    results.Add(new StrayFile(file,
                        $"under unknown KB bucket folder '{segs[0]}' (valid: adrs, docs, features, logs, " +
                        "ideas, sources) — absent from the KB browser"));
            }
        }

        var projectsDir = ProjectsLayout.ProjectsDir(root);
        if (Directory.Exists(projectsDir))
        {
            // Project docs live one folder per slug — a file directly at the
            // projects/ root belongs to no project and never shows anywhere.
            foreach (var file in Directory.EnumerateFiles(projectsDir))
                results.Add(new StrayFile(file,
                    "directly under projects/ with no project folder — project docs live in projects/<slug>/"));
        }

        var jsonlIndex = Path.Combine(RepoDiscovery.KnitDir(root), "index.jsonl");
        if (File.Exists(jsonlIndex))
            results.Add(new StrayFile(jsonlIndex,
                "not read or written by knit — every view is built from the item folders; safe to delete"));

        var knitDir = RepoDiscovery.KnitDir(root);
        if (Directory.Exists(knitDir))
            foreach (var file in Directory.EnumerateFiles(knitDir, $"{AtomicFile.TempPrefix}*{AtomicFile.TempSuffix}",
                         SearchOption.AllDirectories))
                results.Add(new StrayFile(file,
                    "left by a write that was interrupted before it finished — the file beside it is intact; safe to delete"));

        return results;
    }

    /// <summary>
    /// The non-markdown files under <c>items/</c>, <c>kb/</c>, <c>designs/</c> and
    /// <c>projects/</c> (an SVG, an image, a PDF): each belongs to a body that
    /// links it, or nothing shows it. Stray when:
    /// <list type="bullet">
    ///   <item>no body links it;</item>
    ///   <item>an SVG that no body in its own folder links — the SPA draws it
    ///   only next to the doc that embeds it;</item>
    ///   <item>in <c>items/</c>: its name doesn't start with an existing item's
    ///   id (<c>&lt;id&gt;-&lt;name&gt;.&lt;ext&gt;</c>), so the item it belongs
    ///   to is unknown, or gone.</item>
    /// </list>
    /// Files in a folder under <c>items/</c> are left to
    /// <see cref="ScanItemFiles"/>, which flags the folder.
    /// </summary>
    public static IReadOnlyList<StrayFile> ScanLooseFiles(ItemStore store, string root)
    {
        var results = new List<StrayFile>();
        var linkedFrom = LinkedFiles(store, root);
        var itemIds = ItemIds(store).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itemsDir = Path.GetFullPath(store.ItemsDir);

        string[] dirs = [itemsDir, KbLayout.KbDir(root), DesignsLayout.DesignsDir(root), ProjectsLayout.ProjectsDir(root)];
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            var inItems = dir == itemsDir;
            var files = Directory.EnumerateFiles(dir, "*", inItems ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories);
            foreach (var file in files.Select(Path.GetFullPath))
            {
                if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(file).StartsWith(AtomicFile.TempPrefix, StringComparison.Ordinal)) continue;
                if (inItems && LinkPath.IdFromFolderName(Path.GetFileNameWithoutExtension(file)) is var id
                    && !itemIds.Contains(id ?? ""))
                {
                    results.Add(new StrayFile(file, NanoId.IsValid(id ?? "")
                        ? $"its item {id} is gone — delete the file, or rename it to another item's id"
                        : "next to items, a file's name starts with its item's id: <id>-<name>.<ext>"));
                    continue;
                }
                var sources = linkedFrom.GetValueOrDefault(file) ?? [];
                if (sources.Count == 0)
                    results.Add(new StrayFile(file, "no body links to it — link it from the doc it belongs to, or delete it"));
                else if (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                         && !sources.Any(s => string.Equals(Path.GetDirectoryName(s), Path.GetDirectoryName(file),
                             StringComparison.OrdinalIgnoreCase)))
                    results.Add(new StrayFile(file,
                        "an SVG sits next to the doc that embeds it — move it beside the linking doc"));
            }
        }
        return results;
    }

    /// <summary>Every existing file a body links by relative path, with the bodies that link it.</summary>
    private static Dictionary<string, List<string>> LinkedFiles(ItemStore store, string root)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ProseFiles(store, root))
        {
            string content;
            try { content = AtomicFile.ReadAllText(file); } catch { continue; }
            var sourceDir = Path.GetDirectoryName(file)!;
            var lines = content.Split('\n');
            foreach (var i in ProseLineIndexes(lines))
            foreach (Match m in LinkRe.Matches(lines[i]))
            {
                var target = m.Groups[2].Value.Trim();
                if (target.Length == 0 || target.StartsWith('#') || target.StartsWith('/') || ProtocolRe.IsMatch(target))
                    continue;
                var clean = Uri.UnescapeDataString(target.Split('#')[0].Split('?')[0]);
                if (clean.Length == 0) continue;
                string abs;
                try { abs = Path.GetFullPath(Path.Combine(sourceDir, clean)); } catch { continue; }
                if (!map.TryGetValue(abs, out var list)) map[abs] = list = [];
                list.Add(file);
            }
        }
        return map;
    }

    /// <summary>An SVG under <c>.knit/</c> that won't render safely or read well. Warning-severity.</summary>
    public sealed record SvgFinding(string SourcePath, string Reason);

    /// <summary>Every <c>.svg</c> under <c>.knit/</c>, checked by <see cref="SvgDoc.Check"/>.</summary>
    public static IReadOnlyList<SvgFinding> ScanSvgFiles(string root)
    {
        var knitDir = RepoDiscovery.KnitDir(root);
        if (!Directory.Exists(knitDir)) return [];
        var results = new List<SvgFinding>();
        foreach (var file in Directory.EnumerateFiles(knitDir, "*.svg", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string text;
            try { text = AtomicFile.ReadAllText(file); }
            catch (Exception ex) { results.Add(new SvgFinding(file, $"unreadable: {ex.Message}")); continue; }
            results.AddRange(SvgDoc.Check(text).Select(r => new SvgFinding(file, r)));
        }
        return results;
    }

    /// <summary>
    /// Directories named <c>.knit</c> inside the repo that are not the repo's
    /// own root marker. Discovery walks past them (a root needs
    /// <c>config.json</c>), so without this they are invisible — yet they
    /// shadow nothing and resolve nowhere, which is the same "silently hidden"
    /// class as <see cref="StrayFile"/>: someone unpacked something into a
    /// dotfolder that looks like a repo marker, or an <c>init</c> died halfway.
    /// Skips the heavy trees no knit repo lives in.
    /// </summary>
    public static IReadOnlyList<StrayFile> ScanShadowKnitDirs(string root)
    {
        string[] skip = [".git", "node_modules", "bin", "obj", "dist", ".vs", ".idea"];
        var results = new List<StrayFile>();
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (skip.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, RepoDiscovery.KnitDirName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(
                            Path.GetFullPath(child),
                            Path.GetFullPath(RepoDiscovery.KnitDir(root)),
                            StringComparison.OrdinalIgnoreCase))
                        results.Add(new StrayFile(child,
                            $"directory named .knit without {ConfigLoader.FileName} — " +
                            "discovery walks past it, so nothing here is addressable; " +
                            "run 'knit init' there or rename it"));
                    continue;
                }
                queue.Enqueue(child);
            }
        }

        return results;
    }

    /// <summary>
    /// A config layer that exists but whose JSON does not parse. Every reader
    /// falls back to defaults there, so the settings in the file apply
    /// nowhere while the file sits in the repo looking authoritative — the
    /// "nothing under <c>.knit/</c> is silently hidden" rule, applied to
    /// config.
    /// </summary>
    public static IReadOnlyList<StrayFile> ScanConfigLayers(string root)
    {
        var knitDir = RepoDiscovery.KnitDir(root);
        var results = new List<StrayFile>();
        foreach (var file in new[] { ConfigLoader.FileName, ConfigLoader.LocalFileName })
        {
            if (!ConfigLoader.ReadLayer(knitDir, file).Unreadable) continue;
            results.Add(new StrayFile(Path.Combine(knitDir, file),
                "config file does not parse as JSON — every setting in it is ignored " +
                "and read as its default; `knit config list` refuses to report values until it is fixed"));
        }
        return results;
    }

    public sealed record CommittedIdentity(string Path, string Key, string Value);

    /// <summary>
    /// <c>handle</c> / <c>display</c> in the committed <c>config.json</c>. The
    /// repo layer beats each person's global one, so everyone who clones the
    /// repo records as that person. A warning, not an error: in a repo
    /// only one person writes, it is merely a stale copy waiting to happen.
    /// </summary>
    public static IReadOnlyList<CommittedIdentity> ScanCommittedIdentity(string root)
    {
        var path = Path.Combine(RepoDiscovery.KnitDir(root), ConfigLoader.FileName);
        if (ConfigLoader.ReadFile(path).Node is not { } node) return [];
        return
        [
            .. new[] { "handle", "display" }
                .Select(key => (key, value: node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null))
                .Where(kv => !string.IsNullOrEmpty(kv.value))
                .Select(kv => new CommittedIdentity(path, kv.key, kv.value!)),
        ];
    }

    /// <param name="Key">The key as written in the global file, or empty when the whole file is unusable.</param>
    public sealed record GlobalConfigNote(string Path, string Key, string Reason);

    /// <summary>
    /// The user-level config's keys that apply nowhere — a repo-only key
    /// or a relative mount path someone hand-edited in, and the file itself
    /// when it does not parse. Reported, never counted: it is the user's own
    /// machine file, so failing a repo's <c>verify</c> over it would blame the
    /// wrong thing (and CI, which has no global config, would never see it).
    /// </summary>
    public static IReadOnlyList<GlobalConfigNote> ScanGlobalConfig()
    {
        var path = UserPaths.GlobalConfigFile;
        var layer = ConfigLoader.ReadGlobal();
        if (layer.Unreadable)
            return [new GlobalConfigNote(path, "", "does not parse as JSON — nothing in it applies to any repo")];
        return [.. layer.Ignored.Select(i => new GlobalConfigNote(path, i.Name, i.Reason))];
    }

    public sealed record LongConventions(string Scope, string Path, int Lines);

    /// <summary>
    /// Conventions files past <see cref="Conventions.LongLineCount"/> lines.
    /// Reported, never counted: length is a cost — every agent run reads the
    /// file — not a defect.
    /// </summary>
    public static IReadOnlyList<LongConventions> ScanConventions(string root) =>
    [
        .. Conventions.ReadAll(root)
            .Where(f => f.LineCount > Conventions.LongLineCount)
            .Select(f => new LongConventions(f.Scope.ToWire(), f.Path, f.LineCount)),
    ];

    /// <summary>
    /// Scan KB artifact frontmatter for id collisions — two files in the same
    /// bucket declaring the same <c>id</c>. Buckets whose artifacts carry no
    /// <c>id</c> (identity is the slug/path) never collide here; only
    /// id-bearing artifacts (ADRs, sources) are grouped. See
    /// <see cref="DuplicateKbId"/> for why this matters.
    /// </summary>
    public static IReadOnlyList<DuplicateKbId> ScanDuplicateKbIds(string root, IFrontmatterSerializer yaml)
    {
        var results = new List<DuplicateKbId>();
        var kbDir = KbLayout.KbDir(root);
        if (!Directory.Exists(kbDir)) return results;

        // (bucket-folder, id) → colliding file paths, preserving on-disk order.
        var groups = new Dictionary<(string Bucket, string Id), List<string>>();
        foreach (var file in KbLayout.EnumerateArtifactFiles(root))
        {
            var (fm, _) = SplitFrontmatter(AtomicFile.ReadAllText(file));
            if (fm.Length == 0) continue;
            var fields = yaml.Parse(fm);
            var id = fields.TryGetValue("id", out var raw) ? raw?.ToString()?.Trim() : null;
            if (string.IsNullOrEmpty(id)) continue;
            var rel = Path.GetRelativePath(kbDir, file).Replace('\\', '/');
            var bucket = rel.Split('/')[0];
            var key = (bucket, id);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(file);
        }
        foreach (var ((bucket, id), paths) in groups)
            if (paths.Count > 1)
                results.Add(new DuplicateKbId(bucket, id, paths));
        return results;
    }

    /// <summary>
    /// Every KB entry and design doc whose declared identity field disagrees
    /// with its filename. KB: <c>id:</c>, for the buckets that carry
    /// one — <c>source</c> (the slug) and <c>adr</c> (the number minted into
    /// the slug). Designs: <c>slug:</c>, against the <c>&lt;slug&gt;-design.md</c>
    /// name, or the archive folder for a completed cycle. A file that declares
    /// no such field is not one this can judge, and nothing is ever added.
    /// </summary>
    public static IReadOnlyList<IdentityDrift> ScanIdentityDrift(string root, IFrontmatterSerializer yaml)
    {
        var results = new List<IdentityDrift>();
        var store = new KbStore(root, yaml);
        foreach (var bucket in BucketStrings.All)
        {
            if (!KbIdentity.CarriesId(bucket)) continue;
            foreach (var (r, path) in store.ListBucket(bucket))
            {
                var expected = KbIdentity.ExpectedId(bucket, r.Slug);
                if (expected is null) continue;
                var declared = ReadField(path, yaml, "id");
                if (declared is null) continue;
                if (!string.Equals(declared, expected, StringComparison.Ordinal))
                    results.Add(new IdentityDrift(path, "id", declared, expected));
            }
        }

        foreach (var path in DesignsLayout.ActiveDesignDocs(root))
            AddDesignDrift(path, DesignsLayout.SlugFromPath(path));
        foreach (var path in DesignsLayout.CompletedDesignDocs(root))
            AddDesignDrift(path, Path.GetFileName(Path.GetDirectoryName(path)!));

        return results;

        void AddDesignDrift(string path, string expected)
        {
            if (string.IsNullOrEmpty(expected)) return;
            var declared = ReadField(path, yaml, "slug");
            if (declared is null) return;
            if (!string.Equals(declared, expected, StringComparison.Ordinal))
                results.Add(new IdentityDrift(path, "slug", declared, expected));
        }
    }

    private static string? ReadField(string path, IFrontmatterSerializer yaml, string field)
    {
        var (fm, _) = SplitFrontmatter(AtomicFile.ReadAllText(path));
        if (fm.Length == 0) return null;
        var value = yaml.Parse(fm).TryGetValue(field, out var raw) ? raw?.ToString()?.Trim() : null;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// A mount as it actually stands: every declared one plus every
    /// mount a KB source names that nothing declares. Reported <b>once</b> per
    /// mount with the number of KB sources riding on it — an unresolved mount
    /// is a single config problem, and repeating it per source would bury it.
    /// <paramref name="Source"/> is the config layer the value came from, or
    /// null when nothing declares it; <paramref name="Reason"/> is null when
    /// the mount is fine.
    /// </summary>
    public sealed record MountStatus(
        string Name,
        string? DeclaredPath,
        string EffectivePath,
        string? Source,
        bool Resolves,
        bool Portable,
        int SourceCount,
        string? Reason);

    /// <summary>
    /// Mount health, ordered by name. Config layers are passed separately so a
    /// value can be attributed to the file it came from — the merged config no
    /// longer knows. Callers that only want problems filter on
    /// <see cref="MountStatus.Reason"/>.
    /// </summary>
    public static IReadOnlyList<MountStatus> ScanMounts(
        string root, KnitConfig? globalConfig, KnitConfig? repoConfig, KnitConfig? localConfig,
        IFrontmatterSerializer yaml)
    {
        var declared = MountResolver.Describe(root, globalConfig, repoConfig, localConfig);
        var counts = CountSourcesPerMount(root, yaml);
        var rows = new List<MountStatus>();

        foreach (var m in declared)
        {
            var count = counts.GetValueOrDefault(m.Name);
            // A global mount is required to be absolute, so portability
            // is not a defect there — and one that is missing on this machine
            // is only this repo's problem if a source actually rides on it,
            // otherwise every repo would warn about someone else's directory.
            var global = m.Source == MountSource.Global;
            var reason =
                !m.Resolves ? (global && count == 0 ? null : "directory does not exist")
                : !m.Portable && !global ? "absolute path is not portable to another machine"
                : null;
            rows.Add(new MountStatus(
                m.Name,
                m.DeclaredPath,
                m.EffectivePath,
                m.Source switch
                {
                    MountSource.Global => "global",
                    MountSource.Local => ConfigLoader.LocalFileName,
                    _ => ConfigLoader.FileName,
                },
                m.Resolves,
                m.Portable,
                count,
                reason));
        }

        var names = declared.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (!names.Contains(name))
                rows.Add(new MountStatus(name, null, "", null, false, true, count, "not declared in any config"));

        return rows;
    }

    private static Dictionary<string, int> CountSourcesPerMount(string root, IFrontmatterSerializer yaml)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, fm) in SourceFrontmatters(root, yaml))
        {
            var mount = FmString(fm, "mount");
            if (string.IsNullOrEmpty(mount)) continue;
            counts[mount] = counts.GetValueOrDefault(mount) + 1;
        }
        return counts;
    }

    /// <summary>
    /// KB sources whose <c>path:</c> doesn't exist, or leaves the repo root
    /// with no mount covering it. A source on a mount that doesn't resolve is
    /// left to <see cref="ScanMounts"/> (see <see cref="ScopeOf"/>).
    /// </summary>
    public static IReadOnlyList<MissingSource> ScanSourcePaths(string root, KnitConfig? config, IFrontmatterSerializer yaml)
    {
        var issues = new List<MissingSource>();
        foreach (var (file, fm) in SourceFrontmatters(root, yaml))
        {
            var stored = FmString(fm, "path");
            if (string.IsNullOrEmpty(stored)) continue;
            var mount = FmString(fm, "mount");
            var mounted = !string.IsNullOrEmpty(mount);
            if (mounted && !MountResolver.TryResolveDir(root, config, mount!, out _, out _)) continue;

            var target = MountResolver.ResolveSource(root, config, mount, stored);
            if (target.Reason is not null)
            {
                issues.Add(new MissingSource(file, mount, stored, "", target.Reason));
                continue;
            }
            var scope = mounted ? TargetScope.Judged : ScopeOf(root, config, target.FullPath);
            if (scope == TargetScope.OutsideRepo)
                issues.Add(new MissingSource(file, mount, stored, target.FullPath, OutsideRepoReason));
            else if (scope == TargetScope.Judged && !File.Exists(target.FullPath) && !Directory.Exists(target.FullPath))
                issues.Add(new MissingSource(file, mount, stored, target.FullPath, "does not exist"));
        }
        return issues;
    }

    private static IEnumerable<(string File, IReadOnlyDictionary<string, object?> Frontmatter)> SourceFrontmatters(
        string root, IFrontmatterSerializer yaml)
    {
        var sourcesDir = Path.Combine(KbLayout.KbDir(root), BucketFolders.Sources);
        if (!Directory.Exists(sourcesDir)) yield break;
        foreach (var file in KbLayout.EnumerateArtifactFiles(root))
        {
            if (!file.StartsWith(sourcesDir, StringComparison.OrdinalIgnoreCase)) continue;
            var (fm, _) = SplitFrontmatter(AtomicFile.ReadAllText(file));
            if (fm.Length == 0) continue;
            yield return (file, yaml.Parse(fm));
        }
    }

    private static string? FmString(IReadOnlyDictionary<string, object?> fm, string key) =>
        fm.TryGetValue(key, out var raw) ? raw?.ToString()?.Trim() : null;

    private const string OutsideRepoReason = "leaves the repo root and no mount covers it";

    private enum TargetScope
    {
        /// <summary>Inside the repo root, or a mount that resolves here: a missing target is the repo's fault.</summary>
        Judged,
        /// <summary>Inside a mount that doesn't resolve here: the mount row reports it.</summary>
        MachineState,
        /// <summary>Outside the repo and every mount: works on no other machine, whether or not it exists here.</summary>
        OutsideRepo,
    }

    private static TargetScope ScopeOf(string root, KnitConfig? config, string fullPath)
    {
        var covering = MountResolver.FindCovering(root, config, fullPath);
        if (covering.Count > 0)
            return covering.All(m => m.Resolves) ? TargetScope.Judged : TargetScope.MachineState;
        return IsUnder(Path.GetFullPath(root), fullPath) ? TargetScope.Judged : TargetScope.OutsideRepo;
    }

    internal static readonly Regex CodeSpanRe = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    internal static readonly Regex LinkRe = new(@"(!?)\[[^\]]*\]\(([^)\s]+)(?:\s+[^)]*)?\)", RegexOptions.Compiled);
    internal static readonly Regex ProtocolRe = new(@"^[a-z][a-z0-9+.-]*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListItemRe = new(@"^([-*+]|\d+[.)])(\s|$)", RegexOptions.Compiled);

    /// <summary>
    /// Scan item, KB, design and project-doc <b>bodies</b> for references that
    /// rot on a rename/move/delete no write path repaired:
    /// inline <c>bucket:slug</c> code-spans (an <c>item:</c> ref must carry the
    /// full id), and relative markdown links and images — into the repo or a
    /// mount, where <see cref="ScopeOf"/> can tell. Generated index files are
    /// excluded by the enumerators. See <see cref="ProseLineIndexes"/> for the
    /// lines skipped.
    /// </summary>
    public static (IReadOnlyList<ProseRef> Refs, IReadOnlyList<ProseLink> Links) ScanProse(
        ItemStore store, string root, KnitConfig? config)
    {
        var refs = new List<ProseRef>();
        var links = new List<ProseLink>();
        var itemIds = ItemIds(store);

        var bodies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ProseFiles(store, root))
            ScanOne(file);

        return (refs, links);

        void CheckRef(string file, DocRef d)
        {
            if (d.Ref is { Bucket: Bucket.Item } r && CheckItemRef(r.Slug, itemIds) is { } finding)
            {
                refs.Add(new ProseRef(file, d.ToString(), finding.Reason, finding.Candidates));
                return;
            }
            var (path, exists) = d.Resolve(root);
            if (!exists)
            {
                refs.Add(new ProseRef(file, d.ToString(), ProseRefReasons.Unresolved, []));
                return;
            }
            if (d.SvgFile is { } svg)
            {
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(path)!, svg)))
                    refs.Add(new ProseRef(file, d.ToString(), ProseRefReasons.Unresolved, []));
                return;
            }
            if (d.Section is not { } section) return;
            if (!bodies.TryGetValue(path, out var body))
            {
                try { body = AtomicFile.ReadAllText(path); } catch { body = null; }
                bodies[path] = body;
            }
            if (body is not null && SectionCheck.Check(body, section) is { } reason)
                refs.Add(new ProseRef(file, d.ToString(), reason, []));
        }

        void ScanOne(string file)
        {
            string content;
            try { content = AtomicFile.ReadAllText(file); } catch { return; }
            var sourceDir = Path.GetDirectoryName(file)!;
            var lines = content.Split('\n');
            var knitPath = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (var h in DocSections.Outline(content))
                if (DocSections.IsPointerAnchor(h.Anchor))
                    refs.Add(new ProseRef(file, $"{knitPath}#{h.Anchor}", ProseRefReasons.ReservedAnchor, []));
            foreach (var (_, target) in MermaidLinks(lines))
                if (DocRef.TryParse(target, out var d)) CheckRef(file, d);
            foreach (var i in ProseLineIndexes(lines))
            {
                var line = lines[i];
                foreach (Match m in CodeSpanRe.Matches(line))
                    if (TryParseProseRef(m.Groups[1].Value, out var d)) CheckRef(file, d);

                foreach (Match m in LinkRe.Matches(line))
                {
                    var isImage = m.Groups[1].Value == "!";
                    var target = m.Groups[2].Value.Trim();
                    if (target.Length == 0 || target.StartsWith('#') || target.StartsWith('/')) continue;
                    if (ProtocolRe.IsMatch(target)) continue; // http(s) / mailto / other schemes
                    var clean = Uri.UnescapeDataString(target.Split('#')[0].Split('?')[0]);
                    if (clean.Length == 0) continue;
                    var abs = Path.GetFullPath(Path.Combine(sourceDir, clean));
                    var scope = ScopeOf(root, config, abs);
                    if (scope == TargetScope.OutsideRepo)
                        links.Add(new ProseLink(file, isImage, target, abs, ProseLinkReasons.OutsideRepo));
                    else if (scope == TargetScope.Judged && !File.Exists(abs) && !Directory.Exists(abs))
                        links.Add(new ProseLink(file, isImage, target, abs, ProseLinkReasons.Missing));
                }
            }
        }
    }

    /// <summary>
    /// <c>relink</c> repair for a <see cref="ProseRefReasons.ShortId"/> finding:
    /// rewrite every scanned <c>`item:&lt;prefix&gt;`</c> code-span in the file to
    /// the full id. Lines the scan skips stay untouched. Returns true when the
    /// file changed.
    /// </summary>
    public static bool ApplyItemRefExpansion(string sourcePath, string prefix, string fullId)
    {
        var lines = AtomicFile.ReadAllText(sourcePath).Split('\n');
        var changed = RewriteRefs(lines, d =>
            d.Ref is { Bucket: Bucket.Item } r && string.Equals(r.Slug, prefix, StringComparison.OrdinalIgnoreCase)
                ? d.WithRef(new Ref(Bucket.Item, fullId))
                : null);
        if (changed) AtomicFile.WriteAllText(sourcePath, string.Join('\n', lines));
        return changed;
    }

    /// <summary>
    /// Rewrites, in place, every ref the prose scan reads — code-spans and mermaid
    /// link lines — for which <paramref name="map"/> returns a replacement. True
    /// when a line changed.
    /// </summary>
    internal static bool RewriteRefs(string[] lines, Func<DocRef, DocRef?> map)
    {
        var changed = false;
        foreach (var i in ProseLineIndexes(lines))
        {
            var updated = CodeSpanRe.Replace(lines[i], m =>
                TryParseProseRef(m.Groups[1].Value, out var d) && map(d) is { } next ? $"`{next}`" : m.Value);
            if (updated == lines[i]) continue;
            lines[i] = updated;
            changed = true;
        }
        foreach (var (i, target) in MermaidLinks(lines))
        {
            if (!DocRef.TryParse(target, out var d) || map(d) is not { } next) continue;
            lines[i] = MermaidLinkRe.Replace(lines[i], m => m.Groups[1].Value + next + m.Groups[3].Value, 1);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// A code-span read as a ref. A placeholder (<c>doc:x/&lt;sub&gt;</c>) or a
    /// wildcard (<c>source:*</c>) can never name an entry, so it is not one.
    /// </summary>
    internal static bool TryParseProseRef(string span, out DocRef r)
    {
        var text = span.Trim();
        var lt = text.IndexOf('<');
        if (text.Contains('*') || (lt >= 0 && text.IndexOf('>', lt) > lt))
        {
            r = new DocRef(null, null, null);
            return false;
        }
        return DocRef.TryParse(text, out r);
    }

    // Mermaid's own link syntax: `click <id> [href] "<target>"` (flowchart,
    // gantt, class) and `link <id> "<target>"` (class). A callback click has
    // no quoted target and doesn't match.
    internal static readonly Regex MermaidLinkRe = new(
        @"^(\s*(?:click\s+\S+\s+(?:href\s+)?|link\s+\S+\s+)"")([^""]+)("")", RegexOptions.Compiled);

    /// <summary>The link lines inside <c>```mermaid</c> fences: line index and quoted target.</summary>
    internal static IEnumerable<(int Index, string Target)> MermaidLinks(string[] lines)
    {
        char? fence = null;
        var mermaid = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (fence is null)
                {
                    fence = trimmed[0];
                    mermaid = trimmed.TrimStart(fence.Value).Trim().StartsWith("mermaid", StringComparison.OrdinalIgnoreCase);
                }
                else if (trimmed[0] == fence) fence = null;
                continue;
            }
            if (fence is null || !mermaid) continue;
            var m = MermaidLinkRe.Match(lines[i]);
            if (m.Success) yield return (i, m.Groups[2].Value);
        }
    }

    private static List<string> ItemIds(ItemStore store) =>
        store.AllItemFiles()
            .Select(f => LinkPath.IdFromFolderName(ItemLayout.EntryName(f)))
            .OfType<string>()
            // Directory order is the filesystem's (alphabetical on NTFS, not on
            // ext4 / APFS); sorted, the candidate list reads the same everywhere.
            .Order(StringComparer.Ordinal)
            .ToList();

    // Only the full id names one item forever: a prefix unique today turns
    // ambiguous as the repo grows, and ItemLayout.FindFile resolves it until then.
    private static (string Reason, IReadOnlyList<string> Candidates)? CheckItemRef(string slug, IReadOnlyList<string> itemIds)
    {
        if (NanoId.IsValid(slug))
            return itemIds.Contains(slug, StringComparer.OrdinalIgnoreCase) ? null : (ProseRefReasons.Unresolved, []);
        var matches = itemIds.Where(id => id.StartsWith(slug, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            0 => (ProseRefReasons.Unresolved, []),
            1 => (ProseRefReasons.ShortId, matches),
            _ => (ProseRefReasons.AmbiguousId, matches),
        };
    }

    internal static IEnumerable<string> ProseFiles(ItemStore store, string root)
    {
        foreach (var file in store.AllItemFiles()) yield return file;
        foreach (var file in KbLayout.EnumerateArtifactFiles(root)) yield return file;
        foreach (var file in DesignsLayout.ActiveDesignDocs(root)) yield return file;
        foreach (var file in DesignsLayout.CompletedDesignDocs(root)) yield return file;
        var projectsDir = ProjectsLayout.ProjectsDir(root);
        if (!Directory.Exists(projectsDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(projectsDir, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            yield return file;
    }

    /// <summary>
    /// Indexes of the body lines the prose scan reads (a line may still end in
    /// <c>\r</c>). Skips frontmatter, fenced code, indented code — a 4-space
    /// line only after a blank line and outside a list, so nested bullets and
    /// wrapped continuations are still read — and table rows inside
    /// <c>## Links</c>, the structured case <see cref="Scan"/> covers.
    /// </summary>
    internal static IEnumerable<int> ProseLineIndexes(string[] lines)
    {
        var start = 0;
        if (lines.Length > 0 && lines[0].TrimEnd() == "---")
        {
            var close = Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
            if (close > 0) start = close + 1;
        }

        var inFence = false;
        var inIndentedCode = false;
        var inList = false;
        var inLinks = false;
        var prevBlank = true;
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                prevBlank = false;
                continue;
            }
            if (inFence) continue;
            if (trimmed.Length == 0)
            {
                prevBlank = true;
                continue;
            }

            var indented = line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t');
            if (indented && (inIndentedCode || (prevBlank && !inList)))
            {
                inIndentedCode = true;
                prevBlank = false;
                continue;
            }
            inIndentedCode = false;

            if (!indented)
            {
                if (trimmed.StartsWith('#'))
                {
                    inList = false;
                    if (trimmed.StartsWith("# ", StringComparison.Ordinal) || trimmed.StartsWith("## ", StringComparison.Ordinal))
                        inLinks = trimmed.StartsWith("## ", StringComparison.Ordinal) && trimmed[3..].Trim() == "Links";
                }
                else if (ListItemRe.IsMatch(trimmed)) inList = true;
                else if (prevBlank) inList = false;
            }
            prevBlank = false;

            if (inLinks && trimmed.StartsWith('|')) continue;
            yield return i;
        }
    }

    /// <summary>
    /// Best-effort relink candidate for a broken <c>## Links</c> target:
    /// extract a uuid from the old path and find a current item with the
    /// same id, else match a KB artifact by basename. Returns the new
    /// (source-relative) target string or null when ambiguous / missing.
    /// Mirrors <c>knit relink</c>.
    /// </summary>
    public static string? ResolveRelinkCandidate(ItemStore store, string root, string sourceFile, string targetPath)
    {
        var sourceDir = Path.GetDirectoryName(sourceFile)!;
        var hintId = LinkPath.ItemIdFromTarget(targetPath);
        if (hintId is not null)
        {
            var targetFile = store.FindItemFile(hintId);
            if (targetFile is not null)
                return LinkPath.Relative(sourceDir, targetFile);
        }
        var basename = Path.GetFileName(targetPath);
        var matches = KbLayout.EnumerateArtifactFiles(root)
            .Where(f => string.Equals(Path.GetFileName(f), basename, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
            return LinkPath.Relative(sourceDir, matches[0]);
        return null;
    }

    /// <summary>Rewrite a single <c>](oldTarget)</c> occurrence in a source
    /// file to <c>](newTarget)</c>. Returns true when the file changed.</summary>
    public static bool ApplyRelink(string sourcePath, string oldTarget, string newTarget)
    {
        var content = AtomicFile.ReadAllText(sourcePath);
        var updated = content.Replace($"]({oldTarget})", $"]({newTarget})");
        if (updated == content) return false;
        AtomicFile.WriteAllText(sourcePath, updated);
        return true;
    }

    /// <summary>
    /// Repair for <see cref="IdentityDrift"/>: rewrite the declared identity
    /// field to the value its filename implies. Rewrites the one frontmatter
    /// line and leaves the rest of the file alone — the same surgical stance
    /// as <see cref="ApplyRelink"/>. Returns false when the line isn't there.
    /// </summary>
    public static bool ApplyIdentityRepair(string sourcePath, string field, string expected, IFrontmatterSerializer yaml)
    {
        var content = AtomicFile.ReadAllText(sourcePath).Replace("\r\n", "\n");
        var lines = content.Split('\n');
        if (lines.Length == 0 || lines[0] != "---") return false;
        var prefix = field + ":";
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] == "---") break;
            if (!lines[i].StartsWith(prefix, StringComparison.Ordinal)) continue;
            // Formatted by the serializer, not by hand: an ADR id is all
            // digits and must stay quoted or it reads back as a number.
            var replacement = yaml.Write(new Dictionary<string, object?> { [field] = expected }).Trim();
            if (lines[i] == replacement) return false;
            lines[i] = replacement;
            AtomicFile.WriteAllText(sourcePath, string.Join('\n', lines));
            return true;
        }
        return false;
    }

    private static bool IsUnder(string root, string candidate)
    {
        var sep = Path.DirectorySeparatorChar.ToString();
        var rootWithSep = root.EndsWith(sep, StringComparison.Ordinal) ? root : root + sep;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(rootWithSep, cmp) || string.Equals(candidate, root, cmp);
    }

    private static (string fm, string body) SplitFrontmatter(string content)
    {
        var s = content.Replace("\r\n", "\n");
        if (!s.StartsWith("---", StringComparison.Ordinal)) return ("", s);
        var endIdx = s.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return ("", s);
        var fm = s.Substring(3, endIdx - 3).TrimStart('\n');
        var body = s[(endIdx + 4)..].TrimStart('\n');
        return (fm, body);
    }

    private static string? FirstBodyH1(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (var raw in body.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Only a true h1 — "## " is h2, must reject.
            if (raw.StartsWith("# ", StringComparison.Ordinal))
                return raw[2..];
            return null;
        }
        return null;
    }
}
