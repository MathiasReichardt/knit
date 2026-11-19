namespace Knit.Cli.Tests;

/// <summary>
/// A template is a checklist, not a form: headings that don't
/// apply must be removable, or the item reads as unfinished work forever.
/// <c>--set-section --value ""</c> stays the other operation (empty it, keep
/// the heading), so both are covered here side by side.
/// </summary>
[Collection(nameof(CliCollection))]
public class DeleteSectionTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static string Seed(TempKnitRoot root)
    {
        Assert.Equal(0, root.Run("add", "bug", "Login loops").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Repro", "--value", "- step one").ExitCode);
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Notes", "--value", "tail").ExitCode);
        return id;
    }

    [Fact]
    public void Delete_section_removes_heading_and_content()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        var r = root.Run("edit", id, "--delete-section", "Repro");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);

        var body = File.ReadAllText(ItemFixtures.FirstItemFile(root));
        Assert.DoesNotContain("## Repro", body);
        Assert.DoesNotContain("step one", body);
        Assert.Contains("## Notes", body);
    }

    [Fact]
    public void Set_section_with_an_empty_value_keeps_the_heading()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        Assert.Equal(0, root.Run("edit", id, "--set-section", "Repro", "--value", "").ExitCode);

        var body = File.ReadAllText(ItemFixtures.FirstItemFile(root));
        Assert.Contains("## Repro", body);
        Assert.DoesNotContain("step one", body);
    }

    [Fact]
    public void Delete_section_is_repeatable_in_one_call()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        Assert.Equal(0, root.Run("edit", id, "--delete-section", "Repro", "--delete-section", "Notes").ExitCode);

        var body = File.ReadAllText(ItemFixtures.FirstItemFile(root));
        Assert.DoesNotContain("## Repro", body);
        Assert.DoesNotContain("## Notes", body);
    }

    [Fact]
    public void Unknown_section_errors_and_names_the_real_ones()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        var r = root.Run("edit", id, "--delete-section", "Reproduction");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("Reproduction", r.Stderr);
        Assert.Contains("Repro", r.Stderr);
        Assert.Contains("Notes", r.Stderr);
    }

    [Fact]
    public void A_typo_in_the_second_name_leaves_the_first_section_alone()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        Assert.NotEqual(0, root.Run("edit", id, "--delete-section", "Repro", "--delete-section", "Nope").ExitCode);

        // Nothing was written: a partially applied cleanup is worse than none.
        Assert.Contains("## Repro", File.ReadAllText(ItemFixtures.FirstItemFile(root)));
    }

    [Theory]
    [InlineData("Links")]
    [InlineData("Comments")]
    public void Structural_sections_are_refused(string heading)
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        var r = root.Run("edit", id, "--delete-section", heading);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("structural", r.Stderr);
    }

    [Fact]
    public void Delete_section_json_returns_the_item()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);

        var r = root.Run("edit", id, "--delete-section", "Repro", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"id\"", r.Stdout);
        Assert.Contains(id, r.Stdout);
    }

    [Fact]
    public void Show_surfaces_an_empty_section_so_it_can_be_cleaned_up()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Seed(root);
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Repro", "--value", "").ExitCode);

        var shown = root.Run("show", id);
        Assert.Equal(0, shown.ExitCode);
        Assert.Contains("Repro", shown.Stdout);
        Assert.Contains("(empty)", shown.Stdout);
    }
}
