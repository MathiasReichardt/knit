using Knit.Core.Refs;

namespace Knit.Core.Tests;

public class RefTests
{
    [Theory]
    [InlineData("item:0192f9b7-...", Bucket.Item, "0192f9b7-...")]
    [InlineData("adr:0007", Bucket.Adr, "0007")]
    [InlineData("doc:architecture", Bucket.Doc, "architecture")]
    [InlineData("feature:user-mgmt", Bucket.Feature, "user-mgmt")]
    [InlineData("log:2026-04-15-retro", Bucket.Log, "2026-04-15-retro")]
    [InlineData("idea:global", Bucket.Idea, "global")]
    [InlineData("source:rfc-7234", Bucket.Source, "rfc-7234")]
    [InlineData("design:web-ui", Bucket.Design, "web-ui")]
    public void Parse_each_bucket(string text, Bucket bucket, string slug)
    {
        var r = Ref.Parse(text);
        Assert.False(r.IsError);
        var value = r.GetValueOrThrow();
        Assert.Equal(bucket, value.Bucket);
        Assert.Equal(slug, value.Slug);
    }

    [Theory]
    [InlineData("feature:user-mgmt/auth")]
    [InlineData("doc:security/threat-model")]
    [InlineData("feature:user-mgmt/sub/nested")]
    public void Parse_slug_with_slashes(string text)
    {
        var r = Ref.Parse(text);
        Assert.False(r.IsError);
        Assert.Equal(text, r.GetValueOrThrow().ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("noColon")]
    [InlineData(":missingBucket")]
    [InlineData("adr:")]
    [InlineData("unknown:x")]
    [InlineData("adr:/leading-slash")]
    [InlineData("adr:trailing-slash/")]
    public void Parse_rejects_malformed(string text)
    {
        var r = Ref.Parse(text);
        Assert.True(r.IsError);
    }

    [Theory]
    [InlineData("adr:0007")]
    [InlineData("feature:user-mgmt/auth")]
    [InlineData("doc:architecture")]
    [InlineData("design:web-ui")]
    public void Parse_roundtrips_to_string(string text)
    {
        var r = Ref.Parse(text).GetValueOrThrow();
        Assert.Equal(text, r.ToString());
    }

    [Theory]
    [InlineData(Bucket.Item, "items")]
    [InlineData(Bucket.Adr, "adrs")]
    [InlineData(Bucket.Doc, "docs")]
    [InlineData(Bucket.Feature, "features")]
    [InlineData(Bucket.Log, "logs")]
    [InlineData(Bucket.Idea, "ideas")]
    [InlineData(Bucket.Source, "sources")]
    public void Bucket_maps_to_plural_folder(Bucket b, string folder)
    {
        Assert.Equal(folder, b.Folder());
    }

    [Fact]
    public void BucketFolders_covers_every_bucket()
    {
        foreach (var b in BucketStrings.All)
        {
            var f = b.Folder();
            Assert.False(string.IsNullOrEmpty(f));
        }
    }

    [Theory]
    [InlineData("item:abc-123", "abc-123")]
    [InlineData("ITEM:abc-123", "abc-123")]
    [InlineData("abc-123", "abc-123")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void StripItemPrefix_strips_only_item_prefix(string? input, string? expected)
    {
        Assert.Equal(expected, Ref.StripItemPrefix(input));
    }
}
