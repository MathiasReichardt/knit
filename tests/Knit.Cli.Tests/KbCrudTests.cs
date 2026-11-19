using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class KbCrudTests
{
    private static string Flat(string s) => Regex.Replace(s, @"\s+", " ");

    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Add_adr_creates_numbered_file()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "adr", "Use UTC timestamps", "--summary", "consistency");
        Assert.Equal(0, r.ExitCode);

        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        Assert.True(Directory.Exists(adrDir));
        var files = Directory.GetFiles(adrDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("index.md", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(files);
        Assert.StartsWith("0001", Path.GetFileName(files[0]));

        var content = File.ReadAllText(files[0]);
        Assert.Contains("id: \"0001\"", content);
        Assert.Contains("status: proposed", content);
        // Nygard convention: frontmatter title carries the NNNN. prefix
        // so it mirrors the body h1 and `knit verify` stays clean.
        Assert.Contains("title: 0001. Use UTC timestamps", content);
        Assert.Contains("# 0001. Use UTC timestamps", content);
    }

    [Fact]
    public void Add_adr_increments_numbering()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("kb", "add", "adr", "First decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Second decision").ExitCode);

        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        var files = Directory.GetFiles(adrDir, "*.md")
            .Select(Path.GetFileName)
            .Where(n => !n!.Equals("index.md", StringComparison.OrdinalIgnoreCase))
            .Order()
            .ToList();
        Assert.Equal(2, files.Count);
        Assert.StartsWith("0001", files[0]!);
        Assert.StartsWith("0002", files[1]!);
    }

    [Fact]
    public void Add_adr_origin_persists_resolvable_refs()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui", "--title", "Web UI").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var itemId = ItemFixtures.FirstItemId(root);

        var r = root.Run("kb", "add", "adr", "Adopt loopback UI",
            "--origin", "design:web-ui", "--origin", $"item:{itemId}");
        Assert.True(r.ExitCode == 0, $"exit={r.ExitCode} stderr={r.Stderr}");

        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        var file = Directory.GetFiles(adrDir, "*.md")
            .First(f => !Path.GetFileName(f).Equals("index.md", StringComparison.OrdinalIgnoreCase));
        var content = File.ReadAllText(file);
        Assert.Contains("origin:", content);
        Assert.Contains("design:web-ui", content);
        Assert.Contains($"item:{itemId}", content);
    }

    [Fact]
    public void Add_adr_origin_rejects_unresolvable_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var r = root.Run("kb", "add", "adr", "Bad origin", "--origin", "design:ghost");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("does not resolve", r.Stderr);

        // Nothing was written.
        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        var files = Directory.Exists(adrDir)
            ? Directory.GetFiles(adrDir, "*.md")
                .Where(f => !Path.GetFileName(f).Equals("index.md", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];
        Assert.Empty(files);
    }

    [Fact]
    public void Add_adr_origin_rejects_non_design_or_item_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var r = root.Run("kb", "add", "adr", "Bad origin kind", "--origin", "feature:whatever");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("must be a design:<slug>", r.Stderr);
    }

    [Fact]
    public void Add_doc_creates_living_doc()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--summary", "how things fit").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md");
        Assert.True(File.Exists(path));
        Assert.Contains("summary: how things fit", File.ReadAllText(path));
    }

    [Fact]
    public void Add_feature_creates_feature_doc()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt", "--title", "User Management").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Add_feature_subdoc_after_split()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);

        // Sub-doc create succeeds against the split (folder-form) parent.
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt/wiring").ExitCode);
        var subdoc = Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt", "wiring.md");
        Assert.True(File.Exists(subdoc), $"expected {subdoc} to exist");

        // Sub-doc create against a flat-form parent is rejected.
        Assert.Equal(0, root.Run("kb", "add", "feature", "flat").ExitCode);
        Assert.NotEqual(0, root.Run("kb", "add", "feature", "flat/sub").ExitCode);
    }

    [Fact]
    public void Split_keeps_the_entry_s_own_links_resolving_from_one_level_deeper()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "billing").ExitCode);
        var features = Path.Combine(root.Path, ".knit", "kb", "features");
        File.AppendAllText(Path.Combine(features, "user-mgmt.md"), "\nSee [billing](billing.md).\n");

        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);

        Assert.Contains("[billing](../billing.md)", File.ReadAllText(Path.Combine(features, "user-mgmt", "readme.md")));
    }

    [Fact]
    public void Add_log_prefixes_date()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("kb", "add", "log", "retro", "--date", "2026-04-15").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "logs", "2026-04-15-retro.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Add_source_requires_url_for_link()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "source", "rfc-7234", "--kind", "link");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--url", r.Stderr);
    }

    [Fact]
    public void Add_source_with_url_writes_link()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc-7234",
            "--kind", "link", "--url", "https://example/rfc", "--summary", "caching").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "sources", "rfc-7234.md");
        Assert.True(File.Exists(path));
        var c = File.ReadAllText(path);
        Assert.Contains("source_kind: link", c);
        Assert.Contains("url: https://example/rfc", c);
    }

    [Fact]
    public void Add_source_slugifies_id()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "RFC 9457",
            "--kind", "link", "--url", "https://example/rfc").ExitCode);
        // Id is slugified for the filename; the raw input seeds the title.
        var path = Path.Combine(root.Path, ".knit", "kb", "sources", "rfc-9457.md");
        Assert.True(File.Exists(path));
        var c = File.ReadAllText(path);
        Assert.Contains("title: RFC 9457", c);
    }

    [Fact]
    public void Add_feature_slugifies_slug()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "User Mgmt").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt.md");
        Assert.True(File.Exists(path));
        // Raw typed slug seeds the title when no --title is given.
        Assert.Contains("title: User Mgmt", File.ReadAllText(path));
    }

    [Fact]
    public void Add_idea_slugifies_explicit_slug()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "idea", "capture this",
            "--slug", "My Great Idea!").ExitCode);
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "kb", "ideas", "my-great-idea.md")));
    }

    [Fact]
    public void Set_tags_writes_normalized_list_not_scalar()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);

        // Comma-separated, with whitespace, a blank entry, and a case dupe.
        Assert.Equal(0, root.Run("kb", "set", "doc:architecture", "tags", "a, A , , b").ExitCode);

        var path = Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md");
        var content = File.ReadAllText(path);
        // List form, deduped (case-insensitive, first casing kept) and trimmed.
        Assert.Contains("tags:", content);
        Assert.Contains("- a", content);
        Assert.Contains("- b", content);
        Assert.DoesNotContain("- A", content);

        var show = root.Run("kb", "show", "doc:architecture", "--json");
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("\"a\"", show.Stdout);
        Assert.Contains("\"b\"", show.Stdout);
    }

    [Fact]
    public void Show_resolves_and_emits_json()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);

        var r = root.Run("kb", "show", "doc:architecture", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"ref\": \"doc:architecture\"", r.Stdout);
        Assert.Contains("\"frontmatter\"", r.Stdout);
        Assert.Contains("\"body\"", r.Stdout);
    }

    [Fact]
    public void Show_path_only_prints_path()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);

        var r = root.Run("kb", "show", "doc:architecture", "--path-only");
        Assert.Equal(0, r.ExitCode);
        Assert.EndsWith($"architecture.md{Environment.NewLine}", r.Stdout);
    }

    [Fact]
    public void Show_unresolved_returns_nonzero()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "show", "doc:ghost");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void List_filters_by_bucket()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "first").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);

        var r = root.Run("kb", "list", "--bucket", "adr", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"ref\": \"adr:0001-first\"", r.Stdout);
        Assert.DoesNotContain("\"ref\": \"doc:arch\"", r.Stdout);
    }

    [Fact]
    public void List_filters_by_status_for_adrs()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "first").ExitCode);

        var accepted = root.Run("kb", "list", "--status", "accepted", "--json");
        Assert.Equal(0, accepted.ExitCode);
        Assert.DoesNotContain("\"ref\": \"adr:", accepted.Stdout);

        var proposed = root.Run("kb", "list", "--status", "proposed", "--json");
        Assert.Equal(0, proposed.ExitCode);
        Assert.Contains("\"ref\": \"adr:", proposed.Stdout);
    }

    [Fact]
    public void Set_updates_frontmatter_field()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "first").ExitCode);
        Assert.Equal(0, root.Run("kb", "set", "adr:0001-first", "status", "accepted").ExitCode);

        var show = root.Run("kb", "show", "adr:0001", "--json");
        Assert.Contains("\"status\": \"accepted\"", show.Stdout);
    }

    [Fact]
    public void Set_rejects_unknown_field()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);
        var r = root.Run("kb", "set", "doc:arch", "bogus", "x");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void Bare_slug_is_refused_with_the_full_ref_suggested()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);

        var r = root.Run("kb", "edit", "arch", "--set-section", "Notes", "--value", "x");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("has no bucket", Flat(r.Stderr));
        Assert.Contains("did you mean `doc:arch`?", Flat(r.Stderr));
    }

    [Fact]
    public void Bare_slug_in_several_buckets_lists_every_match()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "auth").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "auth").ExitCode);

        var r = root.Run("kb", "show", "auth");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("`doc:auth`", Flat(r.Stderr));
        Assert.Contains("`feature:auth`", Flat(r.Stderr));
    }
}
