using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// The health signal was settable, validated and in
/// `list --json`, but rendered by no human view and matched by no filter.
/// </summary>
[Collection(nameof(CliCollection))]
public class HealthViewTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string type, string title) =>
        JsonDocument.Parse(root.Run("add", type, title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

    [Fact]
    public void List_and_tree_render_the_signal()
    {
        using var root = Init();
        var atRisk = Add(root, "project", "Shaky");
        Add(root, "project", "Quiet");
        Assert.Equal(0, root.Run("set", atRisk, "health=at-risk").ExitCode);

        var list = root.Run("list", "--no-color");
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("at-risk", list.Stdout);

        var tree = root.Run("tree", "--no-color");
        Assert.Equal(0, tree.ExitCode);
        Assert.Contains("at-risk", tree.Stdout);

        // No signal renders as nothing at all — not as on-track.
        Assert.DoesNotContain("on-track", list.Stdout + tree.Stdout);
    }

    [Fact]
    public void Health_filter_matches_only_the_named_signals()
    {
        using var root = Init();
        var risky = Add(root, "project", "Risky");
        var sunk = Add(root, "project", "Sunk");
        var fine = Add(root, "project", "Fine");
        var unset = Add(root, "project", "Unset");
        Assert.Equal(0, root.Run("set", risky, "health=at-risk").ExitCode);
        Assert.Equal(0, root.Run("set", sunk, "health=off-track").ExitCode);
        Assert.Equal(0, root.Run("set", fine, "health=on-track").ExitCode);

        var ids = Ids(root, "list", "--health", "at-risk", "--health", "off-track");
        Assert.Equal(2, ids.Count);
        Assert.Contains(risky, ids);
        Assert.Contains(sunk, ids);
        Assert.DoesNotContain(fine, ids);
        // Unset is not an assessment, so it never matches a health filter.
        Assert.DoesNotContain(unset, ids);
    }

    [Fact]
    public void Tree_health_filter_keeps_ancestor_context()
    {
        using var root = Init();
        var project = Add(root, "project", "Program");
        Assert.Equal(0, root.Run("add", "task", "Chore", "--parent", project).ExitCode);
        Assert.Equal(0, root.Run("set", project, "health=off-track").ExitCode);

        var r = root.Run("tree", "--health", "off-track", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Program", r.Stdout);
        // --flat drops the non-matching context rows; here the child is the
        // non-match and the project is the one that matched.
        var flat = root.Run("tree", "--health", "off-track", "--flat", "--no-color");
        Assert.Contains("Program", flat.Stdout);
        Assert.DoesNotContain("Chore", flat.Stdout);
    }

    [Fact]
    public void Health_filter_rejects_an_unknown_signal()
    {
        using var root = Init();
        var r = root.Run("list", "--health", "amber", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("on-track|at-risk|off-track", r.Stdout + r.Stderr);
    }

    [Fact]
    public void Project_list_carries_the_linked_items_health()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "botco", "--title", "BotCo").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "quiet").ExitCode);

        var itemId = JsonDocument.Parse(root.Run("list", "--type", "project", "--json").Stdout)
            .RootElement.GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("title").GetString() == "BotCo")
            .GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("set", itemId, "health=at-risk").ExitCode);

        var rows = JsonDocument.Parse(root.Run("project", "list", "--json").Stdout).RootElement
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("slug").GetString()!, e => e);
        Assert.Equal("at-risk", rows["botco"].GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Null, rows["quiet"].GetProperty("health").ValueKind);

        var human = root.Run("project", "list", "--no-color");
        Assert.Contains("health", human.Stdout);
        Assert.Contains("at-risk", human.Stdout);
    }

    private static List<string> Ids(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args.Append("--json").ToArray());
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
    }
}
