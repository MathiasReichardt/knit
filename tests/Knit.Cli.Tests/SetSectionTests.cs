namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SetSectionTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Set_agent_skills_persists_list()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("set", id, "agent_skills=alpha,beta,gamma");
        Assert.Equal(0, r.ExitCode);

        var ctx = root.Run("context", id, "--json");
        Assert.Equal(0, ctx.ExitCode);
        Assert.Contains("\"agent_skills\"", ctx.Stdout);
        Assert.Contains("\"alpha\"", ctx.Stdout);
        Assert.Contains("\"beta\"", ctx.Stdout);
        Assert.Contains("\"gamma\"", ctx.Stdout);
    }

    [Fact]
    public void Set_design_persists_and_clears()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "m1c-web-ui", "--title", "M1c Web UI").ExitCode);
        Assert.Equal(0, root.Run("add", "epic", "e").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        Assert.Equal(0, root.Run("set", id, "design=m1c-web-ui").ExitCode);
        Assert.Contains("design: m1c-web-ui", File.ReadAllText(ItemFixtures.FirstItemFile(root)));

        Assert.Equal(0, root.Run("set", id, "design=").ExitCode);
        Assert.DoesNotContain("design:", File.ReadAllText(ItemFixtures.FirstItemFile(root)));
    }

    [Fact]
    public void Set_agent_skills_empty_clears()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        Assert.Equal(0, root.Run("set", id, "agent_skills=one").ExitCode);
        Assert.Equal(0, root.Run("set", id, "agent_skills=").ExitCode);

        var ctx = root.Run("context", id, "--json");
        Assert.DoesNotContain("\"agent_skills\"", ctx.Stdout);
    }

    [Fact]
    public void Edit_set_section_appends_new_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("edit", id, "--set-section", "Code pointers", "--value", "- `Foo.Bar` — entry point");
        Assert.Equal(0, r.ExitCode);

        var ctx = root.Run("context", id, "--json");
        Assert.Contains("\"code_pointers\"", ctx.Stdout);
        Assert.Contains("Foo.Bar", ctx.Stdout);
    }

    [Fact]
    public void Edit_set_section_replaces_existing_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        Assert.Equal(0, root.Run("edit", id, "--set-section", "Acceptance criteria", "--value", "- [ ] old").ExitCode);
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Acceptance criteria", "--value", "- [ ] new").ExitCode);

        var ctx = root.Run("context", id, "--json");
        Assert.Contains("new", ctx.Stdout);
        Assert.DoesNotContain("\"old\"", ctx.Stdout);
    }

    [Fact]
    public void Edit_set_section_rejects_structural_sections()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var links = root.Run("edit", id, "--set-section", "Links", "--value", "x");
        Assert.NotEqual(0, links.ExitCode);

        var comments = root.Run("edit", id, "--set-section", "Comments", "--value", "x");
        Assert.NotEqual(0, comments.ExitCode);
    }

    [Fact]
    public void Edit_set_section_requires_value()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("edit", id, "--set-section", "Code pointers");
        Assert.NotEqual(0, r.ExitCode);
    }
}
