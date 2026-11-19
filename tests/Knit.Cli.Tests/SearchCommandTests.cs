using System.Text.Json;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SearchCommandTests
{
    // One repo shape reused by every case: an item, a KB entry, a design doc
    // and a project doc, all carrying the same distinctive word.
    private static TempKnitRoot RepoWithCorpus()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "feature", "Zephyr login").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Zephyr transport choice").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "transport", "--title", "Zephyr transport").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "apollo", "--title", "Zephyr rollout", "--no-item").ExitCode);
        return root;
    }

    private static List<(string Kind, string Id, string Title)> Hits(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        return doc.RootElement.GetProperty("hits").EnumerateArray()
            .Select(h => (
                h.GetProperty("kind").GetString()!,
                h.GetProperty("id").GetString()!,
                h.GetProperty("title").GetString()!))
            .ToList();
    }

    [Fact]
    public void Search_covers_every_corpus_by_default()
    {
        using var root = RepoWithCorpus();
        var kinds = Hits(root, "search", "zephyr", "--json").Select(h => h.Kind).ToHashSet();
        Assert.Equal(new[] { "design", "item", "kb", "project" }, kinds.OrderBy(k => k));
    }

    [Fact]
    public void Kind_restricts_to_one_corpus()
    {
        using var root = RepoWithCorpus();
        foreach (var kind in new[] { "item", "kb", "design", "project" })
        {
            var hits = Hits(root, "search", "zephyr", "--kind", kind, "--json");
            Assert.All(hits, h => Assert.Equal(kind, h.Kind));
            Assert.NotEmpty(hits);
        }
    }

    [Fact]
    public void Doc_hits_carry_an_addressable_id_and_no_item_fields()
    {
        using var root = RepoWithCorpus();
        var r = root.Run("search", "zephyr", "--json");
        using var doc = JsonDocument.Parse(r.Stdout);
        foreach (var h in doc.RootElement.GetProperty("hits").EnumerateArray())
        {
            var kind = h.GetProperty("kind").GetString();
            if (kind == "item")
            {
                Assert.Equal("feature", h.GetProperty("type").GetString());
                continue;
            }
            // type/state are item-only — omitted, never null-valued.
            Assert.False(h.TryGetProperty("type", out _));
            Assert.False(h.TryGetProperty("state", out _));
        }

        var kb = Hits(root, "search", "zephyr", "--kind", "kb", "--json").Single();
        Assert.StartsWith("adr:", kb.Id);
        var design = Hits(root, "search", "zephyr", "--kind", "design", "--json").Single();
        Assert.Equal("design:transport", design.Id);
        var project = Hits(root, "search", "zephyr", "--kind", "project", "--json").Single();
        Assert.StartsWith("apollo/", project.Id);
    }

    [Fact]
    public void Unknown_kind_is_a_clean_error()
    {
        using var root = RepoWithCorpus();
        var r = root.Run("search", "zephyr", "--kind", "notes");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("unknown --kind", r.Stderr);
        Assert.Equal("", r.Stdout.Trim());
    }
}
