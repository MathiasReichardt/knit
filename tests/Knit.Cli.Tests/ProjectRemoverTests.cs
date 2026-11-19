using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;
using Knit.Data;

namespace Knit.Cli.Tests;

/// <summary>
/// Direct unit coverage of <see cref="ProjectRemover"/>: the
/// re-root-not-cascade semantics (only <em>direct</em> children detach) and
/// the folder-only project (no tracker item) case.
/// </summary>
public class ProjectRemoverTests
{
    [Fact]
    public void Reroots_direct_children_but_keeps_grandchildren()
    {
        var (root, store) = CreateRepo();
        try
        {
            SeedFolder(root, "relaunch");
            var projId = Write(store, "project", "Program", project: "relaunch");
            var childId = WriteChild(store, "feature", "Child", parentId: projId);
            var grandId = WriteChild(store, "task", "Grand", parentId: childId);

            var result = ProjectRemover.Remove(store, root, "relaunch", scrubLinks: false);

            Assert.Equal(projId, result.RemovedItemId);
            Assert.Equal([childId], result.ReRootedChildIds);
            Assert.True(result.FolderDeleted);
            Assert.Null(store.Read(projId));                    // item gone
            Assert.Null(store.Read(childId)!.Parent);           // direct child re-rooted
            Assert.Equal(childId, store.Read(grandId)!.Parent); // grandchild keeps its parent
            Assert.False(Directory.Exists(ProjectsLayout.ProjectDir(root, "relaunch")));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Removes_folder_only_project_without_a_tracker_item()
    {
        var (root, store) = CreateRepo();
        try
        {
            SeedFolder(root, "docs-only");
            var result = ProjectRemover.Remove(store, root, "docs-only", scrubLinks: false);

            Assert.Null(result.RemovedItemId);
            Assert.Empty(result.ReRootedChildIds);
            Assert.True(result.FolderDeleted);
            Assert.False(Directory.Exists(ProjectsLayout.ProjectDir(root, "docs-only")));
        }
        finally { TryDelete(root); }
    }

    private static (string Root, ItemStore Store) CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-remover-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "ci", Display = "CI" });
        return (root, new ItemStore(root, new SimpleFrontmatterSerializer()));
    }

    private static void SeedFolder(string root, string slug)
    {
        var dir = Path.Combine(root, ".knit", "projects", slug, "milestones");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(root, ".knit", "projects", slug, "readme.md"), $"# {slug}\n");
    }

    private static string Write(ItemStore store, string type, string title, string? project = null)
    {
        var id = store.NewItemId();
        store.Write(new Item
        {
            Id = id, Title = title, Type = EnumStrings.TryParseItemType(type, out var t) ? t : ItemType.Task,
            State = ItemState.Todo, Created = DateTimeOffset.UtcNow, Body = $"# {title}\n", Project = project,
        });
        return id;
    }

    private static string WriteChild(ItemStore store, string type, string title, string parentId)
    {
        var parentFile = store.FindItemFile(parentId)!;
        var id = store.NewItemId();
        store.Write(new Item
        {
            Id = id, Title = title, Type = EnumStrings.TryParseItemType(type, out var t) ? t : ItemType.Task,
            State = ItemState.Todo, Created = DateTimeOffset.UtcNow, Body = $"# {title}\n",
            LinkRows = [new LinkRow(LinkRel.Parent, "parent", LinkPath.Relative(store.ItemsDir, parentFile), null)],
        });
        return id;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
