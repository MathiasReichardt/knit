namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class ProseItemRefTests
{
    private static string Init(TempKnitRoot root)
    {
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "cited").ExitCode);
        return ItemFixtures.FirstItemId(root);
    }

    private static string AddDoc(TempKnitRoot root, string body)
    {
        Assert.Equal(0, root.Run("kb", "add", "doc", "notes").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "docs", "notes.md");
        File.AppendAllText(path, "\n" + body);
        return path;
    }

    [Fact]
    public void Verify_passes_a_full_item_ref_and_flags_a_dead_one()
    {
        using var root = new TempKnitRoot();
        var id = Init(root);
        AddDoc(root, $"Live `item:{id}` \"cited\", dead `item:ZZZZZZZZZZZZZZZZZZ`.\n");

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain($"item:{id}", r.Stdout);
        Assert.Contains("\"ref\":\"item:ZZZZZZZZZZZZZZZZZZ\",\"reason\":\"unresolved\"", r.Stdout);
    }

    [Fact]
    public void Verify_flags_a_short_item_ref_even_while_unique()
    {
        using var root = new TempKnitRoot();
        var id = Init(root);
        AddDoc(root, $"See `item:{id[..6]}`.\n");

        var r = root.Run("verify", "--json");
        Assert.Contains($"\"reason\":\"short_id\",\"candidates\":[\"{id}\"]", r.Stdout);
        Assert.Contains("short id", root.Run("verify").Stdout);
    }

    [Fact]
    public void Verify_lists_candidates_for_an_ambiguous_short_ref_and_relink_leaves_it()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "twin").ExitCode);
        // Give both items a shared prefix, as a grown repo eventually does.
        var files = ItemFixtures.AllItemFiles(root).ToList();
        var ids = new[] { "ABCD00000000000001", "ABCD00000000000002" };
        for (var i = 0; i < 2; i++)
        {
            var oldId = ItemFixtures.IdFromEntry(ItemFixtures.EntryName(files[i]));
            var dest = Path.Combine(Path.GetDirectoryName(files[i])!, Path.GetFileName(files[i]).Replace(oldId, ids[i]));
            File.WriteAllText(dest, File.ReadAllText(files[i]).Replace(oldId, ids[i]));
            File.Delete(files[i]);
        }
        var doc = AddDoc(root, "See `item:ABCD`.\n");

        var r = root.Run("verify", "--json");
        Assert.Contains($"\"reason\":\"ambiguous_id\",\"candidates\":[\"{ids[0]}\",\"{ids[1]}\"]", r.Stdout);

        Assert.Equal(0, root.Run("relink", "--non-interactive").ExitCode);
        Assert.Contains("`item:ABCD`", File.ReadAllText(doc));
    }

    [Fact]
    public void Relink_expands_a_unique_short_ref_and_leaves_code_alone()
    {
        using var root = new TempKnitRoot();
        var id = Init(root);
        var shortRef = $"`item:{id[..5].ToLowerInvariant()}`";
        var doc = AddDoc(root, $"See {shortRef} twice: {shortRef}.\n\n```\n{shortRef}\n```\n");

        Assert.Equal(0, root.Run("relink", "--non-interactive").ExitCode);

        var text = File.ReadAllText(doc);
        Assert.Contains($"See `item:{id}` twice: `item:{id}`.", text);
        Assert.Contains($"```\n{shortRef}\n```", text);
        Assert.Contains("ok", root.Run("verify").Stdout);
    }

    [Fact]
    public void Scan_reads_nested_bullets_and_non_links_tables_but_skips_indented_code_and_links_rows()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddDoc(root,
            "- parent\n" +
            "    - nested `adr:0001-nested`\n" +
            "- item wraps\n" +
            "    onto `adr:0002-wrapped`\n" +
            "\n" +
            "| Col | Ref |\n|---|---|\n| a | `adr:0003-table` |\n" +
            "\n" +
            "Code follows:\n\n" +
            "    `adr:0004-indented-code`\n" +
            "\n" +
            "## Links\n\n" +
            "| Rel | Target | Notes |\n|---|---|---|\n| KB ref | `adr:0005-links-row` | |\n");

        var stdout = root.Run("verify", "--json").Stdout;
        var proseRefs = stdout[stdout.IndexOf("\"prose_refs\"", StringComparison.Ordinal)..stdout.IndexOf("\"prose_links\"", StringComparison.Ordinal)];
        Assert.Contains("adr:0001-nested", proseRefs);
        Assert.Contains("adr:0002-wrapped", proseRefs);
        Assert.Contains("adr:0003-table", proseRefs);
        Assert.DoesNotContain("adr:0004-indented-code", proseRefs);
        Assert.DoesNotContain("adr:0005-links-row", proseRefs); // the structured link_targets check owns it
    }

    [Fact]
    public void Scan_reads_design_and_project_docs()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "launch").ExitCode);
        File.AppendAllText(Path.Combine(root.Path, ".knit", "designs", "web-ui-design.md"), "\nSee `adr:0001-from-design`.\n");
        File.WriteAllText(Path.Combine(root.Path, ".knit", "projects", "launch", "roadmap.md"), "# Roadmap\n\nSee `design:from-roadmap`.\n");

        var r = root.Run("verify", "--json");
        Assert.Contains("adr:0001-from-design", r.Stdout);
        Assert.Contains("design:from-roadmap", r.Stdout);
    }
}
