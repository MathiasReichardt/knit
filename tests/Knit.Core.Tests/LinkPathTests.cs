using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class LinkPathTests
{
    // A real 18-char NanoID (uppercase + digits).
    private const string Id = "ABCDEFGHIJ01234567";

    [Fact]
    public void ItemIdFromTarget_flat_item_returns_id()
    {
        var id = LinkPath.ItemIdFromTarget($"{Id}-bobbins-round-chatty-model.md");
        Assert.Equal(Id, id);
    }

    [Fact]
    public void ItemIdFromTarget_path_into_a_folder_is_no_item()
    {
        Assert.Null(LinkPath.ItemIdFromTarget($"../{Id}-bobbins/index.md"));
    }

    [Fact]
    public void ItemIdFromTarget_slug_with_many_hyphens_still_extracts_id()
    {
        // Regression: the old UUID logic rejoined the first 5 dash-segments,
        // so a slug with >=4 hyphens returned a wrong id. NanoID has no
        // internal hyphens, so the id is everything before the first dash.
        var id = LinkPath.ItemIdFromTarget($"{Id}-a-b-c-d-e-f.md");
        Assert.Equal(Id, id);
    }

    [Fact]
    public void ItemIdFromTarget_is_case_insensitive()
    {
        Assert.Equal("abcdefghij01234567", LinkPath.ItemIdFromTarget("abcdefghij01234567-bobbins.md"));
    }

    // KB references must not be mistaken for item links — their leaf segment
    // ("hair-installation", "0002-...", "robot-spec") would otherwise extract a
    // bogus id and the SPA would render the row as a clickable (broken) item.
    // This guarantee must survive the NanoID switch.
    [Theory]
    [InlineData("../kb/features/hair-installation.md")]
    [InlineData("../kb/adrs/0002-switch-to-fluffcomax-premium-line.md")]
    [InlineData("../kb/sources/robot-spec.md")]
    [InlineData("../kb/docs/security/threat-model.md")]
    public void ItemIdFromTarget_kb_ref_returns_null(string target)
    {
        Assert.Null(LinkPath.ItemIdFromTarget(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../../docs/robot-spec.txt")]
    [InlineData("https://example.com/page")]
    public void ItemIdFromTarget_non_item_returns_null(string target)
    {
        Assert.Null(LinkPath.ItemIdFromTarget(target));
    }

    [Theory]
    [InlineData("../kb/features/hair-installation.md", false)]
    [InlineData("https://example.com/page", false)]
    [InlineData("../../../docs/robot-spec.txt", false)]
    [InlineData("ABCDEFGHIJ01234567-bobbins.md", true)]
    [InlineData("../malformed-id-slug.md", true)]     // looks like an item target, id is malformed
    public void LooksLikeItemTarget_distinguishes_item_shaped_paths(string target, bool expected)
    {
        Assert.Equal(expected, LinkPath.LooksLikeItemTarget(target));
    }
}
