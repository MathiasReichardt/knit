using Knit.Core.Designs;

namespace Knit.Core.Tests;

public class DesignsTests : IDisposable
{
    private readonly string _root;
    private Knit.Data.ProseRefRewriter Prose => new(new Knit.Data.ItemStore(_root), _root);

    public DesignsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"knit-designs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "designs"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Active_designs_returns_top_level_design_files()
    {
        var dir = DesignsLayout.DesignsDir(_root);
        File.WriteAllText(Path.Combine(dir, "user-mgmt-design.md"), "x");
        File.WriteAllText(Path.Combine(dir, "user-mgmt-plan.md"), "p");
        File.WriteAllText(Path.Combine(dir, "storage-design.md"), "x");

        var docs = DesignsLayout.ActiveDesignDocs(_root);
        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.EndsWith("-design.md", d));
    }

    [Fact]
    public void Missing_designs_folder_returns_empty()
    {
        var bare = Path.Combine(Path.GetTempPath(), $"bare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(bare, ".knit"));
        try
        {
            Assert.Empty(DesignsLayout.ActiveDesignDocs(bare));
            Assert.Empty(DesignsLayout.CompletedDesignDocs(bare));
        }
        finally
        {
            try { Directory.Delete(bare, true); } catch { }
        }
    }

    [Fact]
    public void Completed_designs_enumerates_under_subfolder()
    {
        var completed = Path.Combine(DesignsLayout.CompletedDir(_root), "user-mgmt");
        Directory.CreateDirectory(completed);
        File.WriteAllText(Path.Combine(completed, "1-design.md"), "x");
        File.WriteAllText(Path.Combine(completed, "2-design.md"), "x");

        var docs = DesignsLayout.CompletedDesignDocs(_root, "user-mgmt");
        Assert.Equal(2, docs.Count);
    }

    [Fact]
    public void Archive_moves_active_doc_into_completed()
    {
        var source = DesignsLayout.DesignDocPath(_root, "user-mgmt");
        File.WriteAllText(source, "design content");
        var result = DesignArchive.MoveToCompleted(_root, "user-mgmt", "1", Prose);
        Assert.False(result.IsError);
        var dest = result.GetValueOrThrow().Path;
        Assert.EndsWith(Path.Combine("user-mgmt", "1-design.md"), dest);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void Archive_errors_when_source_missing()
    {
        var result = DesignArchive.MoveToCompleted(_root, "ghost", "1", Prose);
        Assert.True(result.IsError);
    }
}
