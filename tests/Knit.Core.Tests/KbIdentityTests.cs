using Knit.Core.Kb;
using Knit.Core.Refs;

namespace Knit.Core.Tests;

/// <summary>
/// Which buckets carry an <c>id:</c>, and what it must be. The
/// rule is "derived from the filename, per bucket" — stated here rather than
/// left to each caller, because the ADR case (id = the number in the slug,
/// not the slug) is the one an implementation gets wrong by accident.
/// </summary>
public class KbIdentityTests
{
    [Theory]
    [InlineData(Bucket.Source, true)]
    [InlineData(Bucket.Adr, true)]
    [InlineData(Bucket.Doc, false)]
    [InlineData(Bucket.Feature, false)]
    [InlineData(Bucket.Log, false)]
    [InlineData(Bucket.Idea, false)]
    public void Only_sources_and_adrs_carry_an_id(Bucket bucket, bool carries) =>
        Assert.Equal(carries, KbIdentity.CarriesId(bucket));

    [Fact]
    public void A_sources_id_is_its_slug() =>
        Assert.Equal("rfc-7231", KbIdentity.ExpectedId(Bucket.Source, "rfc-7231"));

    [Fact]
    public void An_adrs_id_is_the_number_in_its_slug() =>
        Assert.Equal("0007", KbIdentity.ExpectedId(Bucket.Adr, "0007-retry-policy"));

    [Fact]
    public void An_adr_slug_without_a_number_has_no_derivable_id() =>
        Assert.Null(KbIdentity.ExpectedId(Bucket.Adr, "retry-policy"));

    [Fact]
    public void A_path_identified_bucket_has_no_id_to_expect() =>
        Assert.Null(KbIdentity.ExpectedId(Bucket.Doc, "architecture"));

    [Fact]
    public void A_sub_doc_is_identified_by_its_own_last_segment() =>
        Assert.Equal("appendix", KbIdentity.ExpectedId(Bucket.Source, "parent/appendix"));
}
