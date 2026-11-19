using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `set parent=` is rejected (parent is a `## Links` relation, not a
/// settable frontmatter scalar; a `set` would silently drop on Save). The
/// error points at the link/unlink verbs that own it, and `link --parent`
/// re-parents for real.
/// </summary>
[Collection(nameof(CliCollection))]
public class SetParentRejectTests
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

    private static string? ParentOf(TempKnitRoot root, string id)
    {
        var r = root.Run("show", id, "--json");
        Assert.Equal(0, r.ExitCode);
        var el = JsonDocument.Parse(r.Stdout).RootElement;
        return el.TryGetProperty("parent", out var p) ? p.GetString() : null;
    }

    [Fact]
    public void Set_parent_is_rejected_and_persists_nothing()
    {
        using var root = Init();
        var epic = Add(root, "epic", "parent epic");
        var feature = Add(root, "feature", "child feature");

        var r = root.Run("set", feature, $"parent={epic}", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("--parent", r.Stdout + r.Stderr);
        Assert.Null(ParentOf(root, feature));
    }

    [Fact]
    public void Set_parent_empty_points_at_unlink()
    {
        using var root = Init();
        var feature = Add(root, "feature", "orphan");

        var r = root.Run("set", feature, "parent=", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("unlink", r.Stdout + r.Stderr);
    }

    [Fact]
    public void Link_parent_reparents_for_real()
    {
        using var root = Init();
        var epic = Add(root, "epic", "parent epic");
        var feature = Add(root, "feature", "child feature");

        Assert.Equal(0, root.Run("link", feature, "--parent", epic).ExitCode);
        Assert.Equal(epic, ParentOf(root, feature));

        Assert.Equal(0, root.Run("unlink", feature, "parent").ExitCode);
        Assert.Null(ParentOf(root, feature));
    }
}
