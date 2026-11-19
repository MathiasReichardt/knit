using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Repeatable --type / --state, --exclude-type /
/// --exclude-state, and --open shorthand on `list` and `tree`.
/// Tree promotes matching descendants past non-matching ancestors and
/// marks them with ↑ in human output.
/// </summary>
[Collection(nameof(CliCollection))]
public class FilterTests
{
    // epic ── feature ── task   (todo)
    //              └─── bug    (todo)
    //              └─── task2  (done)
    private static (TempKnitRoot root, string epic, string feat, string task1, string bug, string task2) Build()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var epic = JsonDocument.Parse(root.Run("add", "epic", "main epic", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var feat = JsonDocument.Parse(root.Run("add", "feature", "feat one", "--parent", epic, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var task1 = JsonDocument.Parse(root.Run("add", "task", "task one", "--parent", feat, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var bug = JsonDocument.Parse(root.Run("add", "bug", "shiny bug", "--parent", epic, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var task2 = JsonDocument.Parse(root.Run("add", "task", "finished task", "--parent", feat, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("done", task2).ExitCode);
        return (root, epic, feat, task1, bug, task2);
    }

    private static HashSet<string> ListIds(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args.Prepend("list").Append("--json").ToArray());
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        return doc.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToHashSet();
    }

    [Fact]
    public void List_type_repeatable_is_or_within_flag()
    {
        var (root, epic, _, task1, bug, task2) = Build();
        using (root)
        {
            var ids = ListIds(root, "--type", "task", "--type", "bug");
            Assert.Contains(task1, ids);
            Assert.Contains(task2, ids);
            Assert.Contains(bug, ids);
            Assert.DoesNotContain(epic, ids);
        }
    }

    [Fact]
    public void List_exclude_type_hides_those_items()
    {
        var (root, _, _, _, bug, _) = Build();
        using (root)
        {
            var ids = ListIds(root, "--exclude-type", "bug");
            Assert.DoesNotContain(bug, ids);
        }
    }

    [Fact]
    public void List_open_hides_done_items()
    {
        var (root, _, _, task1, _, task2) = Build();
        using (root)
        {
            var ids = ListIds(root, "--open");
            Assert.Contains(task1, ids);
            Assert.DoesNotContain(task2, ids);
        }
    }

    [Fact]
    public void List_open_conflicts_with_state()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("list", "--open", "--state", "todo", "--json");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("--open cannot be combined", r.Stdout + r.Stderr);
        }
    }

    [Fact]
    public void List_open_conflicts_with_exclude_state()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("list", "--open", "--exclude-state", "blocked", "--json");
            Assert.Equal(2, r.ExitCode);
        }
    }

    [Fact]
    public void List_include_and_exclude_intersect()
    {
        var (root, _, _, task1, _, task2) = Build();
        using (root)
        {
            // type=task ∧ state≠done → only task1
            var ids = ListIds(root, "--type", "task", "--exclude-state", "done");
            Assert.Contains(task1, ids);
            Assert.DoesNotContain(task2, ids);
        }
    }

    [Fact]
    public void List_unknown_type_is_bad_argument()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("list", "--type", "spaceship", "--json");
            Assert.Equal(2, r.ExitCode);
        }
    }

    [Fact]
    public void Tree_exclude_type_bug_hides_bug_subtree()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--exclude-type", "bug");
            Assert.Equal(0, r.ExitCode);
            Assert.DoesNotContain("shiny bug", r.Stdout);
            Assert.Contains("task one", r.Stdout);
        }
    }

    [Fact]
    public void Tree_open_hides_done_descendants()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--open");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("task one", r.Stdout);
            Assert.DoesNotContain("finished task", r.Stdout);
        }
    }

    [Fact]
    public void Tree_filter_flat_promotes_descendants_with_marker()
    {
        // --flat opts back into the promotion rule: epic (non-matching)
        // and feature (non-matching) are skipped; task1 and task2 are
        // promoted up. Promoted items get the ↑ marker in human output.
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--type", "task", "--flat", "--no-color");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("task one", r.Stdout);
            Assert.Contains("↑", r.Stdout);
            Assert.DoesNotContain("main epic", r.Stdout);
            Assert.DoesNotContain("feat one", r.Stdout);
        }
    }

    [Fact]
    public void Tree_filter_context_keeps_dim_ancestors()
    {
        // Default (context) mode keeps non-matching ancestors visible so
        // the user can see where a match lives in the hierarchy.
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--type", "task", "--no-color");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("task one", r.Stdout);
            Assert.Contains("main epic", r.Stdout);
            Assert.Contains("feat one", r.Stdout);
            Assert.DoesNotContain("↑", r.Stdout);
        }
    }

    [Fact]
    public void Tree_filter_json_keeps_parent_pointer_when_flat()
    {
        // Promoted items retain their real parent id in JSON so callers
        // can reconstruct the real hierarchy.
        var (root, _, _, task1, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--type", "task", "--flat", "--json");
            Assert.Equal(0, r.ExitCode);
            using var doc = JsonDocument.Parse(r.Stdout);
            var roots = doc.RootElement.GetProperty("roots");
            var match = roots.EnumerateArray()
                .First(n => n.GetProperty("item").GetProperty("id").GetString() == task1);
            // parent id is unchanged — task1's parent in storage is the feature.
            var parent = match.GetProperty("item").GetProperty("parent").GetString();
            Assert.False(string.IsNullOrEmpty(parent));
        }
    }

    [Fact]
    public void Tree_filter_context_json_marks_ancestors_as_non_matching()
    {
        // In context mode JSON, ancestor nodes get match=false.
        var (root, epic, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--type", "task", "--json");
            Assert.Equal(0, r.ExitCode);
            using var doc = JsonDocument.Parse(r.Stdout);
            var roots = doc.RootElement.GetProperty("roots");
            var epicNode = roots.EnumerateArray()
                .First(n => n.GetProperty("item").GetProperty("id").GetString() == epic);
            Assert.False(epicNode.GetProperty("match").GetBoolean());
        }
    }

    [Fact]
    public void Tree_open_conflicts_with_state()
    {
        var (root, _, _, _, _, _) = Build();
        using (root)
        {
            var r = root.Run("tree", "--open", "--state", "todo", "--json");
            Assert.Equal(2, r.ExitCode);
        }
    }
}
