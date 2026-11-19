using Knit.Core.Frontmatter;
using Xunit;

namespace Knit.Core.Tests;

public class LeadingH1Tests
{
    [Fact]
    public void Extract_returns_leading_h1_text()
    {
        Assert.Equal("Storage layer", LeadingH1.Extract("# Storage layer\n\nBody."));
    }

    [Fact]
    public void Extract_skips_leading_blank_lines()
    {
        Assert.Equal("Title", LeadingH1.Extract("\n\n# Title\n"));
    }

    [Fact]
    public void Extract_is_null_when_body_does_not_start_with_h1()
    {
        Assert.Null(LeadingH1.Extract("## Section\n\ntext"));
        Assert.Null(LeadingH1.Extract("Just prose.\n\n# Later heading"));
    }

    [Fact]
    public void SetOrInsert_replaces_an_existing_leading_h1_in_place()
    {
        var body = "# Old title\n\nBody paragraph.\n\n## Section\n";
        var result = LeadingH1.SetOrInsert(body, "New title");
        Assert.Equal("# New title\n\nBody paragraph.\n\n## Section\n", result);
    }

    [Fact]
    public void SetOrInsert_inserts_an_h1_when_absent()
    {
        var result = LeadingH1.SetOrInsert("## Section\n\ntext", "Title");
        Assert.Equal("# Title\n\n## Section\n\ntext", result);
    }

    [Fact]
    public void SetOrInsert_does_not_touch_a_non_leading_heading()
    {
        // The only `# ` is mid-body — it must be left alone and a new leading
        // H1 inserted at the top.
        var result = LeadingH1.SetOrInsert("Intro line.\n\n# Not the title\n", "Title");
        Assert.Equal("# Title\n\nIntro line.\n\n# Not the title\n", result);
    }
}
