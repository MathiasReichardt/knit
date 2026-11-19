using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// A PM close-out cost six reads and still produced no
/// completion %, per-assignee rollup or blocked list. `knit status` is the
/// one command that answers it; these pin the arithmetic the SPA strip shares
/// via <c>StatusRollup</c>.
/// </summary>
[Collection(nameof(CliCollection))]
public class StatusCommandTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static JsonElement Status(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["status", .. args, "--json"]);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement;
    }

    private static string AddTask(TempKnitRoot root, string title, params string[] flags)
    {
        var r = root.Run(["add", "task", title, .. flags, "--json"]);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public void Status_counts_states_and_reports_completion()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var a = AddTask(root, "a");
        var b = AddTask(root, "b");
        AddTask(root, "c");
        Assert.Equal(0, root.Run("done", a).ExitCode);
        Assert.Equal(0, root.Run("start", b).ExitCode);

        var s = Status(root);
        Assert.Equal("all items", s.GetProperty("scope").GetString());
        Assert.Equal(3, s.GetProperty("total").GetInt32());
        Assert.Equal(1, s.GetProperty("done").GetInt32());
        Assert.Equal(1, s.GetProperty("in_progress").GetInt32());
        Assert.Equal(1, s.GetProperty("todo").GetInt32());
        Assert.Equal(33, s.GetProperty("percent").GetInt32());
    }

    [Fact]
    public void Dismissed_items_leave_the_completion_denominator()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var a = AddTask(root, "a");
        var b = AddTask(root, "b");
        Assert.Equal(0, root.Run("done", a).ExitCode);
        Assert.Equal(0, root.Run("dismiss", b).ExitCode);

        // Descoped work must not cap a finished scope below 100% forever.
        var s = Status(root);
        Assert.Equal(2, s.GetProperty("total").GetInt32());
        Assert.Equal(1, s.GetProperty("dismissed").GetInt32());
        Assert.Equal(100, s.GetProperty("percent").GetInt32());
    }

    [Fact]
    public void Status_rolls_up_blocked_overdue_and_unassigned()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "late", "--due", "2000-01-01", "--assignee", "bob");
        var stuck = AddTask(root, "stuck");
        Assert.Equal(0, root.Run("block", stuck, "--reason", "waiting on vendor").ExitCode);

        var s = Status(root);
        Assert.Equal(1, s.GetProperty("overdue").GetInt32());
        Assert.Equal(1, s.GetProperty("blocked").GetInt32());
        Assert.True(s.GetProperty("unassigned").GetInt32() >= 1);
        Assert.Equal(
            "waiting on vendor",
            s.GetProperty("blocked_items")[0].GetProperty("blocked").GetString());
        Assert.Equal("late", s.GetProperty("overdue_items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void Per_assignee_load_keeps_unassigned_as_a_row()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "a", "--assignee", "bob");
        AddTask(root, "b", "--assignee", "bob");
        AddTask(root, "c");

        var rows = Status(root).GetProperty("by_assignee").EnumerateArray().ToList();
        var bob = rows.Single(r =>
            r.TryGetProperty("assignee", out var a) && a.GetString() == "bob");
        Assert.Equal(2, bob.GetProperty("total").GetInt32());
        // Unassigned rides along as a row of its own (assignee omitted, per the
        // CLI's drop-nulls JSON convention) — a PM reading load needs the
        // unclaimed pile in the same table.
        Assert.Contains(rows, r => !r.TryGetProperty("assignee", out _));
    }

    [Fact]
    public void Under_scopes_to_a_subtree_and_names_the_scope()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var epicR = root.Run("add", "epic", "big", "--json");
        var epic = JsonDocument.Parse(epicR.Stdout).RootElement.GetProperty("id").GetString()!;
        AddTask(root, "child", "--parent", epic);
        AddTask(root, "outsider");

        var s = Status(root, "--under", epic);
        Assert.Equal(2, s.GetProperty("total").GetInt32()); // the epic + its child
        Assert.StartsWith("under: ", s.GetProperty("scope").GetString());
    }

    [Fact]
    public void An_unknown_project_scope_errors_rather_than_reporting_zero()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "a");

        // A silent 0-item report would read as "this project is empty".
        var r = root.Run("status", "--project", "nope");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("unknown project", r.Stderr);
    }

    [Fact]
    public void An_empty_repo_reports_zero_percent_without_dividing_by_zero()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var s = Status(root);
        Assert.Equal(0, s.GetProperty("total").GetInt32());
        Assert.Equal(0, s.GetProperty("percent").GetInt32());

        var human = root.Run("status");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("no items in scope", human.Stdout);
    }
}
