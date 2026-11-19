using System.Text.Json;
using Knit.Core.Refs;

namespace Knit.Core.Tests;

public class DocSectionsTests
{
    // The SPA's heading-anchor table: both sides must produce the same anchors,
    // or a section ref the CLI accepts lands nowhere in the web view.
    public static TheoryData<string, string, string[]> SharedTable()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Knit.sln")))
            dir = Path.GetDirectoryName(dir);
        var file = Path.Combine(dir!, "src", "Knit.Web", "src", "lib", "heading-anchors.fixture.json");
        var data = new TheoryData<string, string, string[]>();
        foreach (var c in JsonDocument.Parse(File.ReadAllText(file)).RootElement.EnumerateArray())
            data.Add(
                c.GetProperty("name").GetString()!,
                c.GetProperty("body").GetString()!,
                c.GetProperty("anchors").EnumerateArray().Select(a => a.GetString()!).ToArray());
        return data;
    }

    [Theory]
    [MemberData(nameof(SharedTable))]
    public void Anchors_match_the_spa(string name, string body, string[] anchors)
    {
        _ = name;
        Assert.Equal(anchors, DocSections.Outline(body).Select(h => h.Anchor));
    }

    [Fact]
    public void Section_runs_to_the_next_heading_of_its_level_subsections_included()
    {
        const string body = "---\ntitle: x\n---\n# X\n## Goals\n\none\n\n### Detail\n\ntwo\n\n## Next\n\nthree\n";
        Assert.Equal("## Goals\n\none\n\n### Detail\n\ntwo", DocSections.Section(body, "goals"));
        Assert.Equal("### Detail\n\ntwo", DocSections.Section(body, "detail"));
        Assert.Equal("## Next\n\nthree", DocSections.Section(body, "next"));
        Assert.Null(DocSections.Section(body, "missing"));
    }

    [Fact]
    public void Suffixed_anchors_are_marked()
    {
        var outline = DocSections.Outline("## Goals\n## Goals 2\n## Goals");
        // The SPA's slugger doesn't avoid a literal "Goals 2": both read goals-2,
        // the first one wins in the browser and in the check alike.
        Assert.Equal(["goals", "goals-2", "goals-2"], outline.Select(h => h.Anchor));
        Assert.Equal([false, false, true], outline.Select(h => h.Suffixed));
        Assert.Equal(SectionCheck.SuffixedAnchor, SectionCheck.Check("## Goals\n## Goals", "goals-2"));
        Assert.Null(SectionCheck.Check("## Goals 2", "goals-2"));
        Assert.Equal(SectionCheck.MissingSection, SectionCheck.Check("## Goals", "nope"));
    }

    [Theory]
    [InlineData("design:x#customers", "design:x", "customers", false)]
    [InlineData("item:0192f9b7#goals", "item:0192f9b7", "goals", false)]
    [InlineData(".knit/projects/p/milestones/m1.md#scope", ".knit/projects/p/milestones/m1.md", "scope", false)]
    [InlineData("design:x#mermaid-2/node/a", "design:x", null, true)]
    [InlineData("doc:y", "doc:y", null, false)]
    public void Parses_doc_and_fragment(string text, string doc, string? section, bool pointer)
    {
        Assert.True(DocRef.TryParse(text, out var d));
        Assert.Equal(doc, d.Doc);
        Assert.Equal(section, d.Section);
        Assert.Equal(pointer, d.IsPointer);
        Assert.Equal(text, d.ToString());
    }

    [Theory]
    [InlineData("design:x#")]
    [InlineData(".knit/items/")]
    [InlineData(".knit/../secret.md")]
    [InlineData(".knit/config.json")]
    [InlineData("nope:x#y")]
    public void Rejects_malformed(string text) => Assert.False(DocRef.TryParse(text, out _));
}
