using System.Text.Json;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class ShowJsonTests
{
    [Fact]
    public void Show_json_includes_frontmatter_sections_and_comments()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);

        var added = root.Run("add", "feature", "the title", "--json");
        var id = JsonDocument.Parse(added.Stdout).RootElement.GetProperty("id").GetString()!;

        Assert.Equal(0, root.Run("comment", id, "first comment").ExitCode);

        var shown = root.Run("show", id, "--json");
        Assert.Equal(0, shown.ExitCode);
        Assert.DoesNotContain("\x1b", shown.Stdout);

        using var doc = JsonDocument.Parse(shown.Stdout);
        var r = doc.RootElement;

        Assert.Equal(id, r.GetProperty("id").GetString());
        Assert.Equal("feature", r.GetProperty("type").GetString());
        Assert.Equal("the title", r.GetProperty("title").GetString());

        // Default feature template has Description, Acceptance Criteria, Notes h2s
        Assert.True(r.TryGetProperty("description", out _));
        Assert.True(r.TryGetProperty("acceptance_criteria", out _));
        Assert.True(r.TryGetProperty("notes", out _));

        var comments = r.GetProperty("comments");
        Assert.Equal(1, comments.GetArrayLength());
        Assert.Equal("first comment", comments[0].GetProperty("body").GetString());
        Assert.Equal("alice", comments[0].GetProperty("author").GetString());
    }

    [Fact]
    public void Show_human_output_is_pipe_clean_and_renders_sections()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var added = root.Run("add", "feature", "rendered nicely", "--json");
        var id = JsonDocument.Parse(added.Stdout).RootElement.GetProperty("id").GetString()!;

        var shown = root.Run("show", id);
        Assert.True(shown.ExitCode == 0,
            $"exit={shown.ExitCode}\nSTDOUT:\n{shown.Stdout}\nSTDERR:\n{shown.Stderr}");
        Assert.DoesNotContain("\x1b", shown.Stdout);
        Assert.Contains("rendered nicely", shown.Stdout);
    }

    /// <summary>
    /// Every scalar the item stores has to be reachable from the detail view —
    /// `show` used to render `effort` and nothing else from the M1-polish wave,
    /// so due / priority / tags / slate / design were write-only in the CLI.
    /// </summary>
    [Fact]
    public void Show_renders_every_stored_scalar()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var id = JsonDocument.Parse(root.Run("add", "task", "fully populated", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("set", id, "due=2030-01-15").ExitCode);
        Assert.Equal(0, root.Run("set", id, "priority=critical").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "some-design", "--title", "Some Design").ExitCode);
        Assert.Equal(0, root.Run("set", id, "design=some-design").ExitCode);
        Assert.Equal(0, root.Run("set", id, "tags=alpha").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "sprint-1").ExitCode);
        Assert.Equal(0, root.Run("set", id, "slate=sprint-1").ExitCode);

        var shown = root.Run("show", id);
        Assert.True(shown.ExitCode == 0, shown.Stdout + shown.Stderr);
        foreach (var expected in new[] { "2030-01-15", "critical", "some-design", "alpha", "sprint-1" })
            Assert.Contains(expected, shown.Stdout);
    }

    /// <summary>
    /// Intrinsically-coloured emoji survive ANSI stripping, so a single blocked
    /// row rendered as if highlighted in a monochrome terminal. `show`'s 🚫 was
    /// the only emoji in src/.
    /// </summary>
    [Fact]
    public void Show_marks_blocked_with_a_monochrome_safe_glyph()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var id = JsonDocument.Parse(root.Run("add", "task", "stuck", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("block", id, "--reason", "waiting for vendor").ExitCode);

        var shown = root.Run("show", id);
        Assert.True(shown.ExitCode == 0, shown.Stdout + shown.Stderr);
        Assert.Contains("waiting for vendor", shown.Stdout);
        Assert.DoesNotContain("🚫", shown.Stdout);
        Assert.Contains("⊘", shown.Stdout);
    }

    /// <summary>
    /// `show --json` and `list --json` describe the same item; a key present in
    /// one and missing in the other sends agents to the wrong verb.
    /// </summary>
    [Fact]
    public void Show_json_carries_the_same_keys_as_list_json()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var id = JsonDocument.Parse(root.Run("add", "task", "fully populated", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("design", "new", "some-design", "--title", "Some Design").ExitCode);
        Assert.Equal(0, root.Run("set", id, "design=some-design").ExitCode);
        Assert.Equal(0, root.Run("set", id, "tags=alpha").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "sprint-1").ExitCode);
        Assert.Equal(0, root.Run("set", id, "slate=sprint-1").ExitCode);

        using var doc = JsonDocument.Parse(root.Run("show", id, "--json").Stdout);
        var r = doc.RootElement;

        Assert.Equal("some-design", r.GetProperty("design").GetString());
        Assert.Equal("sprint-1", r.GetProperty("slate").GetString());
        Assert.Equal("alpha", r.GetProperty("tags")[0].GetString());
        Assert.False(string.IsNullOrEmpty(r.GetProperty("modified").GetString()));

        using var listed = JsonDocument.Parse(root.Run("list", "--json").Stdout);
        var fromList = listed.RootElement.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == id);
        Assert.Equal("some-design", fromList.GetProperty("design").GetString());
    }
}
