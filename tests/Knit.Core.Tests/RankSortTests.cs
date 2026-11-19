using Knit.Core.Domain;

namespace Knit.Core.Tests;

public class RankSortTests
{
    private static readonly IComparer<Item> ByState = SiblingOrder.Comparer(TreeSort.State);

    private static Item Make(string id, ItemState state, string? rank) => new()
    {
        Id = id,
        Title = id,
        Type = ItemType.Task,
        State = state,
        Rank = rank,
        Created = DateTimeOffset.UnixEpoch,
        Body = "",
    };

    private static Item[] Apply(IReadOnlyList<Item> group, IReadOnlyList<RankMove> moves)
    {
        var ranks = moves.ToDictionary(m => m.Item.Id, m => m.Rank);
        return group.Select(i => ranks.TryGetValue(i.Id, out var r) ? i with { Rank = r } : i).ToArray();
    }

    private static string[] RankOrder(IEnumerable<Item> items) =>
        items.OrderBy(i => i.Rank, StringComparer.Ordinal).Select(i => i.Id).ToArray();

    private static string[] StateOrder(IEnumerable<Item> items) =>
        items.OrderBy(i => i, ByState).Select(i => i.Id).ToArray();

    [Fact]
    public void A_group_already_in_order_writes_nothing()
    {
        Item[] group = [Make("a", ItemState.Done, "c"), Make("b", ItemState.Todo, "g"), Make("c", ItemState.Todo, "m")];
        Assert.Empty(RankSort.Plan(group, ByState));
    }

    [Fact]
    public void Only_the_items_out_of_order_move()
    {
        // Rank order: t1 t2 done t3 t4 — moving `done` alone fixes it.
        Item[] group =
        [
            Make("t1", ItemState.Todo, "c"),
            Make("t2", ItemState.Todo, "f"),
            Make("done", ItemState.Done, "j"),
            Make("t3", ItemState.Todo, "m"),
            Make("t4", ItemState.Todo, "p"),
        ];
        var moves = RankSort.Plan(group, ByState);

        Assert.Equal(["done"], moves.Select(m => m.Item.Id));
        var after = Apply(group, moves);
        Assert.Equal(StateOrder(group), RankOrder(after));
    }

    [Fact]
    public void Result_rank_order_equals_the_state_view()
    {
        Item[] group =
        [
            Make("dismissed", ItemState.Dismissed, "b"),
            Make("draft", ItemState.Draft, "d"),
            Make("todo", ItemState.Todo, "f"),
            Make("prog", ItemState.InProgress, "h"),
            Make("review", ItemState.Review, "j"),
            Make("done", ItemState.Done, "l"),
        ];
        var moves = RankSort.Plan(group, ByState);

        // Exactly reversed: one item stays, every other one moves.
        Assert.Equal(5, moves.Count);
        Assert.Equal(StateOrder(group), RankOrder(Apply(group, moves)));
    }

    [Fact]
    public void Rankless_items_get_a_rank_in_place()
    {
        Item[] group = [Make("todo", ItemState.Todo, "g"), Make("done", ItemState.Done, null)];
        var moves = RankSort.Plan(group, ByState);

        Assert.Equal(["done"], moves.Select(m => m.Item.Id));
        Assert.Equal(["done", "todo"], RankOrder(Apply(group, moves)));
    }

    [Fact]
    public void A_closed_gap_respaces_the_group()
    {
        // Nothing sorts before "a", so `done` has nowhere to go above `todo`.
        Item[] group = [Make("todo", ItemState.Todo, "a"), Make("done", ItemState.Done, "n")];
        var moves = RankSort.Plan(group, ByState);

        Assert.Equal(["done", "todo"], RankOrder(Apply(group, moves)));
        Assert.DoesNotContain(moves, m => m.Rank.EndsWith('a'));
    }

    [Fact]
    public void Items_sharing_a_rank_still_land_in_state_order()
    {
        Item[] group =
        [
            Make("t1", ItemState.Todo, "g"),
            Make("t2", ItemState.Todo, "g"),
            Make("done", ItemState.Done, "n"),
        ];
        var after = Apply(group, RankSort.Plan(group, ByState));
        Assert.Equal("done", RankOrder(after)[0]);
    }
}
