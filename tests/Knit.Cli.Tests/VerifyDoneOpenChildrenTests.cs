using System.Text.Json;
using Knit.Core.Discovery;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit verify` flags a `done` item whose direct child is still open
/// (todo / in-progress): the subtree claims complete while live work hangs
/// under it. Warning-severity (reachable through normal use — closing does not
/// cascade), promoted to a hard failure by `--strict`. A `dismissed` parent is
/// exempt, and a fully-closed subtree stays clean.
/// </summary>
[Collection(nameof(CliCollection))]
public class VerifyDoneOpenChildrenTests
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

    private static (string Epic, string Feature) EpicWithChild(TempKnitRoot root)
    {
        var epic = Add(root, "epic", "parent epic");
        var feature = Add(root, "feature", "child feature");
        Assert.Equal(0, root.Run("link", feature, "--parent", epic).ExitCode);
        return (epic, feature);
    }

    private static List<JsonElement> DoneWithOpenChildren(TempKnitRoot root)
    {
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode); // warning-severity — no hard failure on its own
        return JsonDocument.Parse(r.Stdout).RootElement
            .GetProperty("done_with_open_children").EnumerateArray().ToList();
    }

    [Fact]
    public void Done_parent_with_open_child_is_flagged_as_warning()
    {
        using var root = Init();
        var (epic, feature) = EpicWithChild(root);
        Assert.Equal(0, root.Run("done", epic).ExitCode);

        var findings = DoneWithOpenChildren(root);
        var finding = Assert.Single(findings);
        Assert.Equal(epic, finding.GetProperty("parent_id").GetString());
        Assert.Equal(feature, finding.GetProperty("child_id").GetString());
        Assert.Equal("feature", finding.GetProperty("child_type").GetString());
        Assert.Equal("todo", finding.GetProperty("child_state").GetString());

        var human = root.Run("verify");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("done items with open children", human.Stdout);

        // --strict promotes the warning to a hard failure.
        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);
        Assert.Equal(4, root.Run("verify", "--strict", "--json").ExitCode);
    }

    [Fact]
    public void In_progress_child_counts_as_open()
    {
        using var root = Init();
        var (epic, feature) = EpicWithChild(root);
        Assert.Equal(0, root.Run("start", feature).ExitCode);
        Assert.Equal(0, root.Run("done", epic).ExitCode);

        var finding = Assert.Single(DoneWithOpenChildren(root));
        Assert.Equal("in-progress", finding.GetProperty("child_state").GetString());
    }

    [Fact]
    public void Dismissed_parent_is_exempt()
    {
        using var root = Init();
        var (epic, _) = EpicWithChild(root);
        Assert.Equal(0, root.Run("dismiss", epic).ExitCode);

        Assert.Empty(DoneWithOpenChildren(root));
    }

    [Fact]
    public void Fully_closed_subtree_stays_clean()
    {
        using var root = Init();
        var (epic, feature) = EpicWithChild(root);
        Assert.Equal(0, root.Run("done", feature).ExitCode);
        Assert.Equal(0, root.Run("done", epic).ExitCode);

        Assert.Empty(DoneWithOpenChildren(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_does_not_list_the_items_folder_per_finding(bool json)
    {
        const int findings = 20;
        using var root = Init();
        for (var i = 0; i < findings; i++)
        {
            var epic = Add(root, "epic", $"e{i}");
            Assert.Equal(0, root.Run("add", "task", $"t{i}", "--parent", epic).ExitCode);
            Assert.Equal(0, root.Run("done", epic).ExitCode);
        }

        var listings = ItemLayout.CountListings();
        var r = json ? root.Run("verify", "--json") : root.Run("verify");

        Assert.Equal(0, r.ExitCode);
        Assert.True(listings.Value < findings, $"verify listed the items folder {listings.Value} times");
    }

    [Fact]
    public void Dismissed_child_under_done_parent_is_closed()
    {
        using var root = Init();
        var (epic, feature) = EpicWithChild(root);
        Assert.Equal(0, root.Run("dismiss", feature).ExitCode);
        Assert.Equal(0, root.Run("done", epic).ExitCode);

        Assert.Empty(DoneWithOpenChildren(root));
    }
}
