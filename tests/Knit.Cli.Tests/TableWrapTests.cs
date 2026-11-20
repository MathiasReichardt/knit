using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Free-text table cells must never split a word mid-letter. Spectre's default
/// overflow folds, so a title column squeezed below the longest word rendered
/// "Authentication" as "Authentica" + "tion" — a word that isn't there.
/// </summary>
[Collection(nameof(CliCollection))]
public class TableWrapTests
{
    [Fact]
    public void List_ellipsizes_a_word_too_long_for_the_title_column()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var id = JsonDocument.Parse(root.Run("add", "feature", "Authentication and session management", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        // every other column at full width is what squeezes `title` past the
        // longest word — the defect only shows on a fully populated row.
        Assert.Equal(0, root.Run("assign", id, "ci").ExitCode);
        Assert.Equal(0, root.Run("set", id, "due=2026-08-15").ExitCode);
        Assert.Equal(0, root.Run("set", id, "priority=critical").ExitCode);

        // A standard 80-column terminal is narrow enough to squeeze `title`.
        var width = Knit.Cli.Output.Out.WidthOverride;
        Knit.Cli.Output.Out.WidthOverride = 80;
        CliResult r;
        try { r = root.Run("list", "--no-color"); }
        finally { Knit.Cli.Output.Out.WidthOverride = width; }
        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("Authentica\n", r.Stdout);
        Assert.DoesNotContain("Authentica ", r.Stdout);
        Assert.Contains("Authentic…", r.Stdout);
    }
}
