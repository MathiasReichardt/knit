using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Query blocked work and ask what someone *else* can pick up — the two flags that made blocked/other-people's work unreachable
/// without knowing ids up front.
/// </summary>
[Collection(nameof(CliCollection))]
public class BlockedQueryTests
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

    private static HashSet<string> ListIds(TempKnitRoot root, params string[] args)
    {
        var r = root.Run([.. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"list failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement
            .GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToHashSet();
    }

    [Fact]
    public void List_blocked_shows_only_flagged_items_and_carries_the_reason()
    {
        using var root = NewRepo();
        var stuck = Add(root, "add", "task", "stuck");
        var fine = Add(root, "add", "task", "fine");
        Assert.Equal(0, root.Run("block", stuck, "--reason", "waiting on the loft").ExitCode);

        var ids = ListIds(root, "list", "--blocked");
        Assert.Contains(stuck, ids);
        Assert.DoesNotContain(fine, ids);

        var r = root.Run("list", "--blocked", "--json");
        var blocked = JsonDocument.Parse(r.Stdout).RootElement
            .GetProperty("items").EnumerateArray().Single();
        Assert.Equal("waiting on the loft", blocked.GetProperty("blocked").GetString());
    }

    [Fact]
    public void A_blocked_by_link_alone_blocks()
    {
        using var root = NewRepo();
        var a = Add(root, "add", "task", "a");
        var b = Add(root, "add", "task", "b");
        Assert.Equal(0, root.Run("block", a, "--by", b).ExitCode);
        // Clearing the reason leaves the row — still an impediment.
        Assert.Equal(0, root.Run("set", a, "blocked=").ExitCode);

        Assert.Equal(new HashSet<string> { a }, ListIds(root, "list", "--blocked"));
    }

    [Fact]
    public void Blocked_and_ready_are_refused_together()
    {
        using var root = NewRepo();
        var r = root.Run("list", "--blocked", "--ready");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--blocked", r.Stderr);

        var t = root.Run("tree", "--blocked", "--ready");
        Assert.NotEqual(0, t.ExitCode);
    }

    [Fact]
    public void Blocked_items_are_marked_in_list_and_tree()
    {
        using var root = NewRepo();
        var stuck = Add(root, "add", "task", "stuck");
        Assert.Equal(0, root.Run("block", stuck, "--reason", "waiting").ExitCode);

        Assert.Contains("⊘", root.Run("list", "--no-color").Stdout);
        Assert.Contains("⊘", root.Run("tree", "--no-color").Stdout);
    }

    [Fact]
    public void Ready_and_next_can_be_asked_on_someone_elses_behalf()
    {
        using var root = NewRepo();
        var hers = Add(root, "add", "task", "alice's task");
        var mine = Add(root, "add", "task", "my task");
        Assert.Equal(0, root.Run("assign", hers, "alice").ExitCode);
        Assert.Equal(0, root.Run("assign", mine, "ci").ExitCode);

        var ready = ListIds(root, "ready", "--assignee", "alice");
        Assert.Equal([hers], ready);

        var r = root.Run("next", "--assignee", "alice", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(hers, JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void Mine_and_assignee_are_refused_together()
    {
        using var root = NewRepo();
        Assert.NotEqual(0, root.Run("ready", "--mine", "--assignee", "alice").ExitCode);
        Assert.NotEqual(0, root.Run("next", "--mine", "--assignee", "alice").ExitCode);
    }

    [Fact]
    public void Start_reports_a_takeover_and_keep_assignee_prevents_it()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "bob's task");
        Assert.Equal(0, root.Run("assign", id, "bob").ExitCode);

        var kept = root.Run("start", id, "--keep-assignee", "--json");
        Assert.Equal(0, kept.ExitCode);
        var el = JsonDocument.Parse(kept.Stdout).RootElement;
        Assert.Equal("bob", el.GetProperty("assignee").GetString());
        Assert.Equal("in-progress", el.GetProperty("state").GetString());

        var taken = root.Run("start", id, "--no-color");
        Assert.Equal(0, taken.ExitCode);
        Assert.Contains("took over from", taken.Stdout);
        Assert.Contains("bob", taken.Stdout);
    }

    [Fact]
    public void Starting_an_unassigned_item_says_nothing_extra()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "nobody's task");

        var r = root.Run("start", id, "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("took over", r.Stdout);
    }
}
