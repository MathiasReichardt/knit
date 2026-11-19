using System.Text.Json;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Data;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>knit reorder</c>: which siblings it ranks against, and the
/// automatic respacing that keeps a move from being refused. There is
/// deliberately no <c>rebalance</c> verb: respacing is a consequence of a
/// reorder the caller already asked for, not a command to learn.
/// </summary>
[Collection(nameof(CliCollection))]
public class ReorderRankTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

    private static ItemStore Store(TempKnitRoot root) =>
        new(root.Path, new SimpleFrontmatterSerializer());

    private static string Add(TempKnitRoot root, string title)
    {
        var res = root.Run("add", "task", title, "--json");
        Assert.Equal(0, res.ExitCode);
        return JsonDocument.Parse(res.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Force a legacy rank onto an item — the only way into the
    /// blocked state now that the generator never produces one.</summary>
    private static void Poke(TempKnitRoot root, string id, string rank)
    {
        var store = Store(root);
        store.Write(store.Read(id)! with { Rank = rank });
    }

    [Fact]
    public void Reorder_to_the_top_respaces_a_blocked_group_instead_of_refusing()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var first = Add(root, "one");
        var mover = Add(root, "two");
        // Nothing sorts before "a", so without respacing this move has no answer.
        Poke(root, first, "a");

        var res = root.Run("reorder", mover, "--before", first);

        Assert.Equal(0, res.ExitCode);
        Assert.Contains("respaced", res.Stdout);

        var store = Store(root);
        Assert.True(string.CompareOrdinal(store.Read(mover)!.Rank, store.Read(first)!.Rank) < 0);
    }

    [Fact]
    public void Reorder_into_a_closed_gap_between_siblings_also_respaces()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var lower = Add(root, "one");
        var upper = Add(root, "two");
        var mover = Add(root, "three");
        // Nothing fits between "n" and "na".
        Poke(root, lower, "n");
        Poke(root, upper, "na");

        var res = root.Run("reorder", mover, "--before", upper);

        Assert.Equal(0, res.ExitCode);
        var store = Store(root);
        var ranks = new[] { lower, mover, upper }.Select(id => store.Read(id)!.Rank!).ToList();
        Assert.Equal(ranks.OrderBy(r => r, StringComparer.Ordinal), ranks);
    }

    [Fact]
    public void Reorder_leaves_a_healthy_group_alone()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var first = Add(root, "one");
        var mover = Add(root, "two");
        var firstRankBefore = Store(root).Read(first)!.Rank;

        var res = root.Run("reorder", mover, "--before", first);

        Assert.Equal(0, res.ExitCode);
        Assert.DoesNotContain("respaced", res.Stdout);
        // Only the moved item is written — no collateral rank churn.
        Assert.Equal(firstRankBefore, Store(root).Read(first)!.Rank);
    }

    [Fact]
    public void Reorder_refuses_a_target_under_another_parent()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var parent = Add(root, "parent");
        var child = Add(root, "child");
        Assert.Equal(0, root.Run("link", child, "--parent", parent).ExitCode);
        var loner = Add(root, "loner");
        var rankBefore = Store(root).Read(loner)!.Rank;

        // A rank only orders siblings. Ranking a top-level item against a
        // nested one produced a value compared against its own siblings —
        // an arbitrary placement, written silently.
        var res = root.Run("reorder", loner, "--before", child);

        Assert.NotEqual(0, res.ExitCode);
        Assert.Contains("not a sibling", res.Stderr);
        Assert.Contains("--parent", res.Stderr);
        Assert.Equal(rankBefore, Store(root).Read(loner)!.Rank);
    }

    [Fact]
    public void Reorder_places_among_shared_siblings()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var parent = Add(root, "parent");
        var childA = Add(root, "child a");
        var childB = Add(root, "child b");
        var childC = Add(root, "child c");
        foreach (var c in new[] { childA, childB, childC })
            Assert.Equal(0, root.Run("link", c, "--parent", parent).ExitCode);

        Assert.Equal(0, root.Run("reorder", childC, "--before", childA).ExitCode);

        var store = Store(root);
        var ordered = new[] { childC, childA, childB }.Select(id => store.Read(id)!.Rank!).ToList();
        Assert.Equal(ordered.OrderBy(r => r, StringComparer.Ordinal), ordered);
    }

    [Fact]
    public void Respacing_preserves_the_existing_order()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var a = Add(root, "one");
        var b = Add(root, "two");
        var c = Add(root, "three");
        Poke(root, a, "a");
        Poke(root, b, "g");
        Poke(root, c, "n");

        var mover = Add(root, "four");
        Assert.Equal(0, root.Run("reorder", mover, "--before", a).ExitCode);

        var store = Store(root);
        var ranks = new[] { a, b, c }.Select(id => store.Read(id)!.Rank!).ToList();
        Assert.Equal(ranks.OrderBy(r => r, StringComparer.Ordinal), ranks);
        Assert.All(ranks, r => Assert.False(Rank.IsGapClosed("", r)));
    }
}
