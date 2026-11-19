using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class TimelineTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var args = parent is null
            ? new[] { "add", type, title, "--json" }
            : new[] { "add", type, title, "--parent", parent, "--json" };
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        var m = Regex.Match(r.Stdout, "\"id\": \"([^\"]+)\"");
        Assert.True(m.Success, $"no id in: {r.Stdout}");
        return m.Groups[1].Value;
    }

    private static string InDays(int days) =>
        DateOnly.FromDateTime(DateTime.Now).AddDays(days).ToString("yyyy-MM-dd");

    // Ids that surface as their own timeline node (vs. folded into a coarser
    // node's `rolled_up` list, which carries id+title objects).
    private static HashSet<string> NodeIds(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        return doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> RolledIds(string stdout, string nodeId)
    {
        using var doc = JsonDocument.Parse(stdout);
        var node = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("id").GetString() == nodeId);
        return node.GetProperty("rolled_up").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()!)
            .ToList();
    }

    [Fact]
    public void Json_emits_nodes_edges_and_at_risk()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var blocker = Add(root, "task", "undated blocker");
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("link", blocker, "--blocks", target).ExitCode);

        var r = root.Run("timeline", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"nodes\":", r.Stdout);
        Assert.Contains("\"edges\":", r.Stdout);
        // The undated blocker of a dated item is at-risk, with the target's
        // due as its implied deadline.
        Assert.Contains($"\"implied_deadline\": \"{InDays(10)}\"", r.Stdout);
        Assert.Contains("\"at_risk\": true", r.Stdout);
        // The endangered target names the culprit.
        Assert.Matches(new Regex($"\"at_risk_blockers\":\\s*\\[\\s*\"{blocker}\""), r.Stdout);
        // Blocker edge, flagged.
        Assert.Matches(new Regex(
            $"\"from\": \"{blocker}\",\\s*\"to\": \"{target}\",\\s*\"kind\": \"blocks\",\\s*\"at_risk\": true"), r.Stdout);
    }

    [Fact]
    public void Blocker_finishing_in_time_is_not_at_risk()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var blocker = Add(root, "task", "timely blocker");
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("set", blocker, $"due={InDays(5)}").ExitCode);
        Assert.Equal(0, root.Run("link", blocker, "--blocks", target).ExitCode);

        var r = root.Run("timeline", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("\"at_risk\": true", r.Stdout);
    }

    [Fact]
    public void Block_by_produces_a_timeline_edge_and_blocked_reason()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var blocker = Add(root, "task", "undated blocker");
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        // The natural blocking flow: only a Blocked-by row on the target —
        // no `link --blocks` on the blocker.
        Assert.Equal(0, root.Run("block", target, "--by", blocker).ExitCode);

        var r = root.Run("timeline", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Matches(new Regex($"\"from\": \"{blocker}\",\\s*\"to\": \"{target}\""), r.Stdout);
        Assert.Contains("\"at_risk\": true", r.Stdout); // undated blocker of dated work
        Assert.Contains("\"blocked\": \"blocked by", r.Stdout); // live flag on the node

        var agenda = root.Run("timeline");
        Assert.Equal(0, agenda.ExitCode);
        Assert.Contains("⊘ blocked by", agenda.Stdout);
    }

    [Fact]
    public void Blocks_and_blocked_by_rows_for_one_pair_emit_one_edge()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var blocker = Add(root, "task", "undated blocker");
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("link", blocker, "--blocks", target).ExitCode);
        Assert.Equal(0, root.Run("block", target, "--by", blocker).ExitCode);

        var r = root.Run("timeline", "--json");
        Assert.Equal(0, r.ExitCode);
        var edges = Regex.Matches(r.Stdout, $"\"from\": \"{blocker}\",\\s*\"to\": \"{target}\"");
        Assert.Single(edges);
    }

    [Fact]
    public void Filtered_slate_keeps_out_of_filter_blocker_as_ghost()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        var target = Add(root, "task", "in the slate");
        var blocker = Add(root, "task", "outside the slate");
        Assert.Equal(0, root.Run("set", target, "slate=q1").ExitCode);
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("link", blocker, "--blocks", target).ExitCode);

        var r = root.Run("timeline", "--slate", "q1", "--json");
        Assert.Equal(0, r.ExitCode);
        // The out-of-filter at-risk blocker surfaces as a ghost node, and
        // its edge to the endangered item survives.
        Assert.Contains($"\"id\": \"{blocker}\"", r.Stdout);
        Assert.Contains("\"ghost\": true", r.Stdout);
        Assert.Matches(new Regex($"\"from\": \"{blocker}\",\\s*\"to\": \"{target}\""), r.Stdout);
    }

    [Fact]
    public void Depth_epic_rolls_tasks_into_span()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var epic = Add(root, "epic", "container epic");
        var task = Add(root, "task", "hidden dated task", epic);
        Assert.Equal(0, root.Run("set", task, $"due={InDays(5)}").ExitCode);

        var r = root.Run("timeline", "--depth", "epic", "--json");
        Assert.Equal(0, r.ExitCode);
        var ids = NodeIds(r.Stdout);
        Assert.Contains(epic, ids);
        Assert.DoesNotContain(task, ids);                 // not its own node
        Assert.Contains($"\"span_start\": \"{InDays(5)}\"", r.Stdout);
        Assert.Contains(task, RolledIds(r.Stdout, epic)); // folded into the epic
    }

    [Fact]
    public void Depth_project_rolls_epics_and_tasks_into_the_project()
    {
        // Project altitude: a project-typed item is the coarsest level —
        // at --depth project its descendant epics/tasks roll into it (they do
        // not surface as their own nodes), and its span covers their dues.
        using var root = new TempKnitRoot();
        Init(root);
        var program = Add(root, "project", "the program");
        var epic = Add(root, "epic", "epic under program", program);
        var task = Add(root, "task", "dated leaf", epic);
        Assert.Equal(0, root.Run("set", task, $"due={InDays(5)}").ExitCode);

        var r = root.Run("timeline", "--depth", "project", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        var ids = NodeIds(r.Stdout);
        Assert.Contains(program, ids);
        Assert.DoesNotContain(epic, ids);                     // epic rolled in, not its own node
        Assert.Contains($"\"span_start\": \"{InDays(5)}\"", r.Stdout);
        Assert.Contains(task, RolledIds(r.Stdout, program));  // leaf due covers the project span
    }

    [Fact]
    public void Hide_todos_drops_todo_nodes_but_keeps_an_at_risk_todo_blocker()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var program = Add(root, "project", "todo home"); // todos root at a project
        var todoBlocker = Add(root, "todo", "undated todo blocker", program);
        var standalone = Add(root, "todo", "standalone todo", program);
        Assert.Equal(0, root.Run("set", target, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("set", standalone, $"due={InDays(8)}").ExitCode);
        Assert.Equal(0, root.Run("link", todoBlocker, "--blocks", target).ExitCode);

        // Depth all so todos are their own nodes (else they roll up anyway).
        var r = root.Run("timeline", "--depth", "all", "--hide-todos", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        // A plain todo with no risk role is hidden outright.
        Assert.DoesNotContain($"\"id\": \"{standalone}\"", r.Stdout);
        // The todo blocking dated work resurfaces as a ghost at-risk blocker —
        // hide-todos restricts rendering, never the risk propagation.
        Assert.Contains($"\"id\": \"{todoBlocker}\"", r.Stdout);
        Assert.Matches(new Regex($"\"id\": \"{todoBlocker}\"[\\s\\S]*?\"ghost\": true"), r.Stdout);
    }

    [Fact]
    public void Depth_project_rolls_an_orphan_epics_task_into_the_epic_but_keeps_a_parentless_item()
    {
        // At a coarse depth an item whose chain has no ancestor of that
        // rank rolls into its chain root (the orphan epic here) rather than
        // surfacing as a scattered leaf; only a truly parentless item shows at
        // its own level.
        using var root = new TempKnitRoot();
        Init(root);
        var epic = Add(root, "epic", "orphan epic");        // root — no project above it
        var task = Add(root, "task", "epic leaf", epic);
        var loose = Add(root, "task", "parentless leaf");   // no parent chain at all
        Assert.Equal(0, root.Run("set", task, $"due={InDays(5)}").ExitCode);
        Assert.Equal(0, root.Run("set", loose, $"due={InDays(6)}").ExitCode);

        var r = root.Run("timeline", "--depth", "project", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        var ids = NodeIds(r.Stdout);
        // The epic surfaces as the chain root, with the task rolled into it...
        Assert.Contains(epic, ids);
        Assert.DoesNotContain(task, ids);
        Assert.Contains(task, RolledIds(r.Stdout, epic));
        // ...while the parentless task still shows at its own level.
        Assert.Contains(loose, ids);
    }

    [Fact]
    public void Node_carries_parent_path_and_rolled_up_objects()
    {
        // Each node carries a coarsest-first ancestor breadcrumb, and
        // rolled-up descendants are objects (id + title + type + due), so the
        // hidden work is legible without a second query.
        using var root = new TempKnitRoot();
        Init(root);
        var program = Add(root, "project", "the program");
        var epic = Add(root, "epic", "the epic", program);
        var task = Add(root, "task", "leaf work", epic);
        Assert.Equal(0, root.Run("set", task, $"due={InDays(4)}").ExitCode);

        var r = root.Run("timeline", "--depth", "epic", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var epicNode = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .First(n => n.GetProperty("id").GetString() == epic);
        Assert.Equal("the program", epicNode.GetProperty("parent_path").GetString());
        var rolled = epicNode.GetProperty("rolled_up").EnumerateArray().First();
        Assert.Equal(task, rolled.GetProperty("id").GetString());
        Assert.Equal("leaf work", rolled.GetProperty("title").GetString());
        Assert.Equal("task", rolled.GetProperty("type").GetString());
        Assert.Equal(InDays(4), rolled.GetProperty("due").GetString());
    }

    [Fact]
    public void Hide_disconnected_drops_roots_too_fine_for_the_depth()
    {
        // --hide-disconnected drops chain roots whose own rank is finer
        // than --depth (they surfaced only for lack of a coarser container),
        // while a real item of the selected rank stays.
        using var root = new TempKnitRoot();
        Init(root);
        var program = Add(root, "project", "the program");
        var ptask = Add(root, "task", "program leaf", program);
        var epic = Add(root, "epic", "orphan epic");
        var etask = Add(root, "task", "epic leaf", epic);
        Assert.Equal(0, root.Run("set", ptask, $"due={InDays(5)}").ExitCode);
        Assert.Equal(0, root.Run("set", etask, $"due={InDays(6)}").ExitCode);

        var r = root.Run("timeline", "--depth", "project", "--hide-disconnected", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains($"\"id\": \"{program}\"", r.Stdout);    // real project stays
        Assert.DoesNotContain($"\"id\": \"{epic}\"", r.Stdout); // orphan epic (+ its leaf) dropped
    }

    [Fact]
    public void Hide_disconnected_is_a_no_op_at_depth_all()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var epic = Add(root, "epic", "orphan epic");
        var etask = Add(root, "task", "epic leaf", epic);
        Assert.Equal(0, root.Run("set", etask, $"due={InDays(6)}").ExitCode);

        var r = root.Run("timeline", "--depth", "all", "--hide-disconnected", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        // At depth all every type is within depth → nothing is "disconnected".
        Assert.Contains($"\"id\": \"{epic}\"", r.Stdout);
        Assert.Contains($"\"id\": \"{etask}\"", r.Stdout);
    }

    [Fact]
    public void Window_defaults_future_month_and_extends_back_for_overdue()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var far = Add(root, "task", "far future task");
        var overdue = Add(root, "task", "overdue task");
        Assert.Equal(0, root.Run("set", far, $"due={InDays(300)}").ExitCode);
        Assert.Equal(0, root.Run("set", overdue, $"due={InDays(-10)}").ExitCode);

        var month = root.Run("timeline", "--json");
        Assert.Equal(0, month.ExitCode);
        Assert.DoesNotContain($"\"id\": \"{far}\"", month.Stdout);
        Assert.Contains($"\"id\": \"{overdue}\"", month.Stdout);
        Assert.Contains($"\"from\": \"{InDays(-10)}\"", month.Stdout);

        var all = root.Run("timeline", "--range", "all", "--json");
        Assert.Equal(0, all.ExitCode);
        Assert.Contains($"\"id\": \"{far}\"", all.Stdout);
    }

    [Fact]
    public void Agenda_annotates_at_risk_and_unassigned()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var target = Add(root, "task", "dated target");
        var blocker = Add(root, "task", "undated blocker");
        Assert.Equal(0, root.Run("set", target, $"due={InDays(2)}").ExitCode);
        Assert.Equal(0, root.Run("link", blocker, "--blocks", target).ExitCode);

        var r = root.Run("timeline");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("⚠ blocked by", r.Stdout);
        Assert.Contains("undated, unassigned", r.Stdout);
        Assert.Contains("unassigned!", r.Stdout); // the at-risk blocker line
        Assert.Contains("(undated)", r.Stdout);   // the undated section header
        Assert.Contains("due in 2d", r.Stdout);
    }

    [Fact]
    public void Milestone_filter_narrows_and_json_node_carries_the_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(20)).ExitCode);
        // An undated milestone has no axis position — it never joins the overlay.
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "someday").ExitCode);

        var linked = Add(root, "task", "in the cut");
        var other = Add(root, "task", "not in the cut");
        Assert.Equal(0, root.Run("set", linked, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("set", other, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("link", linked, "--milestone", "relaunch/beta").ExitCode);

        // Unfiltered --json carries the overlay: the dated milestone appears with
        // its target + state; the undated one is omitted (no axis position).
        var full = root.Run("timeline", "--range", "all", "--json");
        Assert.Equal(0, full.ExitCode);
        Assert.Contains("\"milestones\":", full.Stdout);
        Assert.Contains("\"slug\": \"beta\"", full.Stdout);
        Assert.Contains($"\"target\": \"{InDays(20)}\"", full.Stdout);
        Assert.Contains("\"state\": \"planned\"", full.Stdout);
        Assert.DoesNotContain("\"slug\": \"someday\"", full.Stdout);

        // Bare slug is unambiguous (one project carries it) — mirrors list/tree.
        var r = root.Run("timeline", "--milestone", "beta", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains($"\"id\": \"{linked}\"", r.Stdout);
        Assert.DoesNotContain($"\"id\": \"{other}\"", r.Stdout);
        // The rendered node surfaces its milestone as the qualified ref.
        Assert.Contains("\"milestone\": \"relaunch/beta\"", r.Stdout);
        // The overlay is narrowed to the filtered milestone.
        Assert.Contains("\"slug\": \"beta\"", r.Stdout);

        // Unknown milestone is a bad-argument error, not an empty timeline.
        var bad = root.Run("timeline", "--milestone", "nope", "--json");
        Assert.NotEqual(0, bad.ExitCode);
    }

    [Fact]
    public void Project_filter_narrows_to_the_project_subtree()
    {
        // --project scopes to items whose nearest project-type ancestor
        // points at the doc folder (containment, not a Milestone link). An
        // item outside that tree — even one dated in the same window — drops.
        using var root = new TempKnitRoot();
        Init(root);
        var mk = root.Run("project", "new", "relaunch", "--json");
        Assert.Equal(0, mk.ExitCode);
        var program = JsonDocument.Parse(mk.Stdout).RootElement.GetProperty("item_id").GetString()!;

        var epic = Add(root, "epic", "epic under program", program);
        var inside = Add(root, "task", "in the project", epic);
        var outside = Add(root, "task", "outside the project");
        Assert.Equal(0, root.Run("set", inside, $"due={InDays(10)}").ExitCode);
        Assert.Equal(0, root.Run("set", outside, $"due={InDays(10)}").ExitCode);

        var r = root.Run("timeline", "--project", "relaunch", "--depth", "all", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        var ids = NodeIds(r.Stdout);
        Assert.Contains(inside, ids);
        Assert.Contains(program, ids);        // the project item itself is in scope
        Assert.DoesNotContain(outside, ids);

        // Unknown project is a bad-argument error, not an empty timeline.
        var bad = root.Run("timeline", "--project", "nope", "--json");
        Assert.NotEqual(0, bad.ExitCode);
    }

    [Fact]
    public void Undated_item_in_a_dated_milestone_is_at_risk()
    {
        // End-to-end: the milestone's target is an implied deadline for
        // its linked item even though the item itself carries no due date.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(15)).ExitCode);
        var task = Add(root, "task", "undated but committed");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        var r = root.Run("timeline", "--range", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Matches(new Regex(
            $"\"id\": \"{task}\"[\\s\\S]*?\"at_risk\": true,[\\s\\S]*?\"implied_deadline\": \"{InDays(15)}\""),
            r.Stdout);
    }

    [Fact]
    public void Undated_task_under_an_epic_in_a_dated_milestone_is_at_risk()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(15)).ExitCode);
        var epic = Add(root, "epic", "the cut");
        var task = Add(root, "task", "inherits the target", epic);
        Assert.Equal(0, root.Run("link", epic, "--milestone", "relaunch/beta").ExitCode);

        var r = root.Run("timeline", "--range", "all", "--depth", "all", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Matches(new Regex(
            $"\"id\": \"{task}\"[\\s\\S]*?\"milestone\": \"relaunch/beta\"[\\s\\S]*?\"at_risk\": true,[\\s\\S]*?\"implied_deadline\": \"{InDays(15)}\""),
            r.Stdout);
    }

    // The overlay used to exist only in --json.
    [Fact]
    public void Text_agenda_renders_dated_milestones_in_their_date_group()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(10)).ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "someday").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "far-off", "--target", InDays(200)).ExitCode);
        var task = Add(root, "task", "committed work");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        var r = root.Run("timeline", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("◆ relaunch/beta", r.Stdout);
        Assert.Contains("1 open", r.Stdout);            // the linked item is still open
        Assert.Contains(InDays(10), r.Stdout);          // its own date group header
        Assert.DoesNotContain("someday", r.Stdout);     // undated: no axis position
        Assert.DoesNotContain("far-off", r.Stdout);     // outside the default month window
    }

    // --include-done fetches completed work, so the window start has to
    // reach back over it — extending only for open items fetched it and then
    // dropped it behind the start.
    [Fact]
    public void Include_done_widens_the_window_back_over_finished_work()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var add = root.Run("add", "task", "shipped last month", "--due", InDays(-30), "--json");
        Assert.Equal(0, add.ExitCode);
        var task = Regex.Match(add.Stdout, "\"id\": \"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(0, root.Run("done", task).ExitCode);

        Assert.DoesNotContain("shipped last month", root.Run("timeline", "--no-color").Stdout);

        var r = root.Run("timeline", "--include-done", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("shipped last month", r.Stdout);
    }

    [Fact]
    public void A_passed_milestone_widens_the_window_and_is_flagged()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(-9)).ExitCode);

        // A roadmap-only repo is not "no items on the timeline".
        var r = root.Run("timeline", "--no-color");
        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("no items on the timeline", r.Stdout);
        Assert.Contains("relaunch/beta", r.Stdout);
        Assert.Contains("target passed", r.Stdout);

        // Reached is history: it stops widening the window (only unreached
        // targets do), so it leaves the default agenda entirely — and where
        // the window does cover it, it reads as reached, not as a warning.
        Assert.Equal(0, root.Run("project", "milestone", "set", "relaunch/beta", "--state", "reached").ExitCode);
        Assert.DoesNotContain("relaunch/beta", root.Run("timeline", "--no-color").Stdout);
        var back = root.Run("timeline", "--from", InDays(-30), "--no-color");
        Assert.Contains("relaunch/beta", back.Stdout);
        Assert.Contains("reached", back.Stdout);
        Assert.DoesNotContain("target passed", back.Stdout);
    }

    [Fact]
    public void Dropped_milestones_follow_include_done()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(10)).ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "set", "relaunch/beta", "--state", "dropped").ExitCode);

        Assert.DoesNotContain("relaunch/beta", root.Run("timeline", "--no-color").Stdout);
        Assert.Contains("relaunch/beta", root.Run("timeline", "--include-done", "--no-color").Stdout);
        // --json follows the same rule so the two views agree on what exists.
        Assert.DoesNotContain("\"slug\": \"beta\"", root.Run("timeline", "--json").Stdout);
    }

    [Fact]
    public void Project_filter_narrows_the_milestone_overlay_too()
    {
        // The overlay used to ignore --project, so a scoped timeline showed
        // other projects' roadmaps over this project's work.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "sunset", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--target", InDays(10)).ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "sunset", "cutover", "--target", InDays(12)).ExitCode);

        var r = root.Run("timeline", "--project", "relaunch", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"slug\": \"beta\"", r.Stdout);
        Assert.DoesNotContain("\"slug\": \"cutover\"", r.Stdout);

        var text = root.Run("timeline", "--project", "relaunch", "--no-color");
        Assert.Contains("relaunch/beta", text.Stdout);
        Assert.DoesNotContain("sunset/cutover", text.Stdout);
    }

    [Fact]
    public void Range_and_explicit_window_are_mutually_exclusive()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("timeline", "--range", "week", "--from", InDays(0));
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--range", r.Stderr);

        var badDepth = root.Run("timeline", "--depth", "story");
        Assert.NotEqual(0, badDepth.ExitCode);
        Assert.Contains("--depth", badDepth.Stderr);
    }
}
