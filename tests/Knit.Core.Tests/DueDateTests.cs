using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class DueDateTests
{
    private static readonly ItemFile File = new(new SimpleFrontmatterSerializer());

    [Fact]
    public void Read_parses_iso_due_date()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: dated
            type: task
            state: todo
            due: 2026-07-15
            created: 2026-05-01T10:00:00Z
            ---
            body
            """;
        var item = File.Read(content);
        Assert.Equal(new DateOnly(2026, 7, 15), item.Due);
    }

    [Fact]
    public void Read_malformed_due_degrades_to_null_without_throwing()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: bad date
            type: task
            state: todo
            due: not-a-date
            ---
            body
            """;
        var item = File.Read(content);
        Assert.Null(item.Due);
        Assert.Equal("bad date", item.Title);
    }

    [Fact]
    public void Read_absent_due_is_null()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: undated
            type: task
            state: todo
            ---
            body
            """;
        Assert.Null(File.Read(content).Due);
    }

    [Fact]
    public void RoundTrip_preserves_due()
    {
        var original = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "dated",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Due = new DateOnly(2026, 12, 31),
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        var roundTripped = File.Read(File.Write(original));
        Assert.Equal(original.Due, roundTripped.Due);
    }

    [Fact]
    public void Write_omits_due_when_null()
    {
        var item = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "undated",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        Assert.DoesNotContain("due:", File.Write(item));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026/07/15")]
    [InlineData("15-07-2026")]
    [InlineData("")]
    public void ParseLenient_rejects_non_iso(string raw)
    {
        Assert.Null(DueDates.ParseLenient(raw));
    }

    [Fact]
    public void Status_classifies_relative_to_today()
    {
        var today = new DateOnly(2026, 7, 8);
        Assert.Equal(DueStatus.Overdue, DueDates.Status(new DateOnly(2026, 7, 1), ItemState.Todo, today, 3));
        Assert.Equal(DueStatus.DueSoon, DueDates.Status(new DateOnly(2026, 7, 10), ItemState.Todo, today, 3));
        Assert.Equal(DueStatus.DueSoon, DueDates.Status(today, ItemState.Todo, today, 3));
        Assert.Equal(DueStatus.Future, DueDates.Status(new DateOnly(2026, 8, 1), ItemState.Todo, today, 3));
        Assert.Equal(DueStatus.None, DueDates.Status(null, ItemState.Todo, today, 3));
    }

    [Fact]
    public void Status_closed_items_are_never_overdue_or_due_soon()
    {
        var today = new DateOnly(2026, 7, 8);
        Assert.Equal(DueStatus.None, DueDates.Status(new DateOnly(2026, 7, 1), ItemState.Done, today, 3));
        Assert.Equal(DueStatus.None, DueDates.Status(new DateOnly(2026, 7, 10), ItemState.Dismissed, today, 3));
    }
}
