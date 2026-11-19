using Knit.Core.Refs;

namespace Knit.Core.Tests;

public class RefResolverTests : IDisposable
{
    private readonly string _root;

    public RefResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"knit-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "kb", "adrs"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "kb", "docs", "security"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "kb", "features", "user-mgmt"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "kb", "logs"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "items"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "designs", "completed", "old-cycle"));

        File.WriteAllText(Path.Combine(_root, ".knit", "designs", "web-ui-design.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "designs", "completed", "old-cycle", "m1a-design.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "adrs", "0007-utc-timestamps.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "docs", "architecture.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "docs", "security", "threat-model.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "features", "user-mgmt", "readme.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "features", "storage.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "kb", "logs", "2026-04-15-retro.md"), "x");
        File.WriteAllText(Path.Combine(_root, ".knit", "items", "abc-feature-x.md"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Resolve_file_form_feature()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Feature, "storage"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.Contains("storage.md", resolved.Path);
    }

    [Fact]
    public void Resolve_folder_form_feature_readme()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Feature, "user-mgmt"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.Contains("readme.md", resolved.Path);
    }

    [Fact]
    public void Resolve_nested_doc_slug()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Doc, "security/threat-model"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.EndsWith("threat-model.md", resolved.Path);
    }

    [Fact]
    public void Resolve_adr_by_nnnn_prefix()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Adr, "0007"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.Contains("0007-utc-timestamps.md", resolved.Path);
    }

    [Fact]
    public void Resolve_missing_returns_unresolved()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Feature, "ghost"), _root).GetValueOrThrow();
        Assert.False(resolved.Exists);
    }

    [Fact]
    public void Resolve_item_finds_existing_file()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Item, "abc"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.EndsWith("abc-feature-x.md", resolved.Path);
    }

    [Fact]
    public void Resolve_active_design()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Design, "web-ui"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.EndsWith("web-ui-design.md", resolved.Path);
    }

    [Fact]
    public void Resolve_archived_design_via_completed_fallback()
    {
        // Slug = the completed/<slug>/ folder name; the cycle filename stem may differ.
        var resolved = RefResolver.Resolve(new Ref(Bucket.Design, "old-cycle"), _root).GetValueOrThrow();
        Assert.True(resolved.Exists);
        Assert.EndsWith("m1a-design.md", resolved.Path);
    }

    [Fact]
    public void Resolve_missing_design_returns_unresolved()
    {
        var resolved = RefResolver.Resolve(new Ref(Bucket.Design, "ghost"), _root).GetValueOrThrow();
        Assert.False(resolved.Exists);
        Assert.EndsWith("ghost-design.md", resolved.Path);
    }
}
