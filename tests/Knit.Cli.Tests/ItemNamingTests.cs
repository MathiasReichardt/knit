using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Human output names an item as short id + quoted title, the form
/// the skills teach; agents copy the shape they see.
/// </summary>
[Collection(nameof(CliCollection))]
public class ItemNamingTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string title)
    {
        var r = root.Run("add", "task", title, "--json");
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public void A_mutator_confirmation_names_the_item_by_short_id_and_quoted_title()
    {
        using var root = NewRepo();
        var id = Add(root, "Ship the release");

        var r = root.Run("start", id);

        Assert.Equal(0, r.ExitCode);
        Assert.Matches("""^started [0-9a-z]{4,} "Ship the release"\s*$""", r.Stdout);
    }

    [Fact]
    public void An_error_about_an_item_names_it_by_short_id_and_quoted_title()
    {
        using var root = NewRepo();
        var id = Add(root, "Ship the release");

        var r = root.Run("approve", id);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Matches("""[0-9a-z]{4,}\s+"Ship\s+the\s+release"\s+is\s+already\s+todo""", r.Stderr);
    }
}
