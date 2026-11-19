using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;

namespace Knit.Core.Tests;

/// <summary>
/// Project doc folders + milestone sub-docs: layout,
/// milestone frontmatter parse (lenient + problem reporting), the
/// <c>Milestone</c> link rel, milestone target-path parsing, and the
/// item <c>project:</c> pointer round-trip.
/// </summary>
public class ProjectDocsTests : IDisposable
{
    private static readonly SimpleFrontmatterSerializer Yaml = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-projdocs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteMilestone(string project, string slug, string content)
    {
        var path = ProjectsLayout.MilestonePath(_root, project, slug);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void MilestoneFile_reads_full_frontmatter()
    {
        var path = WriteMilestone("relaunch", "beta", """
            ---
            title: Beta cut
            target: 2026-09-01
            state: reached
            created: 2026-07-01T00:00:00Z
            ---

            # Beta cut
            """);
        var (doc, problems) = MilestoneFile.Read(path, "relaunch", Yaml);
        Assert.Empty(problems);
        Assert.Equal("Beta cut", doc.Title);
        Assert.Equal(new DateOnly(2026, 9, 1), doc.Target);
        Assert.Equal(MilestoneState.Reached, doc.State);
        Assert.Equal("relaunch/beta", doc.QualifiedSlug);
    }

    [Fact]
    public void MilestoneFile_degrades_leniently_but_reports_problems()
    {
        var path = WriteMilestone("relaunch", "bad", """
            ---
            target: soon
            state: shipped
            ---
            body
            """);
        var (doc, problems) = MilestoneFile.Read(path, "relaunch", Yaml);
        // Display stays usable: slug fallback title, defaults for the rest.
        Assert.Equal("bad", doc.Title);
        Assert.Null(doc.Target);
        Assert.Equal(MilestoneState.Planned, doc.State);
        // But every degradation is a reported problem for verify.
        Assert.Contains(problems, p => p.Contains("title"));
        Assert.Contains(problems, p => p.Contains("target"));
        Assert.Contains(problems, p => p.Contains("state"));
    }

    [Fact]
    public void MilestoneFile_flags_missing_frontmatter()
    {
        var path = WriteMilestone("relaunch", "plain", "# just a heading\n");
        var (_, problems) = MilestoneFile.Read(path, "relaunch", Yaml);
        Assert.Contains(problems, p => p.Contains("frontmatter"));
    }

    [Theory]
    [InlineData("../../projects/relaunch/milestones/beta.md", "relaunch", "beta")]
    [InlineData("projects/relaunch/milestones/go-live.md", "relaunch", "go-live")]
    public void MilestoneFromTarget_parses_milestone_paths(string path, string project, string slug)
    {
        var id = LinkPath.MilestoneFromTarget(path);
        Assert.NotNull(id);
        Assert.Equal(project, id!.Value.Project);
        Assert.Equal(slug, id.Value.Slug);
    }

    [Theory]
    [InlineData("../OTHERID111111111A-task/index.md")]
    [InlineData("../../kb/docs/architecture.md")]
    [InlineData("../../projects/relaunch/readme.md")]
    [InlineData("../../projects/relaunch/milestones/sub/beta.md")]
    public void MilestoneFromTarget_rejects_non_milestone_paths(string path)
        => Assert.Null(LinkPath.MilestoneFromTarget(path));

    [Fact]
    public void Milestone_rel_round_trips_through_links_table()
    {
        var rows = new[] { new LinkRow(LinkRel.Milestone, "Beta cut", "../../projects/relaunch/milestones/beta.md", null) };
        var rendered = LinksTable.Render(rows);
        Assert.Contains("| Milestone", rendered);
        var parsed = LinksTable.Parse(rendered);
        var row = Assert.Single(parsed);
        Assert.Equal(LinkRel.Milestone, row.Rel);
        Assert.Equal("../../projects/relaunch/milestones/beta.md", row.TargetPath);
    }

    [Fact]
    public void Milestone_target_is_not_mistaken_for_item_link()
        => Assert.False(LinkPath.LooksLikeItemTarget("../../projects/relaunch/milestones/beta.md"));

    [Fact]
    public void ItemFile_round_trips_project_pointer()
    {
        var file = new ItemFile(Yaml);
        var written = file.Write(new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "p",
            Type = ItemType.Project,
            State = ItemState.Todo,
            Project = "relaunch",
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        });
        Assert.Contains("project: relaunch", written);
        Assert.Equal("relaunch", file.Read(written).Project);

        var noPointer = file.Write(new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "p",
            Type = ItemType.Project,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        });
        Assert.DoesNotContain("project:", noPointer);
    }

    [Fact]
    public void ProjectsLayout_enumerates_slugs_and_milestones()
    {
        WriteMilestone("alpha-proj", "m1", "---\ntitle: M1\n---\n");
        WriteMilestone("beta-proj", "m1", "---\ntitle: M1\n---\n");
        Assert.Equal(["alpha-proj", "beta-proj"], ProjectsLayout.ProjectSlugs(_root));
        Assert.Single(ProjectsLayout.MilestoneFiles(_root, "alpha-proj"));
        Assert.Equal("m1", ProjectsLayout.SlugFromMilestonePath(ProjectsLayout.MilestoneFiles(_root, "alpha-proj")[0]));
    }
}
