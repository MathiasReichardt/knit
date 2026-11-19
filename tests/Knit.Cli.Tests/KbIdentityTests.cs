using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// A KB entry's own <c>id:</c> duplicates its filename, and
/// <c>kb rename</c> used to move the file while leaving the field behind —
/// a declared id that resolves nowhere, invisible to <c>verify</c>, with a
/// recovery hint that proposed creating a duplicate.
/// </summary>
[Collection(nameof(CliCollection))]
public class KbIdentityTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static string SourcePath(TempKnitRoot root, string slug) =>
        Path.Combine(root.Path, ".knit", "kb", "sources", $"{slug}.md");

    private static string? Field(string path, string name)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            if (line == "---") continue;
            if (line.StartsWith(name + ":", StringComparison.Ordinal))
                return line[(name.Length + 1)..].Trim().Trim('"');
        }
        return null;
    }

    [Fact]
    public void Renaming_a_source_rewrites_its_own_id()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "release-process-description-schedule-notification", "--url", "https://x").ExitCode);
        var oldSlug = Field(SourcePath(root, "release-process-description-schedule-not"), "id");
        Assert.Equal("release-process-description-schedule-not", oldSlug);

        Assert.Equal(0, root.Run("kb", "rename", "source:release-process-description-schedule-not", "release-process-description").ExitCode);

        Assert.Equal("release-process-description", Field(SourcePath(root, "release-process-description"), "id"));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Renaming_an_adr_carries_the_number_its_slug_declares()
    {
        // An ADR's id is the number minted into its slug, not the slug — so a
        // renumbering rename drifts it exactly like a source's id would.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "retry policy").ExitCode);
        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        Assert.Equal("0001", Field(Path.Combine(adrDir, "0001-retry-policy.md"), "id"));

        Assert.Equal(0, root.Run("kb", "rename", "adr:0001-retry-policy", "0002-retry-and-backoff").ExitCode);

        Assert.Equal("0002", Field(Path.Combine(adrDir, "0002-retry-and-backoff.md"), "id"));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Renaming_an_adr_to_a_numberless_slug_is_refused()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "retry policy").ExitCode);

        var r = root.Run("kb", "rename", "adr:0001-retry-policy", "retry-and-backoff");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("must start with one", r.Stderr);
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "kb", "adrs", "0001-retry-policy.md")));
    }

    [Fact]
    public void A_bucket_without_an_id_does_not_gain_one_on_rename()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("kb", "rename", "doc:architecture", "arch-overview").ExitCode);

        var path = Path.Combine(root.Path, ".knit", "kb", "docs", "arch-overview.md");
        Assert.Null(Field(path, "id"));
    }

    [Fact]
    public void Verify_reports_a_hand_edited_id_with_both_values()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc-7231", "--url", "https://x").ExitCode);
        var path = SourcePath(root, "rfc-7231");
        File.WriteAllText(path, File.ReadAllText(path).Replace("id: rfc-7231", "id: rfc-2616"));

        var r = root.Run("verify", "--json");

        Assert.Equal(4, r.ExitCode);
        var drift = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("identity_drift");
        var row = Assert.Single(drift.EnumerateArray());
        Assert.Equal("id", row.GetProperty("field").GetString());
        Assert.Equal("rfc-2616", row.GetProperty("declared").GetString());
        Assert.Equal("rfc-7231", row.GetProperty("expected").GetString());
    }

    [Fact]
    public void Verify_reports_a_design_slug_that_disagrees_with_its_filename()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "sync-protocol").ExitCode);
        var path = Path.Combine(root.Path, ".knit", "designs", "sync-protocol-design.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("slug: sync-protocol", "slug: sync-proto"));

        var r = root.Run("verify", "--json");

        Assert.Equal(4, r.ExitCode);
        var row = Assert.Single(
            JsonDocument.Parse(r.Stdout).RootElement.GetProperty("identity_drift").EnumerateArray());
        Assert.Equal("slug", row.GetProperty("field").GetString());
        Assert.Equal("sync-proto", row.GetProperty("declared").GetString());
        Assert.Equal("sync-protocol", row.GetProperty("expected").GetString());
    }

    [Fact]
    public void Relink_ids_sets_the_field_from_the_filename()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc-7231", "--url", "https://x").ExitCode);
        var path = SourcePath(root, "rfc-7231");
        File.WriteAllText(path, File.ReadAllText(path).Replace("id: rfc-7231", "id: rfc-2616"));

        var r = root.Run("relink", "--ids", "--non-interactive");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("rfc-7231", Field(path, "id"));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Relink_without_ids_leaves_identity_fields_alone()
    {
        // Rewriting an identity field is not a side effect of repairing link
        // targets — it needs asking for.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc-7231", "--url", "https://x").ExitCode);
        var path = SourcePath(root, "rfc-7231");
        File.WriteAllText(path, File.ReadAllText(path).Replace("id: rfc-7231", "id: rfc-2616"));

        Assert.Equal(0, root.Run("relink", "--non-interactive").ExitCode);

        Assert.Equal("rfc-2616", Field(path, "id"));
    }

    [Fact]
    public void An_unresolved_slug_declared_as_a_stale_id_names_the_file_not_add()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc-7231", "--url", "https://x").ExitCode);
        var path = SourcePath(root, "rfc-7231");
        File.WriteAllText(path, File.ReadAllText(path).Replace("id: rfc-7231", "id: rfc-2616"));
        var add = root.Run("add", "task", "read the spec", "--json");
        var id = JsonDocument.Parse(add.Stdout).RootElement.GetProperty("id").GetString()!;

        var r = root.Run("link", id, "--kb-ref", "source:rfc-2616");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("kb/sources/rfc-7231.md", r.Stderr);
        Assert.Contains("relink --ids", r.Stderr);
        Assert.DoesNotContain("knit kb add", r.Stderr);
    }

    [Fact]
    public void A_design_ref_is_not_a_kb_bucket_to_search()
    {
        // `design:` / `project:` are ref address spaces, not KB buckets — the
        // hint has nothing to enumerate and must still produce an error.
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "show", "design:nothing-here");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("nothing-here", r.Stderr);
    }

    [Fact]
    public void An_unknown_slug_keeps_the_add_hint()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var add = root.Run("add", "task", "read the spec", "--json");
        var id = JsonDocument.Parse(add.Stdout).RootElement.GetProperty("id").GetString()!;

        var r = root.Run("link", id, "--kb-ref", "source:nothing-like-this");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit kb add source nothing-like-this", r.Stderr);
    }
}
