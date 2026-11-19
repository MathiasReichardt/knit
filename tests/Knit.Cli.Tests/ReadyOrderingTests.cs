using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `next --under`, `ready --under`,
/// `list --ready`, `tree --ready`. `ready` is the flat inventory (Rank asc,
/// containers included); the leaf-only recommendation rule behind `next`
/// lives in <see cref="NextPickTests"/>.
/// </summary>
[Collection(nameof(CliCollection))]
public class ReadyOrderingTests
{
    /// <summary>
    /// Tree:
    ///   E1 (epic)         has open children
    ///     F1 (feature)    has open children
    ///       T1 (task)     leaf
    ///       T2 (task)     leaf
    ///   E2 (epic)         has open children
    ///     T3 (task)       leaf
    /// All the same rank, so nothing but the leaf rule separates them.
    /// </summary>
    private static (TempKnitRoot root, string e1, string f1, string t1, string t2, string e2, string t3) BuildTree()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        string Add(params string[] args)
        {
            var r = root.Run(args);
            Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
            return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
        }

        var e1 = Add("add", "epic", "epic 1", "--json");
        var f1 = Add("add", "feature", "feat 1", "--parent", e1, "--json");
        var t1 = Add("add", "task", "task 1", "--parent", f1, "--json");
        var t2 = Add("add", "task", "task 2", "--parent", f1, "--json");
        var e2 = Add("add", "epic", "epic 2", "--json");
        var t3 = Add("add", "task", "task 3", "--parent", e2, "--json");

        foreach (var id in new[] { e1, f1, t1, t2, e2, t3 })
            Assert.Equal(0, root.Run("set", id, "rank=m").ExitCode);

        return (root, e1, f1, t1, t2, e2, t3);
    }

    [Fact]
    public void Next_picks_a_leaf_never_a_container()
    {
        var (root, e1, f1, t1, t2, e2, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("next", "--json");
            Assert.Equal(0, r.ExitCode);
            var id = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString();
            Assert.Contains(id, new[] { t1, t2, t3 });
            Assert.DoesNotContain(id, new[] { e1, f1, e2 });
        }
    }

    [Fact]
    public void Next_under_scopes_to_subtree()
    {
        var (root, _, _, _, _, e2, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("next", "--under", e2, "--json");
            Assert.Equal(0, r.ExitCode);
            var id = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString();
            Assert.Equal(t3, id);
        }
    }

    [Fact]
    public void Next_positional_id_still_works_as_alias()
    {
        var (root, _, _, _, _, e2, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("next", e2, "--json");
            Assert.Equal(0, r.ExitCode);
            var id = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString();
            Assert.Equal(t3, id);
        }
    }

    [Fact]
    public void Next_rejects_positional_and_under_together()
    {
        var (root, e1, _, _, _, e2, _) = BuildTree();
        using (root)
        {
            var r = root.Run("next", e1, "--under", e2, "--json");
            Assert.NotEqual(0, r.ExitCode);
        }
    }

    [Fact]
    public void Next_emits_null_when_nothing_ready()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var r = root.Run("next", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("null" + Environment.NewLine, r.Stdout);
    }

    [Fact]
    public void Ready_under_scopes_to_subtree()
    {
        var (root, e1, _, t1, t2, _, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("ready", "--under", e1, "--json");
            Assert.Equal(0, r.ExitCode);
            var ids = JsonDocument.Parse(r.Stdout).RootElement
                .GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("id").GetString())
                .ToHashSet();
            Assert.Contains(t1, ids);
            Assert.Contains(t2, ids);
            Assert.DoesNotContain(t3, ids);
        }
    }

    /// <summary>
    /// `ready` is the inventory of workable items, so containers belong in it —
    /// the leaf rule is `next`'s job alone.
    /// </summary>
    [Fact]
    public void Ready_lists_containers_alongside_leaves()
    {
        var (root, e1, f1, t1, t2, e2, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("ready", "--json");
            Assert.Equal(0, r.ExitCode);
            var ids = JsonDocument.Parse(r.Stdout).RootElement
                .GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("id").GetString()!)
                .ToHashSet();
            foreach (var id in new[] { e1, f1, t1, t2, e2, t3 })
                Assert.Contains(id, ids);
        }
    }

    [Fact]
    public void List_ready_filters_to_ready_set()
    {
        var (root, e1, f1, t1, t2, e2, t3) = BuildTree();
        using (root)
        {
            // Mark t1 done — only t2 + t3 should remain ready.
            Assert.Equal(0, root.Run("done", t1).ExitCode);

            var r = root.Run("list", "--ready", "--json");
            Assert.Equal(0, r.ExitCode);
            var ids = JsonDocument.Parse(r.Stdout).RootElement
                .GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("id").GetString())
                .ToHashSet();
            Assert.DoesNotContain(t1, ids);
            Assert.Contains(t2, ids);
            Assert.Contains(t3, ids);
            // Epics + feature are not in `todo`-with-no-blockers state by themselves,
            // but knit treats new items as todo. They should also appear unless not-ready.
            // The key invariant: done items are filtered out.
        }
    }

    [Fact]
    public void List_ready_composes_with_under()
    {
        var (root, e1, _, t1, t2, _, t3) = BuildTree();
        using (root)
        {
            var r = root.Run("list", "--ready", "--under", e1, "--json");
            Assert.Equal(0, r.ExitCode);
            var ids = JsonDocument.Parse(r.Stdout).RootElement
                .GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("id").GetString())
                .ToHashSet();
            Assert.Contains(t1, ids);
            Assert.Contains(t2, ids);
            Assert.DoesNotContain(t3, ids);
        }
    }

    [Fact]
    public void Tree_ready_hides_done_items_and_promotes_ready_descendants()
    {
        var (root, e1, f1, t1, t2, _, _) = BuildTree();
        using (root)
        {
            // Mark f1 done — but t1, t2 are still todo (knit doesn't cascade).
            // tree --ready should hide f1 and promote t1/t2.
            Assert.Equal(0, root.Run("done", f1).ExitCode);

            var r = root.Run("tree", "--ready", "--flat", "--under", e1, "--json");
            Assert.Equal(0, r.ExitCode);
            using var doc = JsonDocument.Parse(r.Stdout);
            var roots = doc.RootElement.GetProperty("roots");

            // The under-root e1 is itself still todo (a ready epic), so it stays
            // as the root and its non-ready feature f1 is replaced by its
            // ready descendants t1, t2.
            Assert.Equal(1, roots.GetArrayLength());
            var rootChildIds = roots[0].GetProperty("children").EnumerateArray()
                .Select(c => c.GetProperty("item").GetProperty("id").GetString())
                .ToHashSet();
            Assert.DoesNotContain(f1, rootChildIds);
            Assert.Contains(t1, rootChildIds);
            Assert.Contains(t2, rootChildIds);
        }
    }

    [Fact]
    public void Ready_empty_set_emits_dim_message_or_empty_json()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var human = root.Run("ready");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("nothing ready", human.Stdout);

        var jsonResult = root.Run("ready", "--json");
        Assert.Equal(0, jsonResult.ExitCode);
        using var doc = JsonDocument.Parse(jsonResult.Stdout);
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }
}
