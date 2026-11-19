using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>knit project milestone move &lt;slug&gt; --to &lt;project&gt;</c>: the
/// the sub-doc moves, item back-links are re-pointed (never dropped), a slug
/// collision in the destination demands <c>--rename</c>, and items left
/// pointing across a project boundary are reported by the command and listed
/// by <c>verify</c> as informational.
/// </summary>
[Collection(nameof(CliCollection))]
public class MilestoneMoveTests
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

    private static string MilestonePath(TempKnitRoot root, string project, string slug) =>
        Path.Combine(root.Path, ".knit", "projects", project, "milestones", $"{slug}.md");

    [Fact]
    public void Move_relocates_the_subdoc_and_repoints_backlinks()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "phase-two", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut").ExitCode);

        var project = Add(root, "project", "Relaunch");
        Assert.Equal(0, root.Run("set", project, "project=relaunch").ExitCode);
        var epic = Add(root, "epic", "Content", project);
        Assert.Equal(0, root.Run("link", epic, "--milestone", "relaunch/beta", "--json").ExitCode);

        var move = root.Run("project", "milestone", "move", "beta", "--to", "phase-two", "--json");
        Assert.Equal(0, move.ExitCode);
        var r = JsonDocument.Parse(move.Stdout).RootElement;
        Assert.Equal("relaunch", r.GetProperty("from_project").GetString());
        Assert.Equal("phase-two", r.GetProperty("to_project").GetString());
        Assert.Single(r.GetProperty("repointed_item_ids").EnumerateArray());

        Assert.False(File.Exists(MilestonePath(root, "relaunch", "beta")));
        Assert.True(File.Exists(MilestonePath(root, "phase-two", "beta")));

        var show = root.Run("show", epic, "--json");
        var row = Assert.Single(
            JsonDocument.Parse(show.Stdout).RootElement.GetProperty("links").EnumerateArray(),
            l => l.GetProperty("rel").GetString() == "Milestone");
        Assert.Contains("projects/phase-two/milestones/beta.md", row.GetProperty("target_path").GetString());
    }

    [Fact]
    public void Item_left_pointing_across_a_project_boundary_is_reported_as_informational()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "phase-two", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);

        var project = Add(root, "project", "Relaunch");
        Assert.Equal(0, root.Run("set", project, "project=relaunch").ExitCode);
        var epic = Add(root, "epic", "Content", project);
        Assert.Equal(0, root.Run("link", epic, "--milestone", "relaunch/beta", "--json").ExitCode);

        var move = root.Run("project", "milestone", "move", "beta", "--to", "phase-two", "--json");
        Assert.Equal(0, move.ExitCode);
        var cross = JsonDocument.Parse(move.Stdout).RootElement
            .GetProperty("cross_project_item_ids").EnumerateArray().ToList();
        Assert.Single(cross);
        Assert.Equal(epic, cross[0].GetString());

        // Informational: verify lists it and still exits 0, even under --strict.
        var verify = root.Run("verify", "--json");
        Assert.Equal(0, verify.ExitCode);
        var row = Assert.Single(
            JsonDocument.Parse(verify.Stdout).RootElement.GetProperty("cross_project_milestones").EnumerateArray());
        Assert.Equal("relaunch", row.GetProperty("item_project").GetString());
        Assert.Equal("phase-two", row.GetProperty("milestone_project").GetString());
        Assert.Equal(0, root.Run("verify", "--strict", "--json").ExitCode);
    }

    [Fact]
    public void Slug_collision_in_the_destination_errors_and_names_rename()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "phase-two", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "phase-two", "beta").ExitCode);

        var clash = root.Run("project", "milestone", "move", "relaunch/beta", "--to", "phase-two");
        Assert.NotEqual(0, clash.ExitCode);
        Assert.Contains("--rename", clash.Stderr);
        Assert.True(File.Exists(MilestonePath(root, "relaunch", "beta")));

        var renamed = root.Run("project", "milestone", "move", "relaunch/beta", "--to", "phase-two", "--rename", "beta-2", "--json");
        Assert.Equal(0, renamed.ExitCode);
        Assert.Equal("beta-2", JsonDocument.Parse(renamed.Stdout).RootElement.GetProperty("new_slug").GetString());
        Assert.True(File.Exists(MilestonePath(root, "phase-two", "beta-2")));
    }

    [Fact]
    public void Move_to_an_unknown_project_or_the_same_project_is_refused()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);

        Assert.NotEqual(0, root.Run("project", "milestone", "move", "beta", "--to", "ghost").ExitCode);
        Assert.NotEqual(0, root.Run("project", "milestone", "move", "beta", "--to", "relaunch").ExitCode);
        Assert.True(File.Exists(MilestonePath(root, "relaunch", "beta")));
    }
}
