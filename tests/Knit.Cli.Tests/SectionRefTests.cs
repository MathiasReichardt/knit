namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SectionRefTests
{
    private const string Sections = "\n## Scope\n\none\n\n### Detail\n\ntwo\n\n## After\n\nthree\n";

    private static string Init(TempKnitRoot root)
    {
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "web", "--title", "Web").ExitCode);
        var design = Path.Combine(root.Path, ".knit", "designs", "web-design.md");
        File.AppendAllText(design, Sections);
        return design;
    }

    [Fact]
    public void Show_prints_the_section_with_its_subsections()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("ref", "show", "design:web#scope");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("## Scope", r.Stdout);
        Assert.Contains("two", r.Stdout);
        Assert.DoesNotContain("three", r.Stdout);

        var json = root.Run("ref", "show", "design:web#detail", "--json");
        Assert.Contains("\"section\": \"detail\"", json.Stdout);
        Assert.Contains("\"text\": \"### Detail\\n\\ntwo\"", json.Stdout);
    }

    [Fact]
    public void Show_of_a_missing_section_fails_and_points_to_sections()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("ref", "show", "design:web#nope");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--sections", r.Stderr);
    }

    [Fact]
    public void Sections_lists_each_anchor_as_a_ref()
    {
        using var root = new TempKnitRoot();
        var design = Init(root);
        File.AppendAllText(design, "\n## Übersicht\n\n## A very long heading that runs past the forty character cap\n");

        var r = root.Run("ref", "show", "design:web", "--sections");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("design:web#scope", r.Stdout);
        Assert.Contains("design:web#detail", r.Stdout);
        Assert.Contains("design:web#ubersicht", r.Stdout);
        Assert.Contains("design:web#a-very-long-heading-that-runs-past-the-f", r.Stdout);

        var json = root.Run("ref", "show", "design:web", "--sections", "--json");
        Assert.Contains("\"anchor\": \"ubersicht\"", json.Stdout);
    }

    [Fact]
    public void A_path_ref_takes_a_section_too()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("ref", "show", ".knit/designs/web-design.md#after");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("three", r.Stdout);
        Assert.NotEqual(0, root.Run("ref", "show", ".knit/../x.md").ExitCode);
    }

    [Fact]
    public void Verify_flags_missing_and_suffixed_sections_in_every_body()
    {
        using var root = new TempKnitRoot();
        var design = Init(root);
        File.AppendAllText(design, "\n## Scope\n\nrepeat\n\n## Mermaid 1\n");
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc", "--kind", "link", "--url", "https://example/rfc").ExitCode);
        File.AppendAllText(Path.Combine(root.Path, ".knit", "kb", "sources", "rfc.md"), "\nSee `design:web#nope`.\n");
        Assert.Equal(0, root.Run("add", "task", "cited").ExitCode);
        File.AppendAllText(ItemFixtures.FirstItemFile(root), "\nSee `design:web#scope-2` and `design:web#scope`.\n");
        File.AppendAllText(design, "\nAlso `.knit/designs/web-design.md#gone`.\n");

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"ref\":\"design:web#nope\",\"reason\":\"missing_section\"", r.Stdout);
        Assert.Contains("\"ref\":\"design:web#scope-2\",\"reason\":\"suffixed_anchor\"", r.Stdout);
        Assert.Contains("\"ref\":\".knit/designs/web-design.md#gone\",\"reason\":\"missing_section\"", r.Stdout);
        Assert.Contains("\"ref\":\".knit/designs/web-design.md#mermaid-1\",\"reason\":\"reserved_anchor\"", r.Stdout);
        Assert.DoesNotContain("\"ref\":\"design:web#scope\"", r.Stdout);

        Assert.Contains("make the heading unique", root.Run("verify").Stdout);
    }

    [Fact]
    public void A_pointer_is_not_checked_as_a_section()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "cited").ExitCode);
        File.AppendAllText(ItemFixtures.FirstItemFile(root), "\nSee `design:web#mermaid-1/node/a`.\n");

        Assert.DoesNotContain("design:web#mermaid-1", root.Run("verify", "--json").Stdout);
    }

    [Fact]
    public void Verify_checks_mermaid_link_lines()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "cited").ExitCode);
        File.AppendAllText(ItemFixtures.FirstItemFile(root),
            "\n```mermaid\nflowchart LR\n  a[⇢ Scope]\n  click a href \"design:web#scope\"\n" +
            "  click b \"design:web#nope\"\n  click c href \"design:gone\"\n  click d href \"https://example.org\"\n" +
            "  click e call show()\n```\n");

        var r = root.Run("verify", "--json");
        Assert.Contains("\"ref\":\"design:web#nope\",\"reason\":\"missing_section\"", r.Stdout);
        Assert.Contains("\"ref\":\"design:gone\",\"reason\":\"unresolved\"", r.Stdout);
        Assert.DoesNotContain("\"ref\":\"design:web#scope\"", r.Stdout);
        Assert.DoesNotContain("example.org", r.Stdout);
    }

    [Fact]
    public void A_rename_keeps_the_fragment_in_code_spans_and_mermaid_links()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "old-name").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "cited").ExitCode);
        var item = ItemFixtures.FirstItemFile(root);
        File.AppendAllText(item,
            "\nSee `doc:old-name#intro`.\n\n```mermaid\nclassDiagram\n  link Order \"doc:old-name#intro\" \"tip\"\n```\n");

        Assert.Equal(0, root.Run("kb", "rename", "doc:old-name", "new-name").ExitCode);

        var text = File.ReadAllText(item);
        Assert.Contains("See `doc:new-name#intro`.", text);
        Assert.Contains("  link Order \"doc:new-name#intro\" \"tip\"", text);
    }
}
