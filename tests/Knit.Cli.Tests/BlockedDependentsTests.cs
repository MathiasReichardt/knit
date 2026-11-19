using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Closing a blocker resolves nothing, so every closing command
/// names the open items still blocked by it (`blocked_dependents` in
/// `--json`), and `verify` warns on each row left behind.
/// </summary>
[Collection(nameof(CliCollection))]
public class BlockedDependentsTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string title, string type = "task") =>
        JsonDocument.Parse(root.Run("add", type, title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

    private static void BlockBy(TempKnitRoot root, string item, string blocker) =>
        Assert.Equal(0, root.Run("block", item, "--by", blocker).ExitCode);

    private static JsonElement Json(CliResult r)
    {
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement;
    }

    private static List<JsonElement> Dependents(JsonElement item) =>
        item.GetProperty("blocked_dependents").EnumerateArray().ToList();

    [Fact]
    public void Done_reports_a_sole_dependent_with_unblock()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var dep = Add(root, "dependent");
        BlockBy(root, dep, blocker);

        var human = root.Run("done", blocker);
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("1 item still blocked by", human.Stdout);
        Assert.Contains("dependent", human.Stdout);
        Assert.Contains("→ knit unblock ", human.Stdout);

        root.Run("reopen", blocker);
        var d = Assert.Single(Dependents(Json(root.Run("done", blocker, "--json"))));
        Assert.Equal(dep, d.GetProperty("id").GetString());
        Assert.Equal("dependent", d.GetProperty("title").GetString());
        Assert.False(d.TryGetProperty("remaining", out _));

        // Nothing resolved: the dependent stays blocked.
        Assert.True(Json(root.Run("show", dep, "--json")).TryGetProperty("blocked", out _));
    }

    [Fact]
    public void A_dependent_with_another_impediment_gets_unlink_and_its_remaining_reason()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var dep = Add(root, "dependent");
        BlockBy(root, dep, blocker);
        Assert.Equal(0, root.Run("block", dep, "--reason", "waiting on vendor").ExitCode);

        var human = root.Run("done", blocker);
        Assert.Contains("(also waiting on vendor)", human.Stdout);
        Assert.Contains("→ knit unlink ", human.Stdout);

        root.Run("reopen", blocker);
        var d = Assert.Single(Dependents(Json(root.Run("done", blocker, "--json"))));
        Assert.Equal("waiting on vendor", d.GetProperty("remaining").GetString());
    }

    [Fact]
    public void Several_dependents_are_each_reported()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var a = Add(root, "dep a");
        var b = Add(root, "dep b");
        BlockBy(root, a, blocker);
        BlockBy(root, b, blocker);

        var ids = Dependents(Json(root.Run("done", blocker, "--json")))
            .Select(d => d.GetProperty("id").GetString()).ToHashSet();
        Assert.Equal([a, b], ids);
    }

    [Fact]
    public void A_close_with_nothing_blocked_carries_an_empty_list()
    {
        using var root = Init();
        var item = Add(root, "lonely");
        var r = root.Run("done", item);
        Assert.DoesNotContain("still blocked", r.Stdout);

        root.Run("reopen", item);
        Assert.Empty(Dependents(Json(root.Run("done", item, "--json"))));
    }

    [Fact]
    public void A_call_that_closes_nothing_reports_nothing()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var dep = Add(root, "dependent");
        BlockBy(root, dep, blocker);
        Assert.Equal(0, root.Run("dismiss", blocker).ExitCode);

        // dismissed → done stays closed.
        var again = Json(root.Run("done", blocker, "--json"));
        Assert.False(again.TryGetProperty("blocked_dependents", out _));
        Assert.DoesNotContain("still blocked", root.Run("done", blocker).Stdout);

        // A non-closing mutator never carries the field.
        Assert.False(Json(root.Run("assign", dep, "bob", "--json")).TryGetProperty("blocked_dependents", out _));
    }

    [Fact]
    public void Dismiss_and_set_state_report_like_done()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var dep = Add(root, "dependent");
        BlockBy(root, dep, blocker);

        Assert.Single(Dependents(Json(root.Run("dismiss", blocker, "--json"))));
        root.Run("reopen", blocker);
        Assert.Single(Dependents(Json(root.Run("set", blocker, "state=done", "--json"))));
        root.Run("reopen", blocker);
        Assert.Contains("still blocked by", root.Run("set", blocker, "state=dismissed").Stdout);
    }

    [Fact]
    public void Cascade_reports_per_closed_item_and_skips_dependents_it_closed()
    {
        using var root = Init();
        var feature = Add(root, "feature", "feature");
        var child = Add(root, "child");
        var sibling = Add(root, "sibling");
        var outside = Add(root, "outside");
        Assert.Equal(0, root.Run("link", child, "--parent", feature).ExitCode);
        Assert.Equal(0, root.Run("link", sibling, "--parent", feature).ExitCode);
        BlockBy(root, outside, child);
        BlockBy(root, sibling, child); // dismissed by the cascade, so not reported

        var result = Json(root.Run("dismiss", feature, "--cascade", "--json"));
        Assert.Empty(Dependents(result.GetProperty("item")));
        var cascadedChild = result.GetProperty("cascaded").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == child);
        var d = Assert.Single(Dependents(cascadedChild));
        Assert.Equal(outside, d.GetProperty("id").GetString());
    }

    [Fact]
    public void A_blocks_row_is_a_plan_and_never_reported()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var planned = Add(root, "planned");
        Assert.Equal(0, root.Run("link", blocker, "--blocks", planned).ExitCode);

        Assert.Empty(Dependents(Json(root.Run("done", blocker, "--json"))));
        Assert.Empty(StaleBlockers(root));
    }

    private static List<JsonElement> StaleBlockers(TempKnitRoot root)
    {
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode); // warning-severity
        return JsonDocument.Parse(r.Stdout).RootElement
            .GetProperty("stale_blockers").EnumerateArray().ToList();
    }

    [Fact]
    public void Verify_warns_per_row_with_the_fix()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var sole = Add(root, "sole dep");
        var other = Add(root, "other dep");
        var stillOpen = Add(root, "still open");
        BlockBy(root, sole, blocker);
        BlockBy(root, other, blocker);
        BlockBy(root, other, stillOpen);
        Assert.Equal(0, root.Run("done", blocker).ExitCode);

        var findings = StaleBlockers(root);
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal(blocker, f.GetProperty("blocker_id").GetString());
            Assert.Equal("done", f.GetProperty("blocker_state").GetString());
        });
        Assert.True(findings.Single(f => f.GetProperty("item_id").GetString() == sole).GetProperty("sole").GetBoolean());
        Assert.False(findings.Single(f => f.GetProperty("item_id").GetString() == other).GetProperty("sole").GetBoolean());

        var human = root.Run("verify");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("open items blocked by closed ones (2)", human.Stdout);
        Assert.Contains("→ knit unblock ", human.Stdout);
        Assert.Contains("→ knit unlink ", human.Stdout);
        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);

        Assert.Equal(0, root.Run("unblock", sole, "--json").ExitCode);
        Assert.Single(StaleBlockers(root));
    }

    [Fact]
    public void Verify_ignores_a_closed_dependent()
    {
        using var root = Init();
        var blocker = Add(root, "blocker");
        var dep = Add(root, "dependent");
        BlockBy(root, dep, blocker);
        Assert.Equal(0, root.Run("done", blocker).ExitCode);
        Assert.Equal(0, root.Run("dismiss", dep).ExitCode);

        Assert.Empty(StaleBlockers(root));
    }
}
