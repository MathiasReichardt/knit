using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// CLI parity for the project-doc surface: `knit project
/// roadmap` (standalone roadmap scaffold that never touches the readme) and
/// `knit project doc rename` (free-form doc/folder move, no prose-link repair).
/// </summary>
[Collection(nameof(CliCollection))]
public class ProjectDocCliTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string ProjectFile(TempKnitRoot root, params string[] parts) =>
        Path.Combine(new[] { root.Path, ".knit", "projects" }.Concat(parts).ToArray());

    /// <summary>
    /// The scaffolds shipped as bare headings, so a fresh
    /// project folder gave an author nothing to write against.
    /// </summary>
    [Fact]
    public void Scaffolds_ship_with_prompt_prose_not_empty_sections()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--title", "Website relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);

        var readme = File.ReadAllText(ProjectFile(root, "relaunch", "readme.md"));
        Assert.Contains("## Goal\nWhat this project is for", readme.ReplaceLineEndings("\n"));
        Assert.Contains("## Notes\nContext that belongs", readme.ReplaceLineEndings("\n"));

        Assert.Contains("sequencing rationale", File.ReadAllText(ProjectFile(root, "relaunch", "roadmap.md")));
        Assert.Contains("deliberately leaves out",
            File.ReadAllText(ProjectFile(root, "relaunch", "milestones", "beta.md")));
    }

    [Fact]
    public void Project_new_goal_fills_the_readme_section()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--goal", "Ship the new site by Q3.").ExitCode);
        var readme = File.ReadAllText(ProjectFile(root, "relaunch", "readme.md")).ReplaceLineEndings("\n");
        Assert.Contains("## Goal\nShip the new site by Q3.", readme);
        Assert.DoesNotContain("What this project is for", readme);

        // Same on the folder-only escape hatch.
        Assert.Equal(0, root.Run("project", "new", "solo", "--no-item", "--goal", "Just the folder.").ExitCode);
        Assert.Contains("Just the folder.", File.ReadAllText(ProjectFile(root, "solo", "readme.md")));
    }

    [Fact]
    public void Project_roadmap_scaffolds_standalone_without_touching_readme()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--title", "Website relaunch").ExitCode);
        var readme = ProjectFile(root, "relaunch", "readme.md");
        var readmeBefore = File.ReadAllText(readme);

        var r = root.Run("project", "roadmap", "relaunch", "--title", "Relaunch roadmap", "--json");
        Assert.Equal(0, r.ExitCode);
        var doc = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal("relaunch", doc.GetProperty("slug").GetString());

        Assert.True(File.Exists(ProjectFile(root, "relaunch", "roadmap.md")));
        Assert.Contains("Relaunch roadmap", File.ReadAllText(ProjectFile(root, "relaunch", "roadmap.md")));
        // Readme is untouched.
        Assert.Equal(readmeBefore, File.ReadAllText(readme));

        // Refuses to overwrite an existing roadmap without --force.
        Assert.NotEqual(0, root.Run("project", "roadmap", "relaunch", "--json").ExitCode);
        // Missing project folder → error.
        Assert.NotEqual(0, root.Run("project", "roadmap", "ghost", "--json").ExitCode);
    }

    [Fact]
    public void Project_doc_rename_moves_a_flat_doc()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch").ExitCode);
        // Free-form docs are "just drop a .md" — no create command.
        File.WriteAllText(ProjectFile(root, "relaunch", "notes.md"), "# Notes\n\nsome prose\n");

        var r = root.Run("project", "doc", "rename", "relaunch", "notes.md", "archive/notes.md", "--json");
        Assert.Equal(0, r.ExitCode);
        var doc = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.False(doc.GetProperty("folder_form").GetBoolean());

        Assert.False(File.Exists(ProjectFile(root, "relaunch", "notes.md")));
        Assert.True(File.Exists(ProjectFile(root, "relaunch", "archive", "notes.md")));
        Assert.Contains("some prose", File.ReadAllText(ProjectFile(root, "relaunch", "archive", "notes.md")));
    }

    [Fact]
    public void Project_doc_rename_repoints_links_to_and_from_the_doc()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch").ExitCode);
        File.WriteAllText(ProjectFile(root, "relaunch", "notes.md"), "# Notes\n\nBack to [readme](readme.md).\n");
        File.AppendAllText(ProjectFile(root, "relaunch", "readme.md"), "\nSee [notes](notes.md).\n");

        var r = root.Run("project", "doc", "rename", "relaunch", "notes.md", "archive/notes.md", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(2, JsonDocument.Parse(r.Stdout).RootElement.GetProperty("files_relinked").GetArrayLength());

        Assert.Contains("[notes](archive/notes.md)", File.ReadAllText(ProjectFile(root, "relaunch", "readme.md")));
        Assert.Contains("[readme](../readme.md)", File.ReadAllText(ProjectFile(root, "relaunch", "archive", "notes.md")));
    }

    [Fact]
    public void Project_doc_rename_moves_a_whole_folder_form_doc()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch").ExitCode);
        Directory.CreateDirectory(ProjectFile(root, "relaunch", "specs"));
        File.WriteAllText(ProjectFile(root, "relaunch", "specs", "readme.md"), "# Specs\n");
        File.WriteAllText(ProjectFile(root, "relaunch", "specs", "detail.md"), "# Detail\n");

        // A folder-form doc is addressed by its readme.md (as the SPA enumerates it).
        var r = root.Run("project", "doc", "rename", "relaunch", "specs/readme.md", "design-specs", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.True(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("folder_form").GetBoolean());

        Assert.False(Directory.Exists(ProjectFile(root, "relaunch", "specs")));
        Assert.True(File.Exists(ProjectFile(root, "relaunch", "design-specs", "readme.md")));
        Assert.True(File.Exists(ProjectFile(root, "relaunch", "design-specs", "detail.md")));
    }

    [Fact]
    public void Project_doc_rename_rejects_reserved_destination_and_missing_source()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch").ExitCode);
        File.WriteAllText(ProjectFile(root, "relaunch", "notes.md"), "# Notes\n");

        // Reserved names have their own lifecycle commands.
        var reserved = root.Run("project", "doc", "rename", "relaunch", "notes.md", "roadmap.md", "--json");
        Assert.NotEqual(0, reserved.ExitCode);
        Assert.Contains("reserved", reserved.Stdout + reserved.Stderr);

        // Missing source.
        Assert.NotEqual(0, root.Run("project", "doc", "rename", "relaunch", "ghost.md", "x.md", "--json").ExitCode);

        // Path escape is refused.
        Assert.NotEqual(0, root.Run("project", "doc", "rename", "relaunch", "notes.md", "../escape.md", "--json").ExitCode);
    }
}
