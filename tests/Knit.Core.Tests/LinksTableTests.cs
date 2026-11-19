using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class LinksTableTests
{
    [Fact]
    public void Empty_body_yields_no_rows()
    {
        Assert.Empty(LinksTable.Parse(""));
        Assert.Empty(LinksTable.Parse("# Title\n\nDescription only."));
    }

    [Fact]
    public void Parses_canonical_table()
    {
        var body =
            "# Title\n\n" +
            "## Links\n\n" +
            "| Rel        | Target                                   | Notes                         |\n" +
            "|------------|------------------------------------------|-------------------------------|\n" +
            "| Parent     | [Umbrella](../01968a70-foo.md)     | umbrella feature              |\n" +
            "| Blocks     | [OAuth](../01968a71-bar.md)        |                               |\n" +
            "| KB ref     | [doc:security](../../kb/docs/security.md)| auth-flow assumptions         |\n";
        var rows = LinksTable.Parse(body);
        Assert.Equal(3, rows.Count);
        Assert.Equal(LinkRel.Parent, rows[0].Rel);
        Assert.Equal("Umbrella", rows[0].TargetText);
        Assert.Equal("../01968a70-foo.md", rows[0].TargetPath);
        Assert.Equal("umbrella feature", rows[0].Notes);
        Assert.Null(rows[1].Notes);
        Assert.Equal(LinkRel.KbRef, rows[2].Rel);
    }

    [Fact]
    public void Vocabulary_is_case_insensitive_on_parse()
    {
        var body =
            "## Links\n\n" +
            "| Rel | Target | Notes |\n" +
            "|-----|--------|-------|\n" +
            "| BLOCKED BY | [x](a.md) | |\n" +
            "| related    | [y](b.md) | |\n" +
            "| kb-ref     | [z](c.md) | |\n";
        var rows = LinksTable.Parse(body);
        Assert.Equal(3, rows.Count);
        Assert.Equal(LinkRel.BlockedBy, rows[0].Rel);
        Assert.Equal(LinkRel.Related, rows[1].Rel);
        Assert.Equal(LinkRel.KbRef, rows[2].Rel);
    }

    [Fact]
    public void Render_round_trips()
    {
        var rows = new List<LinkRow>
        {
            new(LinkRel.Parent, "Umbrella", "../01968a70-foo.md", "umbrella feature"),
            new(LinkRel.Blocks, "OAuth", "../01968a71-bar.md", null),
            new(LinkRel.KbRef, "doc:security", "../../kb/docs/security.md", "auth assumptions"),
        };
        var rendered = LinksTable.Render(rows);
        var parsed = LinksTable.Parse(rendered);
        Assert.Equal(3, parsed.Count);
        Assert.Equal(rows[0].TargetPath, parsed[0].TargetPath);
        Assert.Equal(rows[1].Notes, parsed[1].Notes);
        Assert.Equal(rows[2].Rel, parsed[2].Rel);
    }

    [Fact]
    public void A_note_with_pipes_and_line_breaks_round_trips_on_one_row()
    {
        var rows = new[] { new LinkRow(LinkRel.BlockedBy, "Api", "../x.md", "needs v2 | not v1\nasap") };
        var parsed = LinksTable.Parse(LinksTable.Render(rows));
        Assert.Equal("needs v2 | not v1 asap", Assert.Single(parsed).Notes);
    }

    [Fact]
    public void Render_returns_empty_when_no_rows()
    {
        Assert.Equal(string.Empty, LinksTable.Render([]));
    }

    [Fact]
    public void Padding_variants_parse_identically()
    {
        var tight =
            "## Links\n\n" +
            "|Rel|Target|Notes|\n" +
            "|-|-|-|\n" +
            "|Related|[x](a.md)||\n";
        var wide =
            "## Links\n\n" +
            "| Rel       | Target          | Notes |\n" +
            "|-----------|-----------------|-------|\n" +
            "| Related   | [x](a.md)       |       |\n";
        var a = LinksTable.Parse(tight);
        var b = LinksTable.Parse(wide);
        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal(a[0].Rel, b[0].Rel);
        Assert.Equal(a[0].TargetPath, b[0].TargetPath);
    }

    [Fact]
    public void Multiple_parent_rows_collapsed_on_write()
    {
        var rendered = LinksTable.Render(new List<LinkRow>
        {
            new(LinkRel.Parent, "A", "a.md", null),
            new(LinkRel.Parent, "B", "b.md", null),
        });
        var parsed = LinksTable.Parse(rendered);
        Assert.Single(parsed);
        Assert.Equal("A", parsed[0].TargetText);
    }

    [Fact]
    public void Multiple_parent_rows_warn_on_parse()
    {
        var body =
            "## Links\n\n" +
            "| Rel    | Target    | Notes |\n" +
            "|--------|-----------|-------|\n" +
            "| Parent | [a](a.md) |       |\n" +
            "| Parent | [b](b.md) |       |\n";
        var warnings = new List<string>();
        var rows = LinksTable.Parse(body, warnings);
        Assert.Single(rows);
        Assert.Contains(warnings, w => w.Contains("multiple Parent"));
    }

    [Fact]
    public void Unknown_rel_skipped_with_warning()
    {
        var body =
            "## Links\n\n" +
            "| Rel        | Target    | Notes |\n" +
            "|------------|-----------|-------|\n" +
            "| Garbage    | [a](a.md) |       |\n" +
            "| Related    | [b](b.md) |       |\n";
        var warnings = new List<string>();
        var rows = LinksTable.Parse(body, warnings);
        Assert.Single(rows);
        Assert.Equal(LinkRel.Related, rows[0].Rel);
        Assert.Contains(warnings, w => w.Contains("unknown rel"));
    }

    [Fact]
    public void Stops_at_next_h2_heading()
    {
        var body =
            "## Links\n\n" +
            "| Rel     | Target    | Notes |\n" +
            "|---------|-----------|-------|\n" +
            "| Related | [a](a.md) |       |\n\n" +
            "## Notes\n\n" +
            "| not | a | link table |\n";
        var rows = LinksTable.Parse(body);
        Assert.Single(rows);
    }
}
