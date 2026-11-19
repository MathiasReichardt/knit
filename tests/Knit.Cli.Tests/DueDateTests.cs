using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Due-date set/clear, --sort due (nulls last), the
/// --overdue/--due-before/--due-after filters, and color auto-strip.
/// </summary>
[Collection(nameof(CliCollection))]
public class DueDateTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string title) =>
        JsonDocument.Parse(root.Run("add", "task", title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

    private static List<(string id, string? due)> ListWithDue(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args.Prepend("list").Append("--json").ToArray());
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => (
                e.GetProperty("id").GetString()!,
                e.TryGetProperty("due", out var d) ? d.GetString() : null))
            .ToList();
    }

    [Fact]
    public void Set_due_sets_and_clears()
    {
        using var root = Init();
        var id = Add(root, "dated");

        var set = root.Run("set", id, "due=2030-01-15", "--json");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal("2030-01-15", JsonDocument.Parse(set.Stdout).RootElement.GetProperty("due").GetString());

        var clear = root.Run("set", id, "due=", "--json");
        Assert.Equal(0, clear.ExitCode);
        Assert.False(JsonDocument.Parse(clear.Stdout).RootElement.TryGetProperty("due", out _));
    }

    [Fact]
    public void Set_due_rejects_malformed_date()
    {
        using var root = Init();
        var id = Add(root, "dated");
        var r = root.Run("set", id, "due=2030-13-40", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("expected YYYY-MM-DD", r.Stdout + r.Stderr);
    }

    [Fact]
    public void Sort_due_puts_nulls_last_both_directions()
    {
        using var root = Init();
        var early = Add(root, "early");
        var late = Add(root, "late");
        var undated = Add(root, "undated");
        Assert.Equal(0, root.Run("set", early, "due=2020-01-01").ExitCode);
        Assert.Equal(0, root.Run("set", late, "due=2040-01-01").ExitCode);

        var asc = ListWithDue(root, "--sort", "due").Select(x => x.id).ToList();
        Assert.Equal(new[] { early, late, undated }, asc);

        var desc = ListWithDue(root, "--sort", "due:desc").Select(x => x.id).ToList();
        Assert.Equal(new[] { late, early, undated }, desc);
    }

    [Fact]
    public void Overdue_filter_matches_only_past_open_items()
    {
        using var root = Init();
        var overdue = Add(root, "overdue");
        var future = Add(root, "future");
        Assert.Equal(0, root.Run("set", overdue, "due=2000-01-01").ExitCode);
        Assert.Equal(0, root.Run("set", future, "due=2999-01-01").ExitCode);

        var ids = ListWithDue(root, "--overdue").Select(x => x.id).ToHashSet();
        Assert.Contains(overdue, ids);
        Assert.DoesNotContain(future, ids);
    }

    [Fact]
    public void Overdue_filter_excludes_done_items()
    {
        using var root = Init();
        var done = Add(root, "done-past");
        Assert.Equal(0, root.Run("set", done, "due=2000-01-01").ExitCode);
        Assert.Equal(0, root.Run("done", done).ExitCode);

        var ids = ListWithDue(root, "--overdue").Select(x => x.id).ToHashSet();
        Assert.DoesNotContain(done, ids);
    }

    /// <summary>
    /// The deliberate asymmetry: --overdue is a status (a closed item is never
    /// overdue), --due-before/--due-after are raw date windows so retrospective
    /// queries stay possible. Composing with --open narrows a window to live
    /// work. Pinned because the two read as one family and a well-meant
    /// "consistency" fix would silently remove the retrospective query.
    /// </summary>
    [Fact]
    public void Date_windows_keep_closed_items_but_compose_with_open()
    {
        using var root = Init();
        var done = Add(root, "done-past");
        var open = Add(root, "open-past");
        Assert.Equal(0, root.Run("set", done, "due=2000-01-01").ExitCode);
        Assert.Equal(0, root.Run("set", open, "due=2000-01-01").ExitCode);
        Assert.Equal(0, root.Run("done", done).ExitCode);

        var window = ListWithDue(root, "--due-before", "2010-01-01").Select(x => x.id).ToHashSet();
        Assert.Contains(done, window);
        Assert.Contains(open, window);

        var live = ListWithDue(root, "--due-before", "2010-01-01", "--open").Select(x => x.id).ToHashSet();
        Assert.DoesNotContain(done, live);
        Assert.Contains(open, live);
    }

    [Fact]
    public void Due_before_and_after_filters_compose_on_date()
    {
        using var root = Init();
        var a = Add(root, "a");
        var b = Add(root, "b");
        Assert.Equal(0, root.Run("set", a, "due=2030-06-01").ExitCode);
        Assert.Equal(0, root.Run("set", b, "due=2030-08-01").ExitCode);

        var before = ListWithDue(root, "--due-before", "2030-07-01").Select(x => x.id).ToHashSet();
        Assert.Contains(a, before);
        Assert.DoesNotContain(b, before);

        var after = ListWithDue(root, "--due-after", "2030-07-01").Select(x => x.id).ToHashSet();
        Assert.Contains(b, after);
        Assert.DoesNotContain(a, after);
    }

    [Fact]
    public void Due_before_rejects_malformed_date()
    {
        using var root = Init();
        var r = root.Run("list", "--due-before", "soon", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("invalid --due-before", r.Stdout + r.Stderr);
    }

    [Fact]
    public void List_json_emits_raw_iso_due_without_glyph()
    {
        using var root = Init();
        var id = Add(root, "overdue");
        Assert.Equal(0, root.Run("set", id, "due=2000-01-01").ExitCode);

        var due = ListWithDue(root).Single(x => x.id == id).due;
        Assert.Equal("2000-01-01", due);
    }

    [Fact]
    public void List_human_output_strips_color_but_keeps_overdue_glyph()
    {
        using var root = Init();
        var id = Add(root, "overdue");
        Assert.Equal(0, root.Run("set", id, "due=2000-01-01").ExitCode);

        // stdout is redirected in tests → ANSI/markup auto-stripped, but the
        // monochrome-safe ⚠ glyph and the date text survive.
        var r = root.Run("list", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("⚠", r.Stdout);
        Assert.Contains("2000-01-01", r.Stdout);
        Assert.DoesNotContain("[red]", r.Stdout);
    }

    [Fact]
    public void Tree_shows_due_date_on_node()
    {
        using var root = Init();
        var id = Add(root, "dated");
        Assert.Equal(0, root.Run("set", id, "due=2999-01-01").ExitCode);
        var r = root.Run("tree", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("2999-01-01", r.Stdout);
    }
}
