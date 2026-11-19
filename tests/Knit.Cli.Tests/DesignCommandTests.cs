using System.Text.Json;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class DesignCommandTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        return root;
    }

    [Fact]
    public void List_returns_empty_when_no_designs()
    {
        using var root = NewRepo();
        var r = root.Run("design", "list", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("[]", r.Stdout.Trim());
    }

    [Fact]
    public void New_scaffolds_design_file()
    {
        using var root = NewRepo();
        var r = root.Run("design", "new", "kb", "--title", "Knowledge Base", "--json");
        Assert.Equal(0, r.ExitCode);

        var doc = JsonDocument.Parse(r.Stdout);
        Assert.Equal("kb", doc.RootElement.GetProperty("slug").GetString());
        var path = doc.RootElement.GetProperty("path").GetString();
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path!);
        Assert.Contains("title: Knowledge Base", content);
        Assert.Contains("slug: kb", content);
        Assert.Contains("# Knowledge Base", content);
    }

    [Fact]
    public void New_refuses_overwrite_without_force()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        var r = root.Run("design", "new", "kb");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("conflict", r.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void New_with_force_overwrites()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "kb", "--title", "Two", "--force").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "designs", "kb-design.md");
        Assert.Contains("title: Two", File.ReadAllText(path));
    }

    [Fact]
    public void List_returns_active_designs()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "auth").ExitCode);

        var r = root.Run("design", "list", "--json");
        Assert.Equal(0, r.ExitCode);
        var arr = JsonDocument.Parse(r.Stdout).RootElement;
        var slugs = arr.EnumerateArray().Select(e => e.GetProperty("slug").GetString()).ToList();
        Assert.Equal(2, slugs.Count);
        Assert.Contains("kb", slugs);
        Assert.Contains("auth", slugs);
    }

    // The listing used to be slug + path only.
    [Fact]
    public void List_carries_title_and_status()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb", "--title", "Knowledge Base").ExitCode);

        var r = root.Run("design", "list", "--json");
        var row = JsonDocument.Parse(r.Stdout).RootElement[0];
        Assert.Equal("Knowledge Base", row.GetProperty("title").GetString());
        Assert.Equal("Draft.", row.GetProperty("status").GetString()); // the template's `## Status`

        var path = Path.Combine(root.Path, ".knit", "designs", "kb-design.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Draft.", "agreed"));
        var human = root.Run("design", "list", "--no-color");
        Assert.Contains("Knowledge Base", human.Stdout);
        Assert.Contains("agreed", human.Stdout);
    }

    [Fact]
    public void List_status_is_null_when_the_section_is_missing_or_empty()
    {
        using var root = NewRepo();
        var dir = Path.Combine(root.Path, ".knit", "designs");
        Directory.CreateDirectory(dir);
        // No frontmatter title either — the h1 carries it.
        File.WriteAllText(Path.Combine(dir, "bare-design.md"), "# Bare\n\n## Overview\nsomething\n");
        File.WriteAllText(Path.Combine(dir, "empty-design.md"), "---\ntitle: Empty\n---\n\n## Status\n\n## Notes\nx\n");

        var rows = JsonDocument.Parse(root.Run("design", "list", "--json").Stdout).RootElement
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("slug").GetString()!, e => e);

        Assert.Equal("Bare", rows["bare"].GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, rows["bare"].GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows["empty"].GetProperty("status").ValueKind);
    }

    [Fact]
    public void Show_path_only_prints_resolved_path()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        var r = root.Run("design", "show", "kb", "--path-only");
        Assert.Equal(0, r.ExitCode);
        var line = r.Stdout.Trim();
        Assert.EndsWith("kb-design.md", line);
        Assert.True(File.Exists(line));
    }

    [Fact]
    public void Show_unknown_slug_errors_not_found()
    {
        using var root = NewRepo();
        var r = root.Run("design", "show", "missing");
        Assert.Equal(3, r.ExitCode); // ErrorCode.NotFound
        Assert.Contains("not_found", r.Stderr);
    }

    [Fact]
    public void Show_json_returns_slug_path_frontmatter_body()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb", "--title", "KB").ExitCode);
        var r = root.Run("design", "show", "kb", "--json");
        Assert.Equal(0, r.ExitCode);
        var doc = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal("kb", doc.GetProperty("slug").GetString());
        Assert.EndsWith("kb-design.md", doc.GetProperty("path").GetString());
        Assert.Equal("KB", doc.GetProperty("frontmatter").GetProperty("title").GetString());
        Assert.Contains("# KB", doc.GetProperty("body").GetString());
    }

    [Fact]
    public void Archive_moves_active_to_completed()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        var active = Path.Combine(root.Path, ".knit", "designs", "kb-design.md");
        Assert.True(File.Exists(active));

        var r = root.Run("design", "archive", "kb", "--json");
        Assert.Equal(0, r.ExitCode);
        var doc = JsonDocument.Parse(r.Stdout).RootElement;
        var dest = doc.GetProperty("path").GetString();

        Assert.False(File.Exists(active));
        Assert.True(File.Exists(dest));
        Assert.Contains(Path.Combine(".knit", "designs", "completed", "kb"), dest);
    }

    [Fact]
    public void Archive_repoints_links_to_and_from_the_archived_doc()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "other").ExitCode);
        var designs = Path.Combine(root.Path, ".knit", "designs");
        File.AppendAllText(Path.Combine(designs, "kb-design.md"), "\nSee [other](other-design.md).\n");
        File.AppendAllText(Path.Combine(designs, "other-design.md"), "\nSee [kb](kb-design.md#goals).\n");

        var r = root.Run("design", "archive", "kb", "--json");
        Assert.Equal(0, r.ExitCode);
        var relinked = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("files_relinked");
        Assert.Equal(2, relinked.GetArrayLength());

        Assert.Contains("[kb](completed/kb/kb-design.md#goals)", File.ReadAllText(Path.Combine(designs, "other-design.md")));
        Assert.Contains("[other](../../other-design.md)",
            File.ReadAllText(Path.Combine(designs, "completed", "kb", "kb-design.md")));
    }

    [Fact]
    public void Archive_rejects_already_archived()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "archive", "kb").ExitCode);
        var r = root.Run("design", "archive", "kb");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("not_found", r.Stderr);
    }

    [Fact]
    public void Archive_with_custom_cycle_renames()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "archive", "kb", "--cycle", "v1").ExitCode);
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "designs", "completed", "kb", "v1-design.md")));
    }

    [Fact]
    public void List_completed_returns_archived_designs()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "kb").ExitCode);
        Assert.Equal(0, root.Run("design", "archive", "kb").ExitCode);
        var r = root.Run("design", "list", "--completed", "--json");
        Assert.Equal(0, r.ExitCode);
        var arr = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal(1, arr.GetArrayLength());
        Assert.True(arr[0].GetProperty("completed").GetBoolean());
    }
}
