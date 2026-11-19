using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class PriorityTests
{
    private static readonly ItemFile File = new(new SimpleFrontmatterSerializer());

    [Theory]
    [InlineData("someday", Priority.Someday)]
    [InlineData("normal", Priority.Normal)]
    [InlineData("important", Priority.Important)]
    [InlineData("critical", Priority.Critical)]
    [InlineData("CRITICAL", Priority.Critical)]
    [InlineData("  important  ", Priority.Important)]
    [InlineData("", Priority.Normal)]
    public void TryParse_accepts_levels_and_resets_on_empty(string raw, Priority expected)
    {
        Assert.True(Priorities.TryParse(raw, out var p));
        Assert.Equal(expected, p);
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("high")]
    [InlineData("1")]
    public void TryParse_rejects_unknown(string raw)
    {
        Assert.False(Priorities.TryParse(raw, out var p));
        Assert.Equal(Priority.Normal, p);
    }

    [Fact]
    public void ParseLenient_degrades_unknown_to_normal()
    {
        Assert.Equal(Priority.Normal, Priorities.ParseLenient("bogus"));
        Assert.Equal(Priority.Normal, Priorities.ParseLenient(null));
        Assert.Equal(Priority.Critical, Priorities.ParseLenient("critical"));
    }

    [Fact]
    public void Ordinal_order_is_someday_lt_normal_lt_important_lt_critical()
    {
        Assert.True((int)Priority.Someday < (int)Priority.Normal);
        Assert.True((int)Priority.Normal < (int)Priority.Important);
        Assert.True((int)Priority.Important < (int)Priority.Critical);
    }

    [Fact]
    public void Read_absent_priority_defaults_to_normal()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: unset
            type: task
            state: todo
            ---
            body
            """;
        Assert.Equal(Priority.Normal, File.Read(content).Priority);
    }

    [Fact]
    public void Read_malformed_priority_degrades_to_normal_without_throwing()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: bad prio
            type: task
            state: todo
            priority: urgent
            ---
            body
            """;
        var item = File.Read(content);
        Assert.Equal(Priority.Normal, item.Priority);
        Assert.Equal("bad prio", item.Title);
    }

    [Fact]
    public void Write_omits_priority_when_normal()
    {
        var item = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "normal",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Priority = Priority.Normal,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        Assert.DoesNotContain("priority:", File.Write(item));
    }

    [Fact]
    public void RoundTrip_preserves_non_normal_priority()
    {
        var original = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "important",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Priority = Priority.Important,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        var written = File.Write(original);
        Assert.Contains("priority: important", written);
        Assert.Equal(Priority.Important, File.Read(written).Priority);
    }
}
