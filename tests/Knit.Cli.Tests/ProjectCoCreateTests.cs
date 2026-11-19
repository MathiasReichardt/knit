using System.Text.Json;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Cli.Tests;

/// <summary>
/// The tightened two-halves join. <c>knit project new</c>
/// co-creates the doc folder <em>and</em> a linked <c>project</c> item in one
/// action (or links an existing item via <c>--item</c>); verify surfaces both
/// orphan directions — a doc folder with no item, and a project item with no
/// folder — as warnings.
/// </summary>
[Collection(nameof(CliCollection))]
public class ProjectCoCreateTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static ItemStore Store(TempKnitRoot root) => new(root.Path, new SimpleFrontmatterSerializer());

    private static string ReadmePath(TempKnitRoot root, string slug) =>
        Path.Combine(root.Path, ".knit", "projects", slug, "readme.md");

    private static JsonElement Verify(TempKnitRoot root, out int exitCode)
    {
        var r = root.Run("verify", "--json");
        exitCode = r.ExitCode;
        return JsonDocument.Parse(r.Stdout).RootElement.Clone();
    }

    // ---- co-create (default) ----

    [Fact]
    public void Project_new_co_creates_joined_item_and_folder()
    {
        using var root = Init();
        var r = root.Run("project", "new", "relaunch", "--title", "Relaunch", "--json");
        Assert.Equal(0, r.ExitCode);
        var el = JsonDocument.Parse(r.Stdout).RootElement;
        var id = el.GetProperty("item_id").GetString()!;
        Assert.True(el.GetProperty("created_item").GetBoolean());
        Assert.True(File.Exists(ReadmePath(root, "relaunch")));

        var store = Store(root);
        var item = store.Read(id)!;
        Assert.Equal("relaunch", item.Project);
        Assert.Equal(Knit.Core.Domain.ItemType.Project, item.Type);

        // Joined → neither orphan direction fires.
        var v = Verify(root, out var exit);
        Assert.Equal(0, exit);
        Assert.Empty(v.GetProperty("orphan_project_folders").EnumerateArray());
        Assert.Empty(v.GetProperty("orphan_project_items").EnumerateArray());
    }

    [Fact]
    public void Project_new_links_existing_item()
    {
        using var root = Init();
        var addId = JsonDocument.Parse(root.Run("add", "project", "Relaunch", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

        var r = root.Run("project", "new", "relaunch", "--item", addId, "--json");
        Assert.Equal(0, r.ExitCode);
        var el = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal(addId, el.GetProperty("item_id").GetString());
        Assert.False(el.GetProperty("created_item").GetBoolean());

        var store = Store(root);
        Assert.Equal("relaunch", store.Read(addId)!.Project);
        // Exactly one project item — no stray second one was minted.
        Assert.Single(store.AllItems(), i => i.Type == Knit.Core.Domain.ItemType.Project);

        var v = Verify(root, out var exit);
        Assert.Equal(0, exit);
        Assert.Empty(v.GetProperty("orphan_project_folders").EnumerateArray());
        Assert.Empty(v.GetProperty("orphan_project_items").EnumerateArray());
    }

    [Fact]
    public void Project_new_item_rejects_non_project_target()
    {
        using var root = Init();
        var taskId = JsonDocument.Parse(root.Run("add", "task", "t", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

        var r = root.Run("project", "new", "relaunch", "--item", taskId);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("only a project item", r.Stdout + r.Stderr);
        // Atomic: the rejected link left no folder behind.
        Assert.False(File.Exists(ReadmePath(root, "relaunch")));
    }

    [Fact]
    public void Project_new_no_item_and_item_are_mutually_exclusive()
    {
        using var root = Init();
        var id = JsonDocument.Parse(root.Run("add", "project", "p", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var r = root.Run("project", "new", "relaunch", "--no-item", "--item", id);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("mutually exclusive", r.Stdout + r.Stderr);
    }

    // ---- orphan detection (both directions; default warn) ----

    [Fact]
    public void Verify_warns_orphan_folder_with_no_item()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--no-item").ExitCode);

        var v = Verify(root, out var exit);
        Assert.Equal(0, exit); // warning-only → not a hard failure
        var f = Assert.Single(v.GetProperty("orphan_project_folders").EnumerateArray().ToList());
        Assert.Equal("relaunch", f.GetProperty("slug").GetString());

        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);
        Assert.Contains("orphan project doc folders", root.Run("verify").Stdout);
    }

    [Fact]
    public void Verify_warns_project_item_with_no_folder()
    {
        using var root = Init();
        var id = JsonDocument.Parse(root.Run("add", "project", "Relaunch", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

        var v = Verify(root, out var exit);
        Assert.Equal(0, exit); // warning-only
        var f = Assert.Single(v.GetProperty("orphan_project_items").EnumerateArray().ToList());
        Assert.Equal(id, f.GetProperty("item_id").GetString());

        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);
        Assert.Contains("project items without a doc folder", root.Run("verify").Stdout);
    }

    // ---- direct ProjectCreator coverage ----

    [Fact]
    public void ProjectCreator_conflicts_when_item_already_links_elsewhere()
    {
        using var root = Init();
        var store = Store(root);
        // A project item already pointing at another slug.
        var existing = store.Read(JsonDocument.Parse(root.Run("add", "project", "P", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!)!;
        store.Write(existing with { Project = "other" });

        var result = ProjectCreator.Create(
            store, root.Path, "ci", "relaunch", "Relaunch",
            roadmap: false, force: false, linkItemId: existing.Id);

        Assert.True(result.IsError);
        Assert.Contains("already points at project 'other'", result.GetErrorOrDefault()!.Message);
        // Atomic: no folder scaffolded for the rejected link.
        Assert.False(File.Exists(ReadmePath(root, "relaunch")));
    }
}
