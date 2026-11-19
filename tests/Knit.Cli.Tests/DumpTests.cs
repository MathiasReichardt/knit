using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `--dump` on `list` / `tree`. An agent that has to *understand* a
/// slice paid one `knit show` per item; the flag makes the same filters emit
/// the contents behind them.
/// </summary>
[Collection(nameof(CliCollection))]
public class DumpTests
{
    private const string Rule = "===== knit ";

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

    private static int Records(string stdout) => stdout.Split(Rule).Length - 1;

    [Fact]
    public void A_dumped_item_carries_its_body_links_and_comments()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Exporter");
        var task = Add(root, "add", "task", "Write the CSV writer", "--parent", epic);
        Assert.Equal(0, root.Run("edit", task, "--set-section", "Notes", "--value", "quote every field").ExitCode);
        Assert.Equal(0, root.Run("comment", task, "started on this").ExitCode);

        var r = root.Run("list", "--under", task, "--dump");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("quote every field", r.Stdout);
        Assert.Contains("## knit:comments", r.Stdout);
        Assert.Contains("started on this", r.Stdout);
        Assert.Contains("## knit:links", r.Stdout);
    }

    [Fact]
    public void The_header_carries_both_ids_so_the_agent_can_address_what_it_reads()
    {
        // The whole reason a dump beats `cat`-ing the folder.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Addressable");

        var r = root.Run("list", "--dump");

        Assert.Contains($"{Rule}{id} (", r.Stdout);
    }

    [Fact]
    public void A_body_that_contains_markdown_rules_and_fences_survives_verbatim()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Fenced");
        var value = "---\n" + "```json\n" + "{ \"a\": 1 }\n" + "```\n" + "# not a title";
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Notes", "--value", value).ExitCode);

        var r = root.Run("list", "--dump");

        Assert.Contains("```json", r.Stdout);
        Assert.Contains("# not a title", r.Stdout);
        // One item in, one record separator out.
        Assert.Equal(1, Records(r.Stdout));
    }

    [Fact]
    public void The_leading_h1_is_dropped_because_it_restates_the_title()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Only once");

        var r = root.Run("list", "--dump");

        Assert.Contains("title: Only once", r.Stdout);
        Assert.DoesNotContain("# Only once", r.Stdout);
    }

    [Fact]
    public void Dump_changes_the_output_not_the_selection()
    {
        using var root = NewRepo();
        var keep = Add(root, "add", "task", "Open one");
        var drop = Add(root, "add", "task", "Closed one");
        Assert.Equal(0, root.Run("done", drop).ExitCode);

        var r = root.Run("list", "--open", "--dump");

        Assert.Contains(keep, r.Stdout);
        Assert.DoesNotContain(drop, r.Stdout);
    }

    [Fact]
    public void The_index_is_printed_before_the_contents()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Indexed first");

        var r = root.Run("list", "--dump");

        Assert.True(r.Stdout.IndexOf("Indexed first", StringComparison.Ordinal)
                    < r.Stdout.IndexOf(Rule, StringComparison.Ordinal));
    }

    [Fact]
    public void A_dump_above_the_ceiling_is_refused_rather_than_truncated()
    {
        // A short dump that *looks* complete is the failure this guards.
        using var root = NewRepo();
        for (var i = 0; i < 4; i++) Add(root, "add", "task", $"Task {i}");

        var r = root.Run("list", "--dump", "--dump-max", "2");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("4 items", r.Stderr);
        Assert.Contains("--dump-max 4", r.Stderr);
        Assert.Equal(0, Records(r.Stdout));
    }

    [Fact]
    public void The_ceiling_can_be_raised_once_the_count_is_known()
    {
        using var root = NewRepo();
        for (var i = 0; i < 4; i++) Add(root, "add", "task", $"Task {i}");

        var r = root.Run("list", "--dump", "--dump-max", "4");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(4, Records(r.Stdout));
    }

    [Fact]
    public void The_size_is_reported_on_stderr_so_stdout_stays_pipeable()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Measured");

        var r = root.Run("list", "--dump");

        Assert.Contains("dumped 1 item", r.Stderr);
        Assert.DoesNotContain("dumped 1 item", r.Stdout);
    }

    [Fact]
    public void Tree_dump_follows_tree_order_and_keeps_context_ancestors()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Parent epic");
        var task = Add(root, "add", "task", "Critical child", "--parent", epic, "--priority", "critical");

        var r = root.Run("tree", "--min-priority", "critical", "--dump");

        Assert.Equal(0, r.ExitCode);
        // The epic did not match the filter but is kept for structure — and
        // dumped, because its spec is what makes the child readable.
        Assert.Contains($"{Rule}{epic} ", r.Stdout);
        Assert.Contains("· context =====", r.Stdout);
        Assert.True(r.Stdout.IndexOf($"{Rule}{epic} ", StringComparison.Ordinal)
                    < r.Stdout.IndexOf($"{Rule}{task} ", StringComparison.Ordinal));
    }

    [Fact]
    public void Flat_drops_the_context_ancestor_from_the_dump_too()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Parent epic");
        Add(root, "add", "task", "Critical child", "--parent", epic, "--priority", "critical");

        var r = root.Run("tree", "--min-priority", "critical", "--flat", "--dump");

        Assert.DoesNotContain($"{Rule}{epic} ", r.Stdout);
    }

    [Fact]
    public void List_json_dump_keeps_the_items_envelope_and_gains_the_body()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Bodied");
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Notes", "--value", "the note").ExitCode);

        var r = root.Run("list", "--dump", "--json");

        Assert.Equal(0, r.ExitCode);
        var item = Assert.Single(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal("the note", item.GetProperty("notes").GetString());
        Assert.True(item.TryGetProperty("comments", out _));
    }

    [Fact]
    public void Tree_json_dump_keeps_the_nesting_and_swaps_the_payload()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Nested");
        var task = Add(root, "add", "task", "Leaf", "--parent", epic);
        Assert.Equal(0, root.Run("edit", task, "--set-section", "Notes", "--value", "leaf note").ExitCode);

        var r = root.Run("tree", "--dump", "--json");

        Assert.Equal(0, r.ExitCode);
        var rootNode = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("roots").EnumerateArray()
            .Single(n => n.GetProperty("item").GetProperty("id").GetString() == epic);
        var child = Assert.Single(rootNode.GetProperty("children").EnumerateArray());
        Assert.Equal(task, child.GetProperty("item").GetProperty("id").GetString());
        Assert.Equal("leaf note", child.GetProperty("item").GetProperty("notes").GetString());
    }

    [Fact]
    public void Dump_and_watch_are_refused_together()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Whatever");

        var r = root.Run("list", "--dump", "--watch");

        // Under the test harness `--watch`'s own TTY gate fires first; either
        // way the pair never produces a dump.
        Assert.NotEqual(0, r.ExitCode);
        Assert.Equal(0, Records(r.Stdout));
    }
}
