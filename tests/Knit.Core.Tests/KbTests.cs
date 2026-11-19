using Knit.Core.Kb;
using Knit.Core.Refs;

namespace Knit.Core.Tests;

public sealed class KbTestsFixture : IDisposable
{
    public string Root { get; }

    public KbTestsFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"knit-kb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(Root, ".knit"));
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

public class KbLayoutTests : IClassFixture<KbTestsFixture>
{
    private readonly KbTestsFixture _f;
    public KbLayoutTests(KbTestsFixture f) => _f = f;

    [Fact]
    public void Paths_compose_under_kb_folder()
    {
        var kb = KbLayout.KbDir(_f.Root);
        Assert.EndsWith(Path.Combine(".knit", "kb"), kb);
        var bucket = KbLayout.BucketDir(_f.Root, Bucket.Adr);
        Assert.EndsWith(Path.Combine(".knit", "kb", "adrs"), bucket);
        var master = KbLayout.MasterIndexPath(_f.Root);
        Assert.EndsWith("kb-index.md", master);
    }

    [Fact]
    public void EnsureBucketDir_is_idempotent()
    {
        var first = KbLayout.EnsureBucketDir(_f.Root, Bucket.Doc);
        Assert.True(Directory.Exists(first));
        var second = KbLayout.EnsureBucketDir(_f.Root, Bucket.Doc);
        Assert.Equal(first, second);
    }
}

public class KbFrontmatterTests
{
    [Fact]
    public void Adr_roundtrip()
    {
        var f = new AdrFrontmatter(
            Id: "0007",
            Title: "Use UTC timestamps",
            Date: new DateOnly(2026, 5, 11),
            Status: "accepted",
            Supersedes: [],
            SupersededBy: null,
            Feature: ["storage"],
            Origin: ["design:persistence", "item:01960000-0000-7000-8000-0000000000aa"],
            Tags: ["persistence"],
            Summary: "summary line");
        var m = KbFrontmatterMap.ToMap(f);
        var back = KbFrontmatterMap.ReadAdr(m);
        Assert.Equal(f.Id, back.Id);
        Assert.Equal(f.Title, back.Title);
        Assert.Equal(f.Date, back.Date);
        Assert.Equal(f.Status, back.Status);
        Assert.Equal(f.Summary, back.Summary);
        Assert.Equal(f.Feature, back.Feature);
        Assert.Equal(f.Origin, back.Origin);
    }

    [Fact]
    public void Doc_missing_optional_handled()
    {
        var m = new Dictionary<string, object?> { ["title"] = "x" };
        var f = KbFrontmatterMap.ReadDoc(m);
        Assert.Equal("x", f.Title);
        Assert.Null(f.Feature);
        Assert.Empty(f.Tags);
        Assert.Null(f.Summary);
    }

    [Fact]
    public void Log_roundtrip_with_date()
    {
        var f = new LogFrontmatter("retro", new DateOnly(2026, 4, 15), ["storage"], ["retrospective"], "s");
        var back = KbFrontmatterMap.ReadLog(KbFrontmatterMap.ToMap(f));
        Assert.Equal(f.Date, back.Date);
    }

    [Fact]
    public void Source_roundtrip()
    {
        var f = new SourceFrontmatter("rfc-7234", "HTTP Caching", "link",
            "https://example/rfc", null, ["http"], "caching");
        var back = KbFrontmatterMap.ReadSource(KbFrontmatterMap.ToMap(f));
        Assert.Equal(f.Url, back.Url);
        Assert.Equal(f.SourceKind, back.SourceKind);
    }
}

public class KbTemplateTests
{
    [Theory]
    [InlineData(Bucket.Adr, "## Context")]
    [InlineData(Bucket.Adr, "## Decision")]
    [InlineData(Bucket.Adr, "## Consequences")]
    [InlineData(Bucket.Feature, "## Overview")]
    [InlineData(Bucket.Feature, "## Decisions")]
    [InlineData(Bucket.Feature, "## Future ideas")]
    [InlineData(Bucket.Log, "## What happened")]
    [InlineData(Bucket.Doc, "## Overview")]
    public void Template_loads_and_contains_expected_section(Bucket bucket, string header)
    {
        var content = KbTemplates.Load(bucket);
        Assert.Contains(header, content);
    }

    [Theory]
    [InlineData(Bucket.Adr)]
    [InlineData(Bucket.Doc)]
    [InlineData(Bucket.Feature)]
    [InlineData(Bucket.Log)]
    [InlineData(Bucket.Idea)]
    [InlineData(Bucket.Source)]
    public void All_kb_bucket_templates_load(Bucket bucket)
    {
        var content = KbTemplates.Load(bucket);
        Assert.False(string.IsNullOrWhiteSpace(content));
    }
}

public class KbStoreTests : IClassFixture<KbTestsFixture>
{
    private readonly KbTestsFixture _f;
    public KbStoreTests(KbTestsFixture f) => _f = f;

    [Fact]
    public void Write_read_roundtrip_adr()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kbstore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".knit"));
            var store = new KbStore(root);
            var fm = KbFrontmatterMap.ToMap(new AdrFrontmatter(
                "0001", "Pick storage", new DateOnly(2026, 5, 1), "accepted",
                [], null, ["storage"], [], [], "use utc"));
            var path = store.Write(new Ref(Bucket.Adr, "0001-pick-storage"), fm, "# 0001. Pick storage\n## Decision\n- utc\n");
            Assert.True(File.Exists(path));
            var read = store.Read(new Ref(Bucket.Adr, "0001-pick-storage"));
            Assert.NotNull(read);
            Assert.Equal("0001", read!.Value.Frontmatter["id"]?.ToString());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Write_by_adr_nnnn_prefix_targets_existing_file_no_fork()
    {
        // Regression: `kb set adr:0002 ...` read the canonical
        // `0002-slug.md` but wrote a duplicate `0002.md` beside it because the
        // writer used the literal short ref. ResolveWritePath must resolve the
        // bare NNNN prefix to the on-disk full slug so the mutation overwrites
        // the real file.
        var root = Path.Combine(Path.GetTempPath(), $"kbstore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".knit"));
            var store = new KbStore(root);
            var full = new Ref(Bucket.Adr, "0002-switch-supplier");
            store.Write(full, KbFrontmatterMap.ToMap(new AdrFrontmatter(
                "0002", "Switch supplier", new DateOnly(2026, 5, 1), "accepted",
                [], null, [], [], [], "s")), "# 0002. Switch supplier\n");

            // Address by bare prefix, as `kb set adr:0002` does.
            var writePath = store.ResolveWritePath(new Ref(Bucket.Adr, "0002"));
            Assert.EndsWith("0002-switch-supplier.md", writePath);

            store.Write(new Ref(Bucket.Adr, "0002"), KbFrontmatterMap.ToMap(new AdrFrontmatter(
                "0002", "Switch supplier", new DateOnly(2026, 5, 1), "accepted",
                [], null, [], [], ["materials"], "s")), "# 0002. Switch supplier\n");

            var bucketDir = KbLayout.BucketDir(root, Bucket.Adr);
            var files = Directory.GetFiles(bucketDir, "*.md");
            Assert.Single(files);
            Assert.EndsWith("0002-switch-supplier.md", files[0]);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ListBucket_enumerates_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kbstore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".knit"));
            var store = new KbStore(root);
            store.Write(new Ref(Bucket.Doc, "architecture"),
                new Dictionary<string, object?> { ["title"] = "arch" }, "# arch\n");
            store.Write(new Ref(Bucket.Doc, "security"),
                new Dictionary<string, object?> { ["title"] = "sec" }, "# sec\n");

            var items = store.ListBucket(Bucket.Doc).ToList();
            Assert.Equal(2, items.Count);
            Assert.Contains(items, x => x.Ref.Slug == "architecture");
            Assert.Contains(items, x => x.Ref.Slug == "security");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
