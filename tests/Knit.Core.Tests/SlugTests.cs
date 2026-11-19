using Knit.Core.Ids;

namespace Knit.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Add dark mode", "add-dark-mode")]
    [InlineData("  hello  WORLD  ", "hello-world")]
    [InlineData("über cool feature", "uber-cool-feature")]
    [InlineData("v2.0 release!", "v2-0-release")]
    [InlineData("", "untitled")]
    [InlineData("    ", "untitled")]
    [InlineData("✨emoji-only✨", "emoji-only")]
    [InlineData("---leading and trailing---", "leading-and-trailing")]
    [InlineData("a___b___c", "a-b-c")]
    public void From_ProducesExpectedSlug(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
    }

    [Fact]
    public void From_TruncatesAtMaxLength()
    {
        var input = new string('a', 80);
        var slug = Slug.From(input);
        Assert.True(slug.Length <= 40);
    }

    // Lowercasing and spacing are expected and stay silent; anything
    // that actually costs the caller characters is reported.
    [Theory]
    [InlineData("Add dark mode")]
    [InlineData("  hello  WORLD  ")]
    [InlineData("already-a-slug")]
    public void Describe_is_silent_for_plain_slugification(string input)
    {
        var r = Slug.Describe(input);
        Assert.False(r.Changed);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void Describe_reports_folded_accents()
    {
        var r = Slug.Describe("über cool");
        Assert.Equal("uber-cool", r.Value);
        Assert.True(r.Change.HasFlag(SlugChange.Folded));
        Assert.Contains("folded accents", r.Reason);
    }

    [Fact]
    public void Describe_reports_stripped_punctuation()
    {
        var r = Slug.Describe("v2.0 release!");
        Assert.Equal("v2-0-release", r.Value);
        Assert.True(r.Change.HasFlag(SlugChange.Stripped));
    }

    [Fact]
    public void Describe_reports_truncation_at_the_cap()
    {
        var r = Slug.Describe(new string('a', 60));
        Assert.Equal(40, r.Value.Length);
        Assert.True(r.Change.HasFlag(SlugChange.Truncated));
        Assert.Contains("truncated", r.Reason);
    }

    [Fact]
    public void Describe_reports_the_untitled_fallback()
    {
        var r = Slug.Describe("!!!");
        Assert.Equal("untitled", r.Value);
        Assert.True(r.Change.HasFlag(SlugChange.Fallback));
    }
}
