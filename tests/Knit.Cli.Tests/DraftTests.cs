using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

/// <summary>
/// The `draft` state. An agent-created item had no way to say "not
/// specified or approved yet" — so anything it invented was immediately
/// pickable work.
/// </summary>
[Collection(nameof(CliCollection))]
public class DraftTests
{
    private static string Flat(string s) => Regex.Replace(s, @"\s+", " ");

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

    private static string StateOf(TempKnitRoot root, string id)
    {
        var r = root.Run("show", id, "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("state").GetString()!;
    }

    [Fact]
    public void A_draft_round_trips_through_the_file()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Speculative", "--draft");

        Assert.Equal("draft", StateOf(root, id));
        Assert.Contains("draft", root.Run("list").Stdout);
    }

    [Fact]
    public void Verify_stays_silent_on_a_draft()
    {
        // A draft is not a defect.
        using var root = NewRepo();
        Add(root, "add", "task", "Speculative", "--draft");

        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Initial_state_config_flips_every_item_type()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("config", "set", "initial_state", "draft").ExitCode);

        string? project = null;
        foreach (var type in new[] { "project", "epic", "task" })
        {
            var id = Add(root, "add", type, $"A {type}");
            Assert.Equal("draft", StateOf(root, id));
            project ??= id;
        }
        // A todo needs a container, but the state rule is the same.
        Assert.Equal("draft", StateOf(root, Add(root, "add", "todo", "A todo", "--parent", project!)));
    }

    [Fact]
    public void The_flag_beats_the_config_in_both_directions()
    {
        using var root = NewRepo();
        var draftByFlag = Add(root, "add", "task", "Speculative", "--draft");
        Assert.Equal("draft", StateOf(root, draftByFlag));

        Assert.Equal(0, root.Run("config", "set", "initial_state", "draft").ExitCode);
        var todoByFlag = Add(root, "add", "task", "Obvious", "--todo");
        Assert.Equal("todo", StateOf(root, todoByFlag));
    }

    [Fact]
    public void An_absent_initial_state_key_still_reads_todo()
    {
        // The source-gen trap: a property initializer would read back
        // as null and every new item would land in an unparseable state.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Default");

        Assert.Equal("todo", StateOf(root, id));
    }

    [Fact]
    public void A_machine_wide_initial_state_applies_to_a_repo_that_never_set_it()
    {
        // The motivating case is an agent: "work I create is proposed, not
        // committed" follows whoever is typing, not the repo.
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"initial_state":"draft"}""");
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        Assert.Equal("draft", StateOf(root, Add(root, "add", "task", "Proposed")));
    }

    [Fact]
    public void A_repo_setting_beats_the_machine_default()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"initial_state":"draft"}""");
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("config", "set", "initial_state", "todo").ExitCode);

        Assert.Equal("todo", StateOf(root, Add(root, "add", "task", "Ready")));
    }

    [Fact]
    public void Contradicting_flags_are_refused()
    {
        using var root = NewRepo();

        var r = root.Run("add", "task", "Confused", "--draft", "--todo");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("contradict", r.Stderr);
    }

    [Fact]
    public void Config_set_refuses_a_state_that_is_not_todo_or_draft()
    {
        using var root = NewRepo();

        var r = root.Run("config", "set", "initial_state", "in-progress");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("todo", r.Stderr);
    }

    [Fact]
    public void Next_and_ready_never_return_a_draft()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Unapproved", "--draft");

        Assert.DoesNotContain("Unapproved", root.Run("ready").Stdout);
        Assert.DoesNotContain("Unapproved", root.Run("next").Stdout);
    }

    [Fact]
    public void Approve_promotes_a_draft_to_todo()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Unapproved", "--draft");

        Assert.Equal(0, root.Run("approve", id).ExitCode);

        Assert.Equal("todo", StateOf(root, id));
        Assert.Contains("Unapproved", root.Run("ready").Stdout);
    }

    [Fact]
    public void Approve_on_an_item_that_is_not_a_draft_is_refused()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Already ready");

        var r = root.Run("approve", id);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("not a draft", r.Stderr);
    }

    [Fact]
    public void Start_on_a_draft_is_refused_for_an_agent_and_names_the_promotion()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Unapproved", "--draft");

        var r = root.Run("start", id, "--json");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("draft", r.Stderr);
        Assert.Contains("knit approve", r.Stderr);
        Assert.Equal("draft", StateOf(root, id));
    }

    [Fact]
    public void Done_straight_from_a_draft_is_refused()
    {
        // Closing work nobody specified is a mistake, not a shortcut.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Unapproved", "--draft");

        var r = root.Run("done", id);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit approve", Flat(r.Stderr));
        Assert.Contains("knit dismiss", Flat(r.Stderr));
        Assert.Equal("draft", StateOf(root, id));
    }

    [Fact]
    public void A_draft_counts_in_the_rollup_and_gets_its_own_bucket()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Unapproved", "--draft");
        Add(root, "add", "task", "Ready one");

        var r = root.Run("status", "--json");

        Assert.Equal(0, r.ExitCode);
        var s = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal(2, s.GetProperty("total").GetInt32());
        Assert.Equal(1, s.GetProperty("draft").GetInt32());
        Assert.Equal(1, s.GetProperty("todo").GetInt32());
    }

    [Fact]
    public void A_dated_draft_is_still_overdue()
    {
        // Not having specified the work does not move the deadline.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Late draft", "--draft", "--due", "2020-01-01");

        var r = root.Run("status", "--json");

        Assert.Equal(1, JsonDocument.Parse(r.Stdout).RootElement.GetProperty("overdue").GetInt32());
        Assert.Equal("draft", StateOf(root, id));
    }

    [Fact]
    public void A_draft_child_keeps_its_parent_off_the_leaf_work_list()
    {
        // The child is real planned work, so the parent is still a container.
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Container");
        Add(root, "add", "task", "Unapproved child", "--parent", epic, "--draft");

        Assert.DoesNotContain("Container", root.Run("next").Stdout);
    }

    [Fact]
    public void Sort_state_desc_puts_drafts_before_todo()
    {
        using var root = NewRepo();
        Add(root, "add", "task", "Bbb ready");
        Add(root, "add", "task", "Aaa unapproved", "--draft");

        var stdout = root.Run("list", "--sort", "state:desc").Stdout;

        Assert.True(stdout.IndexOf("Aaa unapproved", StringComparison.Ordinal)
                    < stdout.IndexOf("Bbb ready", StringComparison.Ordinal));
    }
}
