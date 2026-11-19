using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;

namespace Knit.Data;

/// <summary>
/// Resolves milestone refs: the qualified form
/// <c>&lt;project&gt;/&lt;slug&gt;</c> addresses any project's milestone;
/// a bare <c>&lt;slug&gt;</c> resolves within an item's own project (the
/// nearest ancestor of type <c>project</c> → its <c>project:</c> pointer)
/// when a context item is given, else globally — erroring when ambiguous.
/// Also hosts the reverse index over <c>Milestone</c> link rows (backlinks).
/// </summary>
public static class MilestoneResolver
{
    public sealed record Target(string ProjectSlug, string Slug, string Path)
    {
        public string QualifiedSlug => $"{ProjectSlug}/{Slug}";
    }

    public static (Target? Target, string? Error) Resolve(
        ItemStore store, string root, Item? context, string refText)
    {
        var s = (refText ?? string.Empty).Trim();
        if (s.Length == 0) return (null, "empty milestone ref");

        var slash = s.IndexOf('/');
        if (slash >= 0)
        {
            var project = s[..slash];
            var slug = s[(slash + 1)..];
            if (project.Length == 0 || slug.Length == 0 || slug.Contains('/'))
                return (null, $"malformed milestone ref '{s}' — expected <slug> or <project>/<slug>");
            var path = ProjectsLayout.MilestonePath(root, project, slug);
            return File.Exists(path)
                ? (new Target(project, slug, path), null)
                : (null, $"no milestone '{s}' (expected {path})");
        }

        // Bare slug: prefer the context item's own project scope.
        if (context is not null && ProjectScopeOf(store, context) is { } scope)
        {
            var path = ProjectsLayout.MilestonePath(root, scope, s);
            return File.Exists(path)
                ? (new Target(scope, s, path), null)
                : (null, $"no milestone '{s}' in project '{scope}' (expected {path}); use <project>/<slug> to link across projects");
        }

        // Global scan — unambiguous match required.
        var candidates = ProjectsLayout.ProjectSlugs(root)
            .Where(p => File.Exists(ProjectsLayout.MilestonePath(root, p, s)))
            .ToList();
        return candidates.Count switch
        {
            0 => (null, $"no milestone '{s}' in any project under .knit/projects/"),
            1 => (new Target(candidates[0], s, ProjectsLayout.MilestonePath(root, candidates[0], s)), null),
            _ => (null, $"milestone '{s}' is ambiguous (in projects: {string.Join(", ", candidates)}); use <project>/<slug>"),
        };
    }

    /// <summary>
    /// The doc-folder slug the item's project scope points at: walk self +
    /// ancestors to the first item of type <c>project</c> and return its
    /// <c>project:</c> field. Null when the item is outside any project
    /// subtree or the project item carries no pointer. Cycle-safe.
    /// </summary>
    public static string? ProjectScopeOf(ItemStore store, Item item)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = item;
        while (current is not null && seen.Add(current.Id))
        {
            if (current.Type == ItemType.Project)
                return string.IsNullOrEmpty(current.Project) ? null : current.Project;
            current = string.IsNullOrEmpty(current.Parent) ? null : store.Read(current.Parent);
        }
        return null;
    }

    /// <summary>
    /// Backlinks: every item carrying a <c>Milestone</c> row that resolves
    /// to the given milestone identity.
    /// </summary>
    public static IReadOnlyList<Item> LinkedItems(ItemStore store, string projectSlug, string slug)
    {
        var result = new List<Item>();
        foreach (var item in store.AllItems())
        {
            foreach (var row in item.MilestoneRows)
            {
                if (LinkPath.MilestoneFromTarget(row.TargetPath) is { } id
                    && string.Equals(id.Project, projectSlug, StringComparison.Ordinal)
                    && string.Equals(id.Slug, slug, StringComparison.Ordinal))
                {
                    result.Add(item);
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Dated milestones for the timeline overlay: every project's
    /// milestones that carry a target date, optionally narrowed to a single
    /// (project, slug) when the timeline's <c>--milestone</c> filter is set,
    /// and/or to one project when <c>--project</c> is. Undated milestones have
    /// no axis position and are omitted. Sorted by target, then qualified
    /// slug, for a stable overlay order.
    /// </summary>
    public static IReadOnlyList<MilestoneDoc> DatedMilestones(
        string root, IFrontmatterSerializer yaml,
        (string Project, string Slug)? only = null, string? projectScope = null)
    {
        var result = new List<MilestoneDoc>();
        foreach (var project in ProjectsLayout.ProjectSlugs(root))
        {
            if (only is { } o && !string.Equals(o.Project, project, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrEmpty(projectScope) && !string.Equals(projectScope, project, StringComparison.Ordinal)) continue;
            foreach (var file in ProjectsLayout.MilestoneFiles(root, project))
            {
                var (doc, _) = MilestoneFile.Read(file, project, yaml);
                if (doc.Target is null) continue;
                if (only is { } o2 && !string.Equals(o2.Slug, doc.Slug, StringComparison.Ordinal)) continue;
                result.Add(doc);
            }
        }
        result.Sort((a, b) =>
        {
            var byTarget = Comparer<DateOnly?>.Default.Compare(a.Target, b.Target);
            return byTarget != 0 ? byTarget : string.CompareOrdinal(a.QualifiedSlug, b.QualifiedSlug);
        });
        return result;
    }

    /// <summary>
    /// Item id → its milestone's target date, for every item whose
    /// <b>effective</b> milestone (own link or inherited, see
    /// <see cref="MilestoneInheritance"/>) is dated. A milestone target
    /// is an implied deadline the item — and, transitively, its blockers — must
    /// meet; this map feeds <see cref="DeadlineRisk.Compute"/>. Items in an
    /// undated milestone or in none are absent.
    /// </summary>
    public static IReadOnlyDictionary<string, DateOnly> MilestoneDeadlines(
        string root, IFrontmatterSerializer yaml, IReadOnlyDictionary<string, MilestoneInheritance.Effective> effective)
    {
        var targets = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var m in DatedMilestones(root, yaml))
            targets[m.QualifiedSlug] = m.Target!.Value;

        var result = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var (id, e) in effective)
            if (targets.TryGetValue(e.QualifiedSlug, out var target))
                result[id] = target;
        return result;
    }
}
