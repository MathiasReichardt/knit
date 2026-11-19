using Knit.Core.Slates;

namespace Knit.Core.Tests;

public class SlateRegistryTests
{
    [Fact]
    public void Render_then_parse_round_trips_names_status_notes_and_order()
    {
        var rows = new List<SlateRow>
        {
            new("2026-w27", SlateStatus.Active, "current focus"),
            new("onboarding", SlateStatus.Planned, null),
            new("tech-debt", SlateStatus.Archived, "shipped 2026-06"),
        };

        var parsed = SlateRegistry.Parse(SlateRegistry.Render(rows));

        Assert.Equal(rows, parsed);
    }

    [Fact]
    public void Parse_of_empty_content_returns_empty_list()
    {
        Assert.Empty(SlateRegistry.Parse(""));
        Assert.Empty(SlateRegistry.Parse("# Slates\n\nno table here\n"));
    }

    [Fact]
    public void Parse_degrades_unknown_status_to_planned_and_keeps_first_duplicate()
    {
        var content = """
            # Slates

            | Slate | Status  | Notes |
            |-------|---------|-------|
            | a     | weird   |       |
            | b     | active  | one   |
            | b     | planned | two   |
            """;

        var warnings = new List<string>();
        var rows = SlateRegistry.Parse(content, warnings);

        Assert.Equal(2, rows.Count);
        Assert.Equal(SlateStatus.Planned, rows[0].Status);
        Assert.Equal(new SlateRow("b", SlateStatus.Active, "one"), rows[1]);
        Assert.Contains(warnings, w => w.Contains("duplicate"));
    }

    [Fact]
    public void Render_of_empty_registry_keeps_heading_and_header_row()
    {
        var text = SlateRegistry.Render([]);

        Assert.StartsWith("# Slates", text);
        Assert.Contains("| Slate", text);
        Assert.Empty(SlateRegistry.Parse(text));
    }
}
