using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Paths;
using Knit.Core.Projects;

namespace Knit.Data;

/// <summary>
/// Removes a project: deletes the project <em>item</em> and its doc folder
/// (<c>.knit/projects/&lt;slug&gt;/</c>), but <strong>detaches</strong> direct
/// children (clears their parent so they surface at root) rather than
/// cascading the subtree — a root-level item is a valid, first-class state.
/// Deeper descendants keep their parent (only the direct children re-root).
/// Back-references pointing at the project — <c>Milestone</c> rows targeting
/// its sub-docs and relation rows targeting the item — are left <em>dangling</em>
/// by default (matching <c>knit delete</c> and the "nothing vanishes"
/// verify/doctor invariant); pass <c>scrubLinks</c> to actively remove them.
/// Shared by the CLI (<c>knit project delete</c>) and the web
/// <c>DELETE /api/projects/{slug}</c> so both surfaces behave identically.
/// </summary>
public static class ProjectRemover
{
    /// <summary>Preview of what removing <paramref name="slug"/> would touch.</summary>
    public sealed record Impact(
        string? ItemId,
        IReadOnlyList<string> DirectChildIds,
        IReadOnlyList<string> DanglingItemIds,
        int MilestoneCount,
        int FileCount,
        bool FolderExists);

    public sealed record Result(
        string? RemovedItemId,
        IReadOnlyList<string> ReRootedChildIds,
        IReadOnlyList<string> ScrubbedItemIds,
        IReadOnlyList<string> DanglingItemIds,
        int DeletedMilestoneCount,
        int DeletedFileCount,
        bool FolderDeleted);

    public static Impact Preview(ItemStore store, string root, string slug)
    {
        var items = store.AllItems().ToList();
        var projectItem = FindProjectItem(items, slug);
        var childIds = DirectChildIds(items, projectItem?.Id);
        var dangling = DanglingItems(items, projectItem?.Id, slug)
            .Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

        var dir = ProjectsLayout.ProjectDir(root, slug);
        var folderExists = Directory.Exists(dir);
        return new Impact(
            projectItem?.Id,
            childIds,
            dangling,
            ProjectsLayout.MilestoneFiles(root, slug).Count,
            folderExists ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0,
            folderExists);
    }

    public static Result Remove(ItemStore store, string root, string slug, bool scrubLinks)
    {
        var items = store.AllItems().ToList();
        var projectItem = FindProjectItem(items, slug);
        var childIds = DirectChildIds(items, projectItem?.Id).ToHashSet(StringComparer.Ordinal);
        var danglingIds = DanglingItems(items, projectItem?.Id, slug)
            .Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var reRooted = new List<string>();
        var scrubbed = new List<string>();

        // One write per affected item: direct children are always detached;
        // dangling-link holders are additionally scrubbed when requested. The
        // two sets can overlap (a detached child may also carry a milestone
        // link to this project), so merge them before writing.
        var affectedIds = new HashSet<string>(childIds, StringComparer.Ordinal);
        if (scrubLinks) affectedIds.UnionWith(danglingIds);

        foreach (var id in affectedIds)
        {
            var updated = items.First(i => string.Equals(i.Id, id, StringComparison.Ordinal));
            if (childIds.Contains(id))
            {
                updated = updated with
                {
                    Parent = null,
                    LinkRows = updated.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList(),
                };
                reRooted.Add(id);
            }
            if (scrubLinks && danglingIds.Contains(id))
            {
                updated = updated with { LinkRows = ScrubRows(updated.LinkRows, projectItem?.Id, slug) };
                scrubbed.Add(id);
            }
            store.Write(updated);
        }

        var removedItemId = projectItem is not null && store.Delete(projectItem.Id) ? projectItem.Id : null;
        var (folderDeleted, milestoneCount, fileCount) = DeleteFolder(root, slug);

        return new Result(
            removedItemId,
            reRooted.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            scrubbed.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            scrubLinks ? [] : danglingIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            milestoneCount,
            fileCount,
            folderDeleted);
    }

    private static Item? FindProjectItem(IReadOnlyList<Item> items, string slug) =>
        items.FirstOrDefault(i => string.Equals(i.Project, slug, StringComparison.Ordinal));

    private static List<string> DirectChildIds(IReadOnlyList<Item> items, string? projectItemId) =>
        projectItemId is null
            ? []
            : items.Where(i => string.Equals(i.Parent, projectItemId, StringComparison.Ordinal))
                   .Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

    private static IEnumerable<Item> DanglingItems(IReadOnlyList<Item> items, string? projectItemId, string slug)
    {
        foreach (var item in items)
        {
            // The project item is deleted with the project; its own rows can't dangle.
            if (projectItemId is not null && string.Equals(item.Id, projectItemId, StringComparison.Ordinal))
                continue;
            var hasMilestone = item.MilestoneRows.Any(r =>
                LinkPath.MilestoneFromTarget(r.TargetPath) is { } id
                && string.Equals(id.Project, slug, StringComparison.Ordinal));
            // Parent rows targeting the project item belong to direct children
            // (re-rooted by detach), so they are not counted as dangling.
            var hasRelation = projectItemId is not null && item.LinkRows.Any(r =>
                r.Rel != LinkRel.Parent
                && string.Equals(LinkPath.ItemIdFromTarget(r.TargetPath), projectItemId, StringComparison.Ordinal));
            if (hasMilestone || hasRelation) yield return item;
        }
    }

    private static IReadOnlyList<LinkRow> ScrubRows(IReadOnlyList<LinkRow> rows, string? projectItemId, string slug) =>
        rows.Where(r =>
        {
            if (r.Rel == LinkRel.Milestone
                && LinkPath.MilestoneFromTarget(r.TargetPath) is { } id
                && string.Equals(id.Project, slug, StringComparison.Ordinal))
                return false;
            if (projectItemId is not null && r.Rel != LinkRel.Parent
                && string.Equals(LinkPath.ItemIdFromTarget(r.TargetPath), projectItemId, StringComparison.Ordinal))
                return false;
            return true;
        }).ToList();

    private static (bool Deleted, int Milestones, int Files) DeleteFolder(string root, string slug)
    {
        var dir = Path.GetFullPath(ProjectsLayout.ProjectDir(root, slug));
        if (!Directory.Exists(dir)) return (false, 0, 0);
        // Never delete outside .knit/projects/, the projects root itself
        // (malformed/empty slug), or through a committed symlink.
        var projectsDir = Path.GetFullPath(ProjectsLayout.ProjectsDir(root));
        if (string.Equals(dir, projectsDir, StringComparison.Ordinal)
            || !PathContainment.IsContained(projectsDir, dir)
            || PathContainment.HasSymlinkBelow(projectsDir, dir))
            return (false, 0, 0);

        var milestones = ProjectsLayout.MilestoneFiles(root, slug).Count;
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(dir, recursive: true);
        return (true, milestones, files);
    }
}
