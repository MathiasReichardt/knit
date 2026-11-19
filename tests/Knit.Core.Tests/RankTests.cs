using Knit.Core.Ids;

namespace Knit.Core.Tests;

public class RankTests
{
    [Fact]
    public void Initial_IsValid()
    {
        Assert.True(Rank.IsValid(Rank.Initial()));
    }

    [Theory]
    [InlineData("a", "c", "b")]
    [InlineData("a", "z", "m")]
    [InlineData("", "n", "g")]
    public void Between_KnownPairs(string lo, string hi, string expected)
    {
        Assert.Equal(expected, Rank.Between(lo, hi));
    }

    [Fact]
    public void Between_OpenUpperBoundPicksMidpoint()
    {
        // Open upper bound: pick a single char between lo[0] and 'z'
        // (no string growth in the common append case).
        var r = Rank.Between("n", "");
        Assert.True(string.CompareOrdinal("n", r) < 0);
        Assert.Single(r);
    }

    [Fact]
    public void Between_RepeatedAppend_StaysShort()
    {
        // Sequence of appends should grow logarithmically, not linearly.
        var cur = Rank.Initial();
        for (var i = 0; i < 10; i++) cur = Rank.Between(cur, "");
        Assert.True(cur.Length <= 3, $"rank grew unexpectedly long: {cur}");
    }

    [Fact]
    public void Between_AdjacentPairProducesExtension()
    {
        // 'a' and 'b' are adjacent; result must extend
        var mid = Rank.Between("a", "b");
        Assert.True(string.CompareOrdinal("a", mid) < 0);
        Assert.True(string.CompareOrdinal(mid, "b") < 0);
        Assert.True(mid.StartsWith('a'));
    }

    [Fact]
    public void Between_StableForRepeatedInsertion()
    {
        // Property: starting from initial, repeatedly insert at random
        // positions; all ranks remain strictly ordered after each insertion.
        // This used to catch InvalidOperationException and skip the insert,
        // which is how the front-insert exhaustion stayed hidden.
        var rng = new Random(42);
        var ranks = new List<string> { Rank.Initial() };
        for (var i = 0; i < 200; i++)
        {
            var pos = rng.Next(0, ranks.Count + 1);
            var lo = pos == 0 ? "" : ranks[pos - 1];
            var hi = pos == ranks.Count ? "" : ranks[pos];
            ranks.Insert(pos, Rank.Between(lo, hi));
            for (var j = 1; j < ranks.Count; j++)
            {
                Assert.True(string.CompareOrdinal(ranks[j - 1], ranks[j]) < 0,
                    $"order broken at {j}: {ranks[j - 1]} >= {ranks[j]}");
            }
        }
    }

    [Fact]
    public void Between_RepeatedFrontInsert_NeverExhausts()
    {
        // The reported bug: `n -> g -> c -> a -> throw` gave up after three.
        var ranks = new List<string> { Rank.Initial() };
        for (var i = 0; i < 100; i++)
        {
            var next = Rank.Between("", ranks[0]);
            Assert.True(string.CompareOrdinal(next, ranks[0]) < 0, $"{next} >= {ranks[0]}");
            ranks.Insert(0, next);
        }
        // One extra character per ~4 front-inserts, not one per insert.
        Assert.True(ranks[0].Length <= 30, $"rank grew unexpectedly long: {ranks[0]}");
    }

    [Fact]
    public void Between_NeverClosesAGapItCreates()
    {
        // The invariant that makes "insert before" always answerable.
        var rng = new Random(7);
        var ranks = new List<string> { Rank.Initial() };
        for (var i = 0; i < 300; i++)
        {
            var pos = rng.Next(0, ranks.Count + 1);
            var lo = pos == 0 ? "" : ranks[pos - 1];
            var hi = pos == ranks.Count ? "" : ranks[pos];
            var mid = Rank.Between(lo, hi);
            // A gap can only be closed when its upper bound ends in 'a', so
            // "never end in 'a'" is exactly what keeps every slot reachable.
            // 'a' as a *prefix* is fine and expected ("an", "aan").
            Assert.False(mid.EndsWith('a'), $"generated a terminal 'a': {mid}");
            Assert.False(Rank.IsGapClosed(lo, mid));
            if (hi.Length > 0) Assert.False(Rank.IsGapClosed(mid, hi));
            ranks.Insert(pos, mid);
        }
    }

    [Fact]
    public void Between_OnAClosedGap_SaysWhichBoundsAreAtFault()
    {
        // Core names no remedy — it does not know which surface is asking.
        var ex = Assert.Throws<InvalidOperationException>(() => Rank.Between("", "a"));
        Assert.Contains("before rank 'a'", ex.Message);
        Assert.DoesNotContain("reindex", ex.Message);
        // The between-two-siblings case names both bounds, not just one rank.
        var pair = Assert.Throws<InvalidOperationException>(() => Rank.Between("n", "na"));
        Assert.Contains("between 'n' and 'na'", pair.Message);
    }

    [Fact]
    public void Spread_IsEvenlySpacedAndOrdered()
    {
        foreach (var count in new[] { 1, 2, 3, 24, 25, 100 })
        {
            var ranks = Rank.Spread(count);
            Assert.Equal(count, ranks.Length);
            Assert.All(ranks, r => Assert.True(Rank.IsValid(r)));
            Assert.All(ranks, r => Assert.DoesNotContain('a', r));
            for (var i = 1; i < ranks.Length; i++)
                Assert.True(string.CompareOrdinal(ranks[i - 1], ranks[i]) < 0,
                    $"count {count}: {ranks[i - 1]} >= {ranks[i]}");
            Assert.Single(ranks.Select(r => r.Length).Distinct());
        }
    }

    [Fact]
    public void Spread_One_IsInitial()
    {
        Assert.Equal(new[] { Rank.Initial() }, Rank.Spread(1));
    }

    [Fact]
    public void Spread_Empty()
    {
        Assert.Empty(Rank.Spread(0));
    }

    [Fact]
    public void Spread_LeavesRoomAtBothEnds()
    {
        // The point of avoiding 'a' and 'z': after a rebalance you can still
        // insert above the first row and append after the last.
        var ranks = Rank.Spread(24);
        var above = Rank.Between("", ranks[0]);
        Assert.True(string.CompareOrdinal(above, ranks[0]) < 0);
        var below = Rank.Between(ranks[^1], "");
        Assert.True(string.CompareOrdinal(ranks[^1], below) < 0);
        Assert.Single(below);
    }

    [Fact]
    public void LegacyRanks_KeepTheirOrderAndStayValid()
    {
        // Legacy ranks are read and sorted exactly as before:
        // ordering is ordinal string comparison, which did not change, and
        // nothing rejects a rank ending in 'a'.
        string[] legacy = ["a", "c", "g", "n", "t", "w", "y", "z", "zn", "zt", "an", "aa"];
        var sorted = legacy.OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.All(legacy, r => Assert.True(Rank.IsValid(r), $"{r} stopped being valid"));
        Assert.Equal(
            new[] { "a", "aa", "an", "c", "g", "n", "t", "w", "y", "z", "zn", "zt" },
            sorted);
    }

    [Fact]
    public void Between_LegacyNeighbours_StillLandsBetweenThem()
    {
        // Inserting among legacy ranks must not reorder what is there.
        string[] legacy = ["an", "c", "g", "n", "t", "w", "y", "z", "zn", "zt"];
        for (var i = 0; i + 1 < legacy.Length; i++)
        {
            var mid = Rank.Between(legacy[i], legacy[i + 1]);
            Assert.True(string.CompareOrdinal(legacy[i], mid) < 0, $"{legacy[i]} >= {mid}");
            Assert.True(string.CompareOrdinal(mid, legacy[i + 1]) < 0, $"{mid} >= {legacy[i + 1]}");
        }
        // Appending after a legacy tail, and inserting before a legacy head
        // that does not end in 'a', both still work.
        Assert.True(string.CompareOrdinal("zt", Rank.Between("zt", "")) < 0);
        Assert.True(string.CompareOrdinal(Rank.Between("", "c"), "c") < 0);
        // Even a legacy rank ending in 'a' can be inserted *before*, as long
        // as it is not all 'a's — only the slot right after it is closed.
        Assert.True(string.CompareOrdinal(Rank.Between("", "na"), "na") < 0);
        Assert.True(string.CompareOrdinal("na", Rank.Between("na", "")) < 0);
    }

    [Theory]
    // Closed: `after` is `before` plus one or more 'a's, so nothing fits.
    [InlineData("", "a", true)]
    [InlineData("", "aa", true)]
    [InlineData("a", "aa", true)]
    [InlineData("n", "na", true)]
    [InlineData("n", "naa", true)]
    // Open: there is room, however deep.
    [InlineData("", "na", false)]
    [InlineData("", "b", false)]
    [InlineData("a", "ab", false)]
    [InlineData("n", "nab", false)]
    [InlineData("n", "t", false)]
    [InlineData("zt", "", false)]
    public void IsGapClosed_IsOnlyTheTrailingMinRun(string lo, string hi, bool closed)
    {
        Assert.Equal(closed, Rank.IsGapClosed(lo, hi));
        if (closed) Assert.Throws<InvalidOperationException>(() => Rank.Between(lo, hi));
        else Assert.NotNull(Rank.Between(lo, hi));
    }

    [Fact]
    public void Rebalance_SpreadPreservesAnExistingOrder()
    {
        // The one operation that rewrites existing ranks assigns them in the
        // order the group is already in, so display order is unchanged.
        string[] legacy = ["a", "c", "g", "n", "zt"];
        var respaced = Rank.Spread(legacy.Length);
        Assert.Equal(respaced.OrderBy(r => r, StringComparer.Ordinal), respaced);
        Assert.Equal(legacy.Length, respaced.Length);
    }

    [Fact]
    public void Spread_StaysShortForLargeGroups()
    {
        Assert.All(Rank.Spread(500), r => Assert.True(r.Length <= 2, $"too long: {r}"));
    }
}
