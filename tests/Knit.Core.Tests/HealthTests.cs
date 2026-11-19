using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class HealthTests
{
    private static readonly ItemFile File = new(new SimpleFrontmatterSerializer());

    [Theory]
    [InlineData("on-track", Health.OnTrack)]
    [InlineData("at-risk", Health.AtRisk)]
    [InlineData("off-track", Health.OffTrack)]
    [InlineData("OFF-TRACK", Health.OffTrack)]
    [InlineData("  at-risk  ", Health.AtRisk)]
    public void TryParse_accepts_levels(string raw, Health expected)
    {
        Assert.True(Healths.TryParse(raw, out var h));
        Assert.Equal(expected, h);
    }

    [Fact]
    public void TryParse_empty_clears_to_null()
    {
        Assert.True(Healths.TryParse("", out var h));
        Assert.Null(h);
    }

    [Theory]
    [InlineData("green")]
    [InlineData("ok")]
    [InlineData("ontrack")]
    public void TryParse_rejects_unknown(string raw)
    {
        Assert.False(Healths.TryParse(raw, out var h));
        Assert.Null(h);
    }

    [Fact]
    public void ParseLenient_degrades_unknown_to_null()
    {
        Assert.Null(Healths.ParseLenient("bogus"));
        Assert.Null(Healths.ParseLenient(null));
        Assert.Equal(Health.AtRisk, Healths.ParseLenient("at-risk"));
    }

    [Fact]
    public void Read_absent_health_is_null()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: unset
            type: project
            state: todo
            ---
            body
            """;
        Assert.Null(File.Read(content).Health);
    }

    [Fact]
    public void Read_malformed_health_degrades_to_null_without_throwing()
    {
        const string content = """
            ---
            id: AAAAAAAAAA11111111
            title: bad health
            type: project
            state: todo
            health: green
            ---
            body
            """;
        var item = File.Read(content);
        Assert.Null(item.Health);
        Assert.Equal("bad health", item.Title);
    }

    [Fact]
    public void Write_omits_health_when_null()
    {
        var item = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "no signal",
            Type = ItemType.Project,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        Assert.DoesNotContain("health:", File.Write(item));
    }

    [Fact]
    public void RoundTrip_preserves_health()
    {
        var original = new Item
        {
            Id = "AAAAAAAAAA11111111",
            Title = "watched",
            Type = ItemType.Project,
            State = ItemState.Todo,
            Health = Health.OffTrack,
            Created = DateTimeOffset.UnixEpoch,
            Body = "b\n",
        };
        var written = File.Write(original);
        Assert.Contains("health: off-track", written);
        Assert.Equal(Health.OffTrack, File.Read(written).Health);
    }

    [Fact]
    public void RoundTrip_preserves_project_and_todo_types()
    {
        foreach (var type in new[] { ItemType.Project, ItemType.Todo })
        {
            var written = File.Write(new Item
            {
                Id = "AAAAAAAAAA11111111",
                Title = "t",
                Type = type,
                State = ItemState.Todo,
                Created = DateTimeOffset.UnixEpoch,
                Body = "b\n",
            });
            Assert.Contains($"type: {type.ToWire()}", written);
            Assert.Equal(type, File.Read(written).Type);
        }
    }
}
