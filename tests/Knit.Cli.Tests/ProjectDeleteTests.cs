using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit project delete`: removes the project item + doc folder,
/// re-roots (does not cascade) direct children, and leaves back-references
/// dangling by default (or scrubs them with --scrub-links).
/// </summary>
[Collection(nameof(CliCollection))]
public class ProjectDeleteTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var args = parent is null
            ? new[] { "add", type, title, "--json" }
            : new[] { "add", type, title, "--parent", parent, "--json" };
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static string SeedProjectWithLinks(TempKnitRoot root, out string childId, out string linkedId)
    {
        // Co-create the joined project (item + folder + link) in one action.
        var created = root.Run("project", "new", "relaunch", "--title", "Relaunch", "--roadmap", "--json");
        Assert.Equal(0, created.ExitCode);
        var projId = JsonDocument.Parse(created.Stdout).RootElement.GetProperty("item_id").GetString()!;
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        childId = Add(root, "feature", "Child", projId);
        linkedId = Add(root, "task", "Linked");
        Assert.Equal(0, root.Run("link", linkedId, "--milestone", "relaunch/beta", "--json").ExitCode);
        return projId;
    }

    [Fact]
    public void Delete_removes_item_and_folder_reroots_children_leaves_links_dangling()
    {
        using var root = Init();
        var projId = SeedProjectWithLinks(root, out var childId, out var linkedId);

        var del = root.Run("project", "delete", "relaunch", "-y", "--json");
        Assert.Equal(0, del.ExitCode);
        var d = JsonDocument.Parse(del.Stdout).RootElement;
        Assert.Equal(projId, d.GetProperty("removed_item_id").GetString());
        Assert.Equal(childId, Assert.Single(d.GetProperty("re_rooted_child_ids").EnumerateArray()).GetString());
        Assert.Equal(linkedId, Assert.Single(d.GetProperty("dangling_item_ids").EnumerateArray()).GetString());
        Assert.Empty(d.GetProperty("scrubbed_item_ids").EnumerateArray());
        Assert.Equal(1, d.GetProperty("milestone_count").GetInt32());
        Assert.True(d.GetProperty("folder_deleted").GetBoolean());

        // Doc folder gone.
        var list = root.Run("project", "list", "--json");
        Assert.Equal(0, list.ExitCode);
        Assert.Empty(JsonDocument.Parse(list.Stdout).RootElement.EnumerateArray());

        // Project item gone; direct child + linked item survive.
        Assert.NotEqual(0, root.Run("show", projId, "--json").ExitCode);
        Assert.Equal(0, root.Run("show", childId, "--json").ExitCode);
        Assert.Equal(0, root.Run("show", linkedId, "--json").ExitCode);
    }

    [Fact]
    public void Delete_with_scrub_links_removes_back_references()
    {
        using var root = Init();
        SeedProjectWithLinks(root, out _, out var linkedId);

        var del = root.Run("project", "delete", "relaunch", "-y", "--scrub-links", "--json");
        Assert.Equal(0, del.ExitCode);
        var d = JsonDocument.Parse(del.Stdout).RootElement;
        Assert.Equal(linkedId, Assert.Single(d.GetProperty("scrubbed_item_ids").EnumerateArray()).GetString());
        Assert.Empty(d.GetProperty("dangling_item_ids").EnumerateArray());
    }

    [Fact]
    public void Delete_unknown_project_is_not_found()
    {
        using var root = Init();
        var r = root.Run("project", "delete", "nope", "-y", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no project", r.Stdout + r.Stderr);
    }
}
