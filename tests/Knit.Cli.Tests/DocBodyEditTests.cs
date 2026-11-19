namespace Knit.Cli.Tests;

/// <summary>
/// Items had `edit --set-section` but KB artifacts and design
/// docs had no body-writing verb at all, so an agent had to hand-edit the file.
/// </summary>
[Collection(nameof(CliCollection))]
public class DocBodyEditTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Kb_edit_writes_a_body_section_and_keeps_frontmatter()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "caching", "--title", "Caching", "--summary", "how we cache").ExitCode);

        Assert.Equal(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Decision", "--value", "- write-through").ExitCode);

        var shown = root.Run("kb", "show", "doc:caching");
        Assert.Equal(0, shown.ExitCode);
        Assert.Contains("Decision", shown.Stdout);
        Assert.Contains("write-through", shown.Stdout);
        // Frontmatter survives a body-only write.
        Assert.Contains("how we cache", shown.Stdout);
    }

    [Fact]
    public void Kb_edit_replaces_an_existing_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "caching", "--title", "Caching").ExitCode);

        Assert.Equal(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Notes", "--value", "first").ExitCode);
        Assert.Equal(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Notes", "--value", "second").ExitCode);

        var shown = root.Run("kb", "show", "doc:caching");
        Assert.Contains("second", shown.Stdout);
        Assert.DoesNotContain("first", shown.Stdout);
    }

    [Fact]
    public void Kb_edit_rejects_structural_sections_and_unresolved_refs()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "caching", "--title", "Caching").ExitCode);

        Assert.NotEqual(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Links", "--value", "x").ExitCode);
        Assert.NotEqual(0, root.Run("kb", "edit", "doc:ghost", "--set-section", "Notes", "--value", "x").ExitCode);
        Assert.NotEqual(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Notes").ExitCode);
    }

    [Fact]
    public void Design_edit_writes_a_body_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "widget", "--title", "Widget").ExitCode);

        Assert.Equal(0, root.Run("design", "edit", "widget", "--set-section", "Goals", "--value", "- ship it").ExitCode);

        var shown = root.Run("design", "show", "widget", "--json");
        Assert.Equal(0, shown.ExitCode);
        Assert.Contains("## Goals", shown.Stdout);
        Assert.Contains("ship it", shown.Stdout);
    }

    [Fact]
    public void Design_edit_reports_a_missing_doc_rather_than_creating_one()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("design", "edit", "ghost", "--set-section", "Goals", "--value", "x");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("ghost", r.Stderr);
        Assert.Equal(0, root.Run("design", "list", "--json").ExitCode);
        Assert.DoesNotContain("ghost", root.Run("design", "list", "--json").Stdout);
    }

    // `--delete-section` reaches the same three artifacts as
    // `--set-section`, so an agent doesn't have to learn which ones are
    // exceptions.
    [Fact]
    public void Kb_edit_deletes_a_body_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "caching", "--title", "Caching").ExitCode);
        Assert.Equal(0, root.Run("kb", "edit", "doc:caching", "--set-section", "Notes", "--value", "scratch").ExitCode);

        Assert.Equal(0, root.Run("kb", "edit", "doc:caching", "--delete-section", "Notes").ExitCode);

        var shown = root.Run("kb", "show", "doc:caching");
        Assert.DoesNotContain("scratch", shown.Stdout);
    }

    [Fact]
    public void Kb_edit_rejects_an_unknown_section_to_delete()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "caching", "--title", "Caching").ExitCode);

        var r = root.Run("kb", "edit", "doc:caching", "--delete-section", "Nope");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("Nope", r.Stderr);
    }

    [Fact]
    public void Design_edit_deletes_a_body_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "widget", "--title", "Widget").ExitCode);
        Assert.Equal(0, root.Run("design", "edit", "widget", "--set-section", "Notes", "--value", "scratch").ExitCode);

        Assert.Equal(0, root.Run("design", "edit", "widget", "--delete-section", "Notes").ExitCode);

        var shown = root.Run("design", "show", "widget");
        Assert.DoesNotContain("scratch", shown.Stdout);
    }
}
