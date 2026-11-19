using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Priority set/reset, --sort priority (critical-first, rank
/// tiebreak), --min-priority threshold, --json always emitting the resolved
/// value, and proof that priority does NOT reorder `ready`.
/// </summary>
[Collection(nameof(CliCollection))]
public class PriorityTests
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

    private static List<(string id, string priority)> ListWithPriority(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args.Prepend("list").Append("--json").ToArray());
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => (
                e.GetProperty("id").GetString()!,
                e.GetProperty("priority").GetString()!))
            .ToList();
    }

    [Fact]
    public void Set_priority_sets_and_resets()
    {
        using var root = Init();
        var id = Add(root, "prio");

        var set = root.Run("set", id, "priority=critical", "--json");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal("critical", JsonDocument.Parse(set.Stdout).RootElement.GetProperty("priority").GetString());

        // Empty resets to the normal default (and is emitted, not omitted).
        var reset = root.Run("set", id, "priority=", "--json");
        Assert.Equal(0, reset.ExitCode);
        Assert.Equal("normal", JsonDocument.Parse(reset.Stdout).RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public void Set_priority_rejects_unknown_level()
    {
        using var root = Init();
        var id = Add(root, "prio");
        var r = root.Run("set", id, "priority=urgent", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("someday|normal|important|critical", r.Stdout + r.Stderr);
    }

    [Fact]
    public void Json_emits_resolved_priority_even_when_defaulted()
    {
        using var root = Init();
        var id = Add(root, "unset");
        var priority = ListWithPriority(root).Single(x => x.id == id).priority;
        Assert.Equal("normal", priority);
    }

    [Fact]
    public void Sort_priority_orders_critical_first_with_rank_tiebreak()
    {
        using var root = Init();
        // Creation order fixes rank ascending: a < b < c < d.
        var a = Add(root, "a");
        var b = Add(root, "b");
        var c = Add(root, "c");
        var d = Add(root, "d");
        Assert.Equal(0, root.Run("set", a, "priority=important").ExitCode);
        Assert.Equal(0, root.Run("set", b, "priority=critical").ExitCode);
        Assert.Equal(0, root.Run("set", c, "priority=important").ExitCode);
        // d stays normal.

        var order = ListWithPriority(root, "--sort", "priority").Select(x => x.id).ToList();
        // critical (b), then important in rank order (a, c), then normal (d).
        Assert.Equal(new[] { b, a, c, d }, order);

        var desc = ListWithPriority(root, "--sort", "priority:desc").Select(x => x.id).ToList();
        // normal (d) first, then important by rank (a, c), then critical (b).
        Assert.Equal(new[] { d, a, c, b }, desc);
    }

    [Fact]
    public void Min_priority_threshold_is_inclusive_upward()
    {
        using var root = Init();
        var someday = Add(root, "someday");
        var normal = Add(root, "normal");
        var important = Add(root, "important");
        var critical = Add(root, "critical");
        Assert.Equal(0, root.Run("set", someday, "priority=someday").ExitCode);
        Assert.Equal(0, root.Run("set", important, "priority=important").ExitCode);
        Assert.Equal(0, root.Run("set", critical, "priority=critical").ExitCode);

        var ids = ListWithPriority(root, "--min-priority", "important").Select(x => x.id).ToHashSet();
        Assert.Contains(important, ids);
        Assert.Contains(critical, ids);
        Assert.DoesNotContain(normal, ids);
        Assert.DoesNotContain(someday, ids);
    }

    [Fact]
    public void Min_priority_rejects_unknown_level()
    {
        using var root = Init();
        var r = root.Run("list", "--min-priority", "urgent", "--json");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("someday|normal|important|critical", r.Stdout + r.Stderr);
    }

    [Fact]
    public void Ready_order_is_not_reordered_by_priority()
    {
        using var root = Init();
        // a created first (earlier rank), left normal; b created second, made
        // critical. Ready order is rank-ascending — priority must not hoist b.
        var a = Add(root, "a");
        var b = Add(root, "b");
        Assert.Equal(0, root.Run("set", b, "priority=critical").ExitCode);

        var r = root.Run("ready", "--json");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var order = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToList();
        Assert.Equal(new[] { a, b }, order);
    }
}
