using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// A blocked open ancestor holds its whole subtree: nothing under
/// it is ready, rows and `show` say which ancestor holds it, and unblocking the
/// ancestor releases them. Nothing is written to the descendants.
/// </summary>
[Collection(nameof(CliCollection))]
public class HeldByBlockedParentTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var id = JsonDocument.Parse(root.Run("add", type, title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        if (parent is not null) Assert.Equal(0, root.Run("link", id, "--parent", parent).ExitCode);
        return id;
    }

    private static List<string> ReadyIds(TempKnitRoot root, params string[] extra)
    {
        var r = root.Run(["ready", "--json", .. extra]);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!).ToList();
    }

    [Fact]
    public void A_blocked_feature_holds_its_tasks_until_unblocked()
    {
        using var root = Init();
        var feature = Add(root, "feature", "phase 2");
        var task = Add(root, "task", "phase 2 task", feature);
        Assert.Contains(task, ReadyIds(root, "--under", feature));

        Assert.Equal(0, root.Run("block", feature, "--reason", "phase 1 first").ExitCode);
        Assert.Empty(ReadyIds(root, "--under", feature));
        Assert.Equal("null", root.Run("next", "--json").Stdout.Trim());

        Assert.Equal(0, root.Run("unblock", feature, "--json").ExitCode);
        Assert.Contains(task, ReadyIds(root, "--under", feature));
    }

    [Fact]
    public void A_blocked_epic_holds_two_levels_down()
    {
        using var root = Init();
        var epic = Add(root, "epic", "epic");
        var feature = Add(root, "feature", "feature", epic);
        var task = Add(root, "task", "deep task", feature);
        var blocker = Add(root, "task", "blocker");
        Assert.Equal(0, root.Run("block", epic, "--by", blocker).ExitCode);

        Assert.DoesNotContain(task, ReadyIds(root));

        var show = JsonDocument.Parse(root.Run("show", task, "--json").Stdout).RootElement;
        Assert.Equal(epic, show.GetProperty("held_by").GetString());
        Assert.False(show.TryGetProperty("blocked", out _));
        Assert.Contains("blocked parent", root.Run("show", task).Stdout);

        var listed = JsonDocument.Parse(root.Run("list", "--json").Stdout).RootElement.GetProperty("items")
            .EnumerateArray().Single(i => i.GetProperty("id").GetString() == task);
        Assert.Equal(epic, listed.GetProperty("held_by").GetString());
        Assert.Contains("⊘↑", root.Run("tree").Stdout);
    }

    [Fact]
    public void A_closed_blocked_ancestor_holds_nothing()
    {
        using var root = Init();
        var feature = Add(root, "feature", "feature");
        var task = Add(root, "task", "task", feature);
        Assert.Equal(0, root.Run("block", feature, "--reason", "stale").ExitCode);
        Assert.Equal(0, root.Run("dismiss", feature).ExitCode);

        Assert.Contains(task, ReadyIds(root));
        Assert.False(JsonDocument.Parse(root.Run("show", task, "--json").Stdout).RootElement
            .TryGetProperty("held_by", out _));
    }

    [Fact]
    public void Unblock_on_a_held_item_names_the_ancestor()
    {
        using var root = Init();
        var feature = Add(root, "feature", "phase 2");
        var task = Add(root, "task", "task", feature);
        Assert.Equal(0, root.Run("block", feature, "--reason", "phase 1 first").ExitCode);

        var r = root.Run("unblock", task, "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("phase 2", r.Stderr);
    }
}
