using Knit.Core.Domain;

namespace Knit.Core.Tests;

public class SiblingOrderTests
{
    private static Item Make(ItemState state, string rank) => new()
    {
        Id = $"{state}-{rank}",
        Title = $"{state} {rank}",
        Type = ItemType.Task,
        State = state,
        Rank = rank,
        Created = DateTimeOffset.UnixEpoch,
        Body = "",
    };

    // Ranks run against the target order so only the state can produce it.
    private static readonly Item[] Shuffled =
    [
        Make(ItemState.Done, "f"),
        Make(ItemState.Dismissed, "a"),
        Make(ItemState.Draft, "b"),
        Make(ItemState.Todo, "c"),
        Make(ItemState.InProgress, "d"),
        Make(ItemState.Review, "e"),
    ];

    [Fact]
    public void State_sort_puts_done_first_and_dismissed_last()
    {
        var states = Shuffled.OrderBy(i => i, SiblingOrder.Comparer(TreeSort.State)).Select(i => i.State);
        Assert.Equal(
            [ItemState.Done, ItemState.Review, ItemState.InProgress, ItemState.Todo, ItemState.Draft, ItemState.Dismissed],
            states);
    }

    [Fact]
    public void Descending_is_the_lifecycle_with_dismissed_still_last()
    {
        var states = Shuffled
            .OrderBy(i => i, Comparer<Item>.Create((a, b) => SiblingOrder.CompareState(a, b, descending: true)))
            .Select(i => i.State);
        Assert.Equal(
            [ItemState.Draft, ItemState.Todo, ItemState.InProgress, ItemState.Review, ItemState.Done, ItemState.Dismissed],
            states);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rank_ascends_within_a_band_in_both_directions(bool descending)
    {
        Item[] todos = [Make(ItemState.Todo, "m"), Make(ItemState.Todo, "c"), Make(ItemState.Todo, "x")];
        var ranks = todos
            .OrderBy(i => i, Comparer<Item>.Create((a, b) => SiblingOrder.CompareState(a, b, descending)))
            .Select(i => i.Rank);
        Assert.Equal(["c", "m", "x"], ranks);
    }

    [Fact]
    public void Rank_sort_ignores_state()
    {
        var ranks = Shuffled.OrderBy(i => i, SiblingOrder.Comparer(TreeSort.Rank)).Select(i => i.Rank);
        Assert.Equal(["a", "b", "c", "d", "e", "f"], ranks);
    }

    [Theory]
    [InlineData("rank", TreeSort.Rank)]
    [InlineData(" State ", TreeSort.State)]
    public void Parses_the_wire_names(string raw, TreeSort expected)
    {
        Assert.True(SiblingOrder.TryParse(raw, out var sort));
        Assert.Equal(expected, sort);
    }

    [Fact]
    public void Rejects_an_unknown_sort() => Assert.False(SiblingOrder.TryParse("priority", out _));
}
