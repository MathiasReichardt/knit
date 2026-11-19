using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Data;

namespace Knit.Cli.Tests;

/// <summary>
/// <see cref="RankRebalancer"/>: which sibling groups are
/// blocked, and what respacing them writes. A group is blocked when its first
/// row's rank ends in <c>'a'</c>, which only ranks written by older versions do.
/// </summary>
public class RankRebalancerTests
{
    [Fact]
    public void Healthy_repo_has_no_blocked_groups()
    {
        var (root, store) = CreateRepo();
        try
        {
            foreach (var rank in Rank.Spread(5)) Write(store, rank, parentId: null);
            Assert.Empty(RankRebalancer.BlockedGroups(store));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void A_terminal_a_rank_blocks_only_its_own_group()
    {
        var (root, store) = CreateRepo();
        try
        {
            var parentId = Write(store, "n", parentId: null);
            Write(store, "a", parentId);
            Write(store, "g", parentId);
            Write(store, "t", parentId: null);

            var blocked = RankRebalancer.BlockedGroups(store);

            Assert.Single(blocked);
            Assert.Equal(parentId, blocked[0].ParentId);
            Assert.Equal(2, blocked[0].Children.Count);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void A_gap_between_two_siblings_blocks_the_group_too()
    {
        var (root, store) = CreateRepo();
        try
        {
            // Nothing fits between "n" and "na" — the slot is unreachable even
            // though the top of the group is fine.
            Write(store, "n", parentId: null);
            Write(store, "na", parentId: null);

            var group = RankRebalancer.GroupOf(store, null)!;

            Assert.True(group.Blocked);
            Assert.Equal(("n", "na"), group.ClosedGap);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void A_rank_ending_in_a_is_not_blocked_on_its_own()
    {
        var (root, store) = CreateRepo();
        try
        {
            // "na" ends in 'a' but "g" still sorts before it — only an
            // all-'a' run, or a neighbour it extends, closes a slot.
            Write(store, "na", parentId: null);
            Write(store, "t", parentId: null);

            Assert.Empty(RankRebalancer.BlockedGroups(store));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Rebalance_respaces_in_place_and_unblocks()
    {
        var (root, store) = CreateRepo();
        try
        {
            var first = Write(store, "a", parentId: null);
            var second = Write(store, "g", parentId: null);
            var third = Write(store, "n", parentId: null);

            var group = RankRebalancer.GroupOf(store, null)!;
            Assert.True(group.Blocked);
            Assert.Equal((null, "a"), group.ClosedGap);
            Assert.Equal(3, RankRebalancer.Rebalance(store, group));

            // Order is preserved, and the top is reachable again.
            var ranks = new[] { first, second, third }.Select(id => store.Read(id)!.Rank!).ToList();
            Assert.Equal(ranks.OrderBy(r => r, StringComparer.Ordinal), ranks);
            Assert.False(Rank.IsGapClosed("", ranks[0]));
            Assert.Empty(RankRebalancer.BlockedGroups(store));
            Assert.NotNull(Rank.Between("", ranks[0]));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Rebalance_writes_only_what_changes()
    {
        var (root, store) = CreateRepo();
        try
        {
            // Already the exact spread: nothing to write.
            foreach (var rank in Rank.Spread(4)) Write(store, rank, parentId: null);
            Assert.Equal(0, RankRebalancer.Rebalance(store, RankRebalancer.GroupOf(store, null)!));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Rankless_items_sort_last_and_get_backfilled()
    {
        var (root, store) = CreateRepo();
        try
        {
            var ranked = Write(store, "n", parentId: null);
            var rankless = Write(store, null, parentId: null);

            var group = RankRebalancer.GroupOf(store, null)!;
            Assert.Equal([ranked, rankless], group.Children.Select(c => c.Id));

            RankRebalancer.Rebalance(store, group);
            Assert.True(Rank.IsValid(store.Read(rankless)!.Rank));
        }
        finally { TryDelete(root); }
    }

    private static (string Root, ItemStore Store) CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-rank-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "ci", Display = "CI" });
        return (root, new ItemStore(root, new SimpleFrontmatterSerializer()));
    }

    private static string Write(ItemStore store, string? rank, string? parentId)
    {
        var id = store.NewItemId();
        LinkRow[] links = parentId is null
            ? []
            : [new LinkRow(LinkRel.Parent, "parent",
                LinkPath.Relative(store.ItemsDir, store.FindItemFile(parentId)!), null)];
        store.Write(new Item
        {
            Id = id, Title = rank ?? "no rank", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# x\n", Rank = rank, LinkRows = links,
        });
        return id;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
