using Knit.Core.Kb;

namespace Knit.Core.Tests;

public class TagsTests
{
    [Fact]
    public void Null_yields_empty()
    {
        Assert.Empty(Tags.Normalize(null));
    }

    [Fact]
    public void Trims_and_drops_empties()
    {
        Assert.Equal(["api", "db"], Tags.Normalize(["  api ", "", "  ", "db"]));
    }

    [Fact]
    public void Dedups_case_insensitively_preserving_first_casing()
    {
        Assert.Equal(["API"], Tags.Normalize(["API", "api", "Api"]));
    }

    [Fact]
    public void Preserves_order_of_first_appearance()
    {
        Assert.Equal(["b", "a", "c"], Tags.Normalize(["b", "a", "B", "c", "A"]));
    }
}
