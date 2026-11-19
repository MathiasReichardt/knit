using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `next` recommends leaf work only (`ready` stays the full
/// inventory) and orders it in-progress → overdue → due-soon → priority →
/// due date → rank, printing why the pick won.
/// </summary>
[Collection(nameof(CliCollection))]
public class NextPickTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, params string[] args)
    {
        var r = root.Run([.. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static string? Next(TempKnitRoot root)
    {
        var r = root.Run("next", "--json");
        Assert.Equal(0, r.ExitCode);
        var el = JsonDocument.Parse(r.Stdout).RootElement;
        return el.ValueKind == JsonValueKind.Null ? null : el.GetProperty("id").GetString();
    }

    private static string Day(int offset) =>
        DateOnly.FromDateTime(DateTime.Now).AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public void Container_becomes_pickable_once_its_children_close()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "epic 1");
        var task = Add(root, "add", "task", "task 1", "--parent", epic);

        Assert.Equal(task, Next(root));

        Assert.Equal(0, root.Run("done", task).ExitCode);
        // No open children left — the epic is workable directly now.
        Assert.Equal(epic, Next(root));
    }

    [Fact]
    public void A_project_is_never_picked()
    {
        using var root = NewRepo();
        var project = Add(root, "add", "project", "website relaunch");
        Assert.Null(Next(root));

        var task = Add(root, "add", "task", "task 1", "--parent", project);
        Assert.Equal(task, Next(root));
    }

    [Fact]
    public void In_progress_beats_a_better_ranked_todo()
    {
        using var root = NewRepo();
        var todo = Add(root, "add", "task", "untouched");
        var started = Add(root, "add", "task", "already going");
        Assert.Equal(0, root.Run("set", todo, "rank=a").ExitCode);
        Assert.Equal(0, root.Run("set", started, "rank=z").ExitCode);
        Assert.Equal(0, root.Run("start", started).ExitCode);

        Assert.Equal(started, Next(root));
    }

    [Fact]
    public void Deadline_pressure_beats_priority_which_beats_rank()
    {
        using var root = NewRepo();
        var overdue = Add(root, "add", "task", "overdue");
        var soon = Add(root, "add", "task", "due soon");
        var critical = Add(root, "add", "task", "critical");
        Assert.Equal(0, root.Run("set", overdue, "rank=z").ExitCode);
        Assert.Equal(0, root.Run("set", soon, "rank=m").ExitCode);
        Assert.Equal(0, root.Run("set", critical, "rank=a").ExitCode);
        Assert.Equal(0, root.Run("set", overdue, $"due={Day(-2)}").ExitCode);
        Assert.Equal(0, root.Run("set", soon, $"due={Day(1)}").ExitCode);
        Assert.Equal(0, root.Run("set", critical, "priority=critical").ExitCode);

        Assert.Equal(overdue, Next(root));

        Assert.Equal(0, root.Run("done", overdue).ExitCode);
        Assert.Equal(soon, Next(root));

        Assert.Equal(0, root.Run("done", soon).ExitCode);
        Assert.Equal(critical, Next(root));
    }

    [Fact]
    public void Pick_explains_itself()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "leaky thing");
        Assert.Equal(0, root.Run("set", id, $"due={Day(-1)}").ExitCode);
        Assert.Equal(0, root.Run("set", id, "priority=critical").ExitCode);
        Assert.Equal(0, root.Run("start", id).ExitCode);

        var r = root.Run("next", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("in progress", r.Stdout);
        Assert.Contains("overdue by 1 day", r.Stdout);
        Assert.Contains("critical priority", r.Stdout);
    }

    [Fact]
    public void Pick_with_no_standout_signal_says_so()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "plain task");

        var r = root.Run("next", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("first in rank order", r.Stdout);
    }
}
