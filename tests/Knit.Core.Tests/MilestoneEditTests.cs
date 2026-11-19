using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Projects;

namespace Knit.Core.Tests;

/// <summary>
/// The pure-FS milestone core (<see cref="MilestoneEdit"/>):
/// frontmatter/body edits that preserve <c>created</c> + mirror the title↔H1,
/// target clearing, and the guarded flat file move / delete. Backlink
/// re-pointing lives in the Data layer (tested through the CLI/WebApi).
/// </summary>
public class MilestoneEditTests : IDisposable
{
    private static readonly SimpleFrontmatterSerializer Yaml = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-msedit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Scaffold(string project, string slug, string title, DateOnly? target = null)
    {
        Directory.CreateDirectory(ProjectsLayout.ProjectDir(_root, project));
        File.WriteAllText(ProjectsLayout.ReadmePath(_root, project), $"# {project}\n");
        File.WriteAllText(ProjectsLayout.RoadmapPath(_root, project), $"# {project} — roadmap\n"); // milestones are roadmap-gated
        Assert.False(ProjectScaffold.NewMilestone(_root, project, slug, title, target, force: false, Yaml).IsError);
    }

    [Fact]
    public void Apply_edits_fields_preserves_created_and_mirrors_title_into_h1()
    {
        Scaffold("relaunch", "beta", "Beta cut", new DateOnly(2026, 9, 1));

        var result = MilestoneEdit.Apply(_root, "relaunch", "beta",
            new MilestoneEdits(Title: "Beta shipped", State: MilestoneState.Reached), Yaml);
        Assert.False(result.IsError);
        var doc = result.GetValueOrThrow();
        Assert.Equal("Beta shipped", doc.Title);
        Assert.Equal(MilestoneState.Reached, doc.State);
        Assert.Equal(new DateOnly(2026, 9, 1), doc.Target);

        var text = File.ReadAllText(ProjectsLayout.MilestonePath(_root, "relaunch", "beta"));
        Assert.Contains("created:", text);        // untouched
        Assert.Contains("# Beta shipped", text);   // H1 mirrored
    }

    [Fact]
    public void Apply_clear_target_removes_the_field()
    {
        Scaffold("relaunch", "beta", "Beta cut", new DateOnly(2026, 9, 1));

        var result = MilestoneEdit.Apply(_root, "relaunch", "beta", new MilestoneEdits(ClearTarget: true), Yaml);
        Assert.Null(result.GetValueOrThrow().Target);
    }

    [Fact]
    public void Apply_body_edit_pulls_title_from_leading_h1()
    {
        Scaffold("relaunch", "beta", "Beta cut");

        var result = MilestoneEdit.Apply(_root, "relaunch", "beta",
            new MilestoneEdits(Body: "# Renamed via body\n\nnotes"), Yaml);
        Assert.Equal("Renamed via body", result.GetValueOrThrow().Title);
    }

    [Fact]
    public void Apply_on_missing_milestone_is_not_found()
    {
        var result = MilestoneEdit.Apply(_root, "relaunch", "ghost", new MilestoneEdits(State: MilestoneState.Dropped), Yaml);
        Assert.True(result.IsError);
    }

    [Fact]
    public void Rename_moves_the_file_and_refuses_an_existing_target()
    {
        Scaffold("relaunch", "beta", "Beta cut");
        Scaffold("relaunch", "go-live", "Go live");

        var clash = MilestoneEdit.Rename(_root, "relaunch", "beta", "go-live");
        Assert.True(clash.IsError);

        var ok = MilestoneEdit.Rename(_root, "relaunch", "beta", "Beta 2");
        Assert.False(ok.IsError);
        var (oldPath, newPath) = ok.GetValueOrThrow();
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
        Assert.Equal("beta-2", ProjectsLayout.SlugFromMilestonePath(newPath)); // slugified
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        Scaffold("relaunch", "beta", "Beta cut");
        var result = MilestoneEdit.Delete(_root, "relaunch", "beta");
        Assert.False(result.IsError);
        Assert.False(File.Exists(result.GetValueOrThrow()));

        Assert.True(MilestoneEdit.Delete(_root, "relaunch", "beta").IsError); // gone now
    }

    [Fact]
    public void NewMilestone_assigns_an_append_rank_preserving_creation_order()
    {
        Scaffold("relaunch", "alpha", "Alpha");
        Scaffold("relaunch", "beta", "Beta");

        var docs = ProjectsLayout.MilestoneFiles(_root, "relaunch")
            .Select(f => MilestoneFile.Read(f, "relaunch", Yaml).Doc)
            .ToList();
        Assert.All(docs, d => Assert.True(Rank.IsValid(d.Rank)));
        var alpha = docs.Single(d => d.Slug == "alpha");
        var beta = docs.Single(d => d.Slug == "beta");
        Assert.True(string.CompareOrdinal(alpha.Rank, beta.Rank) < 0); // created first → sorts first
    }

    [Fact]
    public void Reorder_rewrites_ranks_to_match_the_requested_order()
    {
        Scaffold("relaunch", "alpha", "Alpha");
        Scaffold("relaunch", "beta", "Beta");
        Scaffold("relaunch", "gamma", "Gamma");
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, OrderByRank("relaunch"));

        var res = MilestoneEdit.Reorder(_root, "relaunch", new[] { "gamma", "alpha", "beta" }, Yaml);
        Assert.False(res.IsError);
        Assert.Equal(new[] { "gamma", "alpha", "beta" }, OrderByRank("relaunch"));

        // created: (unknown key) survives the rank rewrite.
        Assert.Contains("created:", File.ReadAllText(ProjectsLayout.MilestonePath(_root, "relaunch", "gamma")));
    }

    [Fact]
    public void Reorder_rejects_a_non_permutation()
    {
        Scaffold("relaunch", "alpha", "Alpha");
        Scaffold("relaunch", "beta", "Beta");

        Assert.True(MilestoneEdit.Reorder(_root, "relaunch", new[] { "alpha" }, Yaml).IsError);              // missing one
        Assert.True(MilestoneEdit.Reorder(_root, "relaunch", new[] { "alpha", "beta", "ghost" }, Yaml).IsError); // extra
        Assert.True(MilestoneEdit.Reorder(_root, "relaunch", new[] { "alpha", "alpha" }, Yaml).IsError);     // duplicate
    }

    private string[] OrderByRank(string project) =>
        ProjectsLayout.MilestoneFiles(_root, project)
            .Select(f => MilestoneFile.Read(f, project, Yaml).Doc)
            .OrderBy(d => Rank.IsValid(d.Rank) ? d.Rank! : "￿", StringComparer.Ordinal)
            .ThenBy(d => d.Slug, StringComparer.Ordinal)
            .Select(d => d.Slug)
            .ToArray();
}
