using Knit.Core.DocFolders;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;

namespace Knit.Core.Tests;

/// <summary>
/// The free-form project-doc core (<see cref="ProjectDoc"/>)
/// and the shared frontmatter-preserving body helper
/// (<see cref="DocBody"/>). Create / delete / rename compose the shared
/// doc-folder core; every resolved path is containment-guarded to the project
/// folder. Endpoint/HTTP behavior is covered through the WebApi tests.
/// </summary>
public class ProjectDocCrudTests : IDisposable
{
    private static readonly SimpleFrontmatterSerializer Yaml = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-pdoc-{Guid.NewGuid():N}");
    private Knit.Data.ProseRefRewriter Prose => new(new Knit.Data.ItemStore(_root), _root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void ScaffoldProject(string slug = "relaunch")
    {
        Directory.CreateDirectory(ProjectsLayout.ProjectDir(_root, slug));
        File.WriteAllText(ProjectsLayout.ReadmePath(_root, slug), $"# {slug}\n\n## Goal\n");
    }

    // ---- DocBody ----

    [Fact]
    public void DocBody_round_trips_plain_markdown_without_adding_frontmatter()
    {
        var path = Path.Combine(_root, "plain.md");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "# Title\n\nbody text\n");

        var read = DocBody.ReadRaw(path, Yaml)!.Value;
        Assert.Empty(read.Frontmatter);
        Assert.Contains("body text", read.Body);

        DocBody.WriteRaw(path, read.Frontmatter, read.Body + "\n\nmore", Yaml);
        var content = File.ReadAllText(path);
        Assert.DoesNotContain("---", content); // no frontmatter block invented
        Assert.Contains("more", content);
    }

    [Fact]
    public void DocBody_preserves_frontmatter_on_a_body_only_edit()
    {
        var path = Path.Combine(_root, "fm.md");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "---\ntags: [a, b]\n---\n\noriginal\n");

        var read = DocBody.ReadRaw(path, Yaml)!.Value;
        DocBody.WriteRaw(path, read.Frontmatter, "revised", Yaml);

        var reread = DocBody.ReadRaw(path, Yaml)!.Value;
        Assert.True(reread.Frontmatter.ContainsKey("tags"));
        Assert.Equal("revised", reread.Body.Trim());
    }

    // ---- create ----

    [Fact]
    public void CreateFreeForm_writes_flat_doc()
    {
        ScaffoldProject();
        var r = ProjectDoc.CreateFreeForm(_root, "relaunch", "notes", "Notes", asFolder: false);
        Assert.False(r.IsError);
        var path = Path.Combine(ProjectsLayout.ProjectDir(_root, "relaunch"), "notes.md");
        Assert.True(File.Exists(path));
        Assert.Contains("# Notes", File.ReadAllText(path));
    }

    [Fact]
    public void CreateFreeForm_writes_folder_form_readme()
    {
        ScaffoldProject();
        var r = ProjectDoc.CreateFreeForm(_root, "relaunch", "guide", "Guide", asFolder: true);
        Assert.False(r.IsError);
        Assert.True(r.GetValueOrThrow().IsFolderForm);
        Assert.True(File.Exists(Path.Combine(ProjectsLayout.ProjectDir(_root, "relaunch"), "guide", "readme.md")));
    }

    [Theory]
    [InlineData("readme")]
    [InlineData("roadmap.md")]
    [InlineData("milestones/beta")]
    public void CreateFreeForm_rejects_reserved_names(string slug)
    {
        ScaffoldProject();
        var r = ProjectDoc.CreateFreeForm(_root, "relaunch", slug, null, asFolder: false);
        Assert.True(r.IsError);
    }

    [Fact]
    public void CreateFreeForm_rejects_duplicate()
    {
        ScaffoldProject();
        Assert.False(ProjectDoc.CreateFreeForm(_root, "relaunch", "notes", null, false).IsError);
        Assert.True(ProjectDoc.CreateFreeForm(_root, "relaunch", "notes", null, false).IsError);
    }

    [Fact]
    public void CreateFreeForm_errors_when_project_folder_missing()
    {
        var r = ProjectDoc.CreateFreeForm(_root, "ghost", "notes", null, false);
        Assert.True(r.IsError);
    }

    // ---- resolve / containment ----

    [Theory]
    [InlineData("../../escape.md")]
    [InlineData("sub/../../escape.md")]
    public void Resolve_rejects_escapes(string rel)
    {
        ScaffoldProject();
        Assert.True(ProjectDoc.Resolve(_root, "relaunch", rel).IsError);
    }

    // ---- delete ----

    [Fact]
    public void Delete_removes_flat_doc()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "notes", null, false);
        var r = ProjectDoc.Delete(_root, "relaunch", "notes.md", deleteFolder: false);
        Assert.False(r.IsError);
        Assert.True(r.GetValueOrThrow().Deleted);
        Assert.False(File.Exists(Path.Combine(ProjectsLayout.ProjectDir(_root, "relaunch"), "notes.md")));
    }

    [Fact]
    public void Delete_folder_form_needs_deleteFolder_when_it_has_siblings()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "guide", null, asFolder: true);
        var guideDir = Path.Combine(ProjectsLayout.ProjectDir(_root, "relaunch"), "guide");
        File.WriteAllText(Path.Combine(guideDir, "extra.md"), "# extra\n");

        // Without deleteFolder the sibling blocks and nothing is removed.
        var blocked = ProjectDoc.Delete(_root, "relaunch", "guide/readme.md", deleteFolder: false);
        Assert.False(blocked.IsError);
        Assert.False(blocked.GetValueOrThrow().Deleted);
        Assert.NotEmpty(blocked.GetValueOrThrow().BlockingFiles);
        Assert.True(Directory.Exists(guideDir));

        // With deleteFolder the whole folder goes.
        var forced = ProjectDoc.Delete(_root, "relaunch", "guide/readme.md", deleteFolder: true);
        Assert.True(forced.GetValueOrThrow().Deleted);
        Assert.False(Directory.Exists(guideDir));
    }

    // ---- rename / move ----

    [Fact]
    public void Move_renames_flat_doc()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "old", "Old", false);
        var r = ProjectDoc.Move(_root, "relaunch", "old.md", "new.md", Prose);
        Assert.False(r.IsError);
        var dir = ProjectsLayout.ProjectDir(_root, "relaunch");
        Assert.False(File.Exists(Path.Combine(dir, "old.md")));
        Assert.True(File.Exists(Path.Combine(dir, "new.md")));
    }

    [Fact]
    public void Move_renames_folder_form_and_moves_the_whole_folder()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "guide", null, asFolder: true);
        var dir = ProjectsLayout.ProjectDir(_root, "relaunch");
        File.WriteAllText(Path.Combine(dir, "guide", "extra.md"), "# extra\n");

        var r = ProjectDoc.Move(_root, "relaunch", "guide/readme.md", "manual", Prose);
        Assert.False(r.IsError);
        Assert.True(r.GetValueOrThrow().WasFolderForm);
        Assert.False(Directory.Exists(Path.Combine(dir, "guide")));
        Assert.True(File.Exists(Path.Combine(dir, "manual", "readme.md")));
        Assert.True(File.Exists(Path.Combine(dir, "manual", "extra.md")));
    }

    [Fact]
    public void Move_rejects_reserved_destination()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "notes", null, false);
        Assert.True(ProjectDoc.Move(_root, "relaunch", "notes.md", "readme.md", Prose).IsError);
    }

    [Fact]
    public void Move_conflicts_when_destination_exists()
    {
        ScaffoldProject();
        ProjectDoc.CreateFreeForm(_root, "relaunch", "a", null, false);
        ProjectDoc.CreateFreeForm(_root, "relaunch", "b", null, false);
        Assert.True(ProjectDoc.Move(_root, "relaunch", "a.md", "b.md", Prose).IsError);
    }
}
