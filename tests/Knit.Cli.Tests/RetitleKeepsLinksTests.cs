using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// An item's file name is fixed at creation, so a retitle leaves
/// every incoming relative link working; only <c>knit move</c> renames.
/// </summary>
[Collection(nameof(CliCollection))]
public class RetitleKeepsLinksTests
{
    private static string Add(TempKnitRoot root, string type, string title) =>
        JsonDocument.Parse(root.Run("add", type, title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

    private static string[] ItemFiles(TempKnitRoot root) =>
        Directory.GetFiles(Path.Combine(root.Path, ".knit", "items"), "*.md").Select(Path.GetFileName).Order().ToArray()!;

    private static int LinkTargetFindings(TempKnitRoot root) =>
        JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement
            .GetProperty("link_targets").GetArrayLength();

    [Fact]
    public void Retitle_keeps_the_file_name_and_incoming_links()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var feature = Add(root, "feature", "old name");
        var child = Add(root, "task", "child");
        var dependent = Add(root, "task", "dependent");
        Assert.Equal(0, root.Run("link", child, "--parent", feature).ExitCode);
        Assert.Equal(0, root.Run("block", dependent, "--by", feature).ExitCode);
        var before = ItemFiles(root);

        Assert.Equal(0, root.Run("set", feature, "title=new name").ExitCode);

        Assert.Equal(before, ItemFiles(root));
        Assert.Equal(0, LinkTargetFindings(root));
        var shown = JsonDocument.Parse(root.Run("show", feature, "--json").Stdout).RootElement;
        Assert.Equal("new name", shown.GetProperty("title").GetString());
    }

    [Fact]
    public void A_later_write_keeps_a_moved_file_name()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var feature = Add(root, "feature", "feature");
        var child = Add(root, "task", "child");
        Assert.Equal(0, root.Run("link", child, "--parent", feature).ExitCode);

        Assert.Equal(0, root.Run("move", feature, "renamed").ExitCode);
        Assert.Equal(0, root.Run("set", feature, "priority=critical").ExitCode);

        Assert.Contains($"{feature}-renamed.md", ItemFiles(root));
        Assert.Equal(0, LinkTargetFindings(root));
    }

    [Fact]
    public void Move_rewrites_prose_links_in_items_and_designs()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var feature = Add(root, "feature", "feature");
        var other = Add(root, "task", "other");
        Assert.Equal(0, root.Run("design", "new", "plan", "--title", "Plan").ExitCode);
        var itemsDir = Path.Combine(root.Path, ".knit", "items");
        var otherFile = Directory.GetFiles(itemsDir, $"{other}-*.md").Single();
        var designFile = Path.Combine(root.Path, ".knit", "designs", "plan-design.md");
        File.AppendAllText(otherFile, $"\nSee [the feature]({feature}-feature.md#scope).\n");
        File.AppendAllText(designFile, $"\nSee [the feature](../items/{feature}-feature.md).\n");

        Assert.Equal(0, root.Run("move", feature, "renamed").ExitCode);

        Assert.Contains($"]({feature}-renamed.md#scope)", File.ReadAllText(otherFile));
        Assert.Contains($"](../items/{feature}-renamed.md)", File.ReadAllText(designFile));
        Assert.Equal(0, JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement
            .GetProperty("prose_links").GetArrayLength());
    }
}
