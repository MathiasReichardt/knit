using System.Text.Json;
using Knit.Core.Config;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit mount` + the mount half of `kb source add` / `verify`.
/// </summary>
[Collection(nameof(CliCollection))]
public class MountCliTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);

    /// <summary>A directory outside the repo, the shape a mount points at.
    /// Returned as the relative value that belongs in the config.</summary>
    private static string Outside(TempKnitRoot root, string name)
    {
        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(root.Path, "..", name)));
        return $"../{name}";
    }

    private static string ConfigText(TempKnitRoot root, string file) =>
        File.ReadAllText(Path.Combine(root.Path, ".knit", file));

    [Fact]
    public void Add_writes_the_committed_config_and_list_reports_it_resolved()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");

        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        Assert.Contains("\"docs\"", ConfigText(root, ConfigLoader.FileName));

        var listed = Mounts(root);
        Assert.Single(listed);
        Assert.Equal("docs", listed[0].GetProperty("name").GetString());
        Assert.Equal("repo", listed[0].GetProperty("source").GetString());
        Assert.True(listed[0].GetProperty("resolves").GetBoolean());
        Assert.True(listed[0].GetProperty("portable").GetBoolean());
    }

    [Fact]
    public void Add_refuses_a_name_that_is_already_declared_and_points_at_set()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);

        // Asserted on --json: the human form is hard-wrapped at the console
        // width, which can split a word mid-string.
        var again = root.Run("mount", "add", "docs", rel, "--json");
        Assert.NotEqual(0, again.ExitCode);
        Assert.Contains("knit mount set", again.Stderr);
    }

    [Fact]
    public void Set_repoints_an_existing_mount()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var first = Outside(root, $"a-{Guid.NewGuid():N}");
        var second = Outside(root, $"b-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", first).ExitCode);

        Assert.Equal(0, root.Run("mount", "set", "docs", second).ExitCode);
        Assert.Equal(second, Mounts(root)[0].GetProperty("declared_path").GetString());
    }

    [Fact]
    public void Invalid_names_are_refused_with_the_rule()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("mount", "add", "C", "../anything");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("invalid mount name", r.Stderr);
    }

    [Fact]
    public void Local_override_wins_and_is_written_to_the_uncommitted_file()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var committed = Outside(root, $"committed-{Guid.NewGuid():N}");
        var local = Outside(root, $"local-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", committed).ExitCode);

        Assert.Equal(0, root.Run("mount", "set", "docs", local, "--local").ExitCode);

        // The committed value is untouched; only the overlay carries the new one.
        Assert.Contains(committed, ConfigText(root, ConfigLoader.FileName));
        Assert.Contains(local, ConfigText(root, ConfigLoader.LocalFileName));
        var listed = Mounts(root);
        Assert.Single(listed);
        Assert.Equal("local", listed[0].GetProperty("source").GetString());
        Assert.Equal(local, listed[0].GetProperty("declared_path").GetString());
    }

    [Fact]
    public void The_local_overlay_does_not_clobber_settings_it_never_declared()
    {
        // Writing the overlay as a full config record would materialise every
        // default and silently reset handle / due_soon_days.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}"), "--local").ExitCode);

        var cfg = ConfigLoader.Load(Path.Combine(root.Path, ".knit"));
        Assert.Equal("alice", cfg.Handle);
        Assert.DoesNotContain("due_soon_days", ConfigText(root, ConfigLoader.LocalFileName));
    }

    [Fact]
    public void Removing_a_local_override_hands_the_mount_back_to_the_committed_value()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var committed = Outside(root, $"committed-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", committed).ExitCode);
        Assert.Equal(0, root.Run("mount", "set", "docs", Outside(root, $"local-{Guid.NewGuid():N}"), "--local").ExitCode);

        var r = root.Run("mount", "remove", "docs", "--local");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("still declares it", r.Stdout);
        Assert.Equal("repo", Mounts(root)[0].GetProperty("source").GetString());
    }

    [Fact]
    public void Removing_a_mount_that_is_not_declared_there_errors()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.NotEqual(0, root.Run("mount", "remove", "docs").ExitCode);
    }

    [Fact]
    public void An_absolute_value_is_accepted_but_reported_as_not_portable()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var abs = Path.GetFullPath(Path.Combine(root.Path, "..", $"abs-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(abs);

        var r = root.Run("mount", "add", "docs", abs);
        Assert.Equal(0, r.ExitCode);
        // Said on stderr so stdout stays capture-clean.
        Assert.Contains("not portable", r.Stderr);
        Assert.False(Mounts(root)[0].GetProperty("portable").GetBoolean());
    }

    [Fact]
    public void Init_gitignores_the_local_config()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var ignore = File.ReadAllText(Path.Combine(root.Path, ".knit", ".gitignore"));
        Assert.Contains(ConfigLoader.LocalFileName, ignore);
    }

    [Fact]
    public void Kb_source_add_stores_the_mount_beside_the_path()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);

        Assert.Equal(0, root.Run(
            "kb", "add", "source", "api-spec", "--kind", "file",
            "--path", "specs/api.md", "--mount", "docs").ExitCode);

        var file = Path.Combine(root.Path, ".knit", "kb", "sources", "api-spec.md");
        var text = File.ReadAllText(file);
        Assert.Contains("mount: docs", text);
        Assert.Contains("path: specs/api.md", text);
    }

    [Fact]
    public void Kb_source_add_refuses_an_undeclared_mount()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run(
            "kb", "add", "source", "api-spec", "--kind", "file",
            "--path", "specs/api.md", "--mount", "docs");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("unknown mount 'docs'", r.Stderr);
        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "kb", "sources", "api-spec.md")));
    }

    [Fact]
    public void Kb_source_add_refuses_a_mount_on_a_link_source()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);

        var r = root.Run(
            "kb", "add", "source", "rfc", "--kind", "link",
            "--url", "https://example.org/rfc", "--mount", "docs");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("file sources only", r.Stderr);
    }

    [Fact]
    public void Verify_reports_an_unresolved_mount_once_with_its_source_count()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // Declare it against a directory that exists, add two sources, then
        // point it at nothing — the order a real checkout arrives in.
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "one", "--kind", "file", "--path", "a.md", "--mount", "docs").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "two", "--kind", "file", "--path", "b.md", "--mount", "docs").ExitCode);
        Assert.Equal(0, root.Run("mount", "set", "docs", "../gone-for-good").ExitCode);

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        var mounts = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts");
        Assert.Equal(1, mounts.GetArrayLength());
        Assert.Equal("docs", mounts[0].GetProperty("name").GetString());
        Assert.Equal(2, mounts[0].GetProperty("source_count").GetInt32());
        Assert.Contains("does not exist", mounts[0].GetProperty("reason").GetString());
    }

    [Fact]
    public void Verify_flags_a_mount_a_source_names_but_nothing_declares()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "one", "--kind", "file", "--path", "a.md", "--mount", "docs").ExitCode);
        // Hand-editing the config out is how this arrives in practice.
        Assert.Equal(0, root.Run("mount", "remove", "docs").ExitCode);

        var r = root.Run("verify", "--json");
        var mounts = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts");
        Assert.Equal(1, mounts.GetArrayLength());
        Assert.Contains("not declared", mounts[0].GetProperty("reason").GetString());
        // An entry naming a mount nobody declares is broken content, so
        // --strict fails on it; a merely unresolved path does not.
        Assert.NotEqual(0, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void A_healthy_mount_keeps_verify_quiet()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"d-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        File.WriteAllText(Path.Combine(root.Path, rel, "a.md"), "a");
        Assert.Equal(0, root.Run("kb", "add", "source", "one", "--kind", "file", "--path", "a.md", "--mount", "docs").ExitCode);

        var r = root.Run("verify", "--strict", "--json");
        Assert.Equal(0, r.ExitCode);
        var json = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal(0, json.GetProperty("mounts").GetArrayLength());
        Assert.Equal(0, json.GetProperty("source_paths").GetArrayLength());
    }

    [Fact]
    public void Verify_flags_a_source_whose_file_is_gone_from_a_resolving_mount()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "one", "--kind", "file", "--path", "specs/gone.md", "--mount", "docs").ExitCode);

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        var row = Assert.Single(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("source_paths").EnumerateArray());
        Assert.Equal("docs", row.GetProperty("mount").GetString());
        Assert.Equal("specs/gone.md", row.GetProperty("path").GetString());
        Assert.Equal("does not exist", row.GetProperty("reason").GetString());
        Assert.NotEqual(0, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Verify_leaves_sources_on_an_unresolved_mount_to_the_mount_row()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "one", "--kind", "file", "--path", "a.md", "--mount", "docs").ExitCode);
        Assert.Equal(0, root.Run("mount", "set", "docs", "../gone-for-good").ExitCode);

        var json = JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement;
        Assert.Equal(1, json.GetProperty("mounts").GetArrayLength());
        Assert.Equal(0, json.GetProperty("source_paths").GetArrayLength());
    }

    [Fact]
    public void Verify_checks_prose_links_into_a_mount_only_while_it_resolves()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var name = $"d-{Guid.NewGuid():N}";
        var rel = Outside(root, name);
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        File.WriteAllText(Path.Combine(root.Path, rel, "here.md"), "x");
        Assert.Equal(0, root.Run("kb", "add", "doc", "notes").ExitCode);
        // .knit/kb/docs/notes.md → four levels up is the repo's parent.
        File.AppendAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "notes.md"),
            $"\nSee [here](../../../../{name}/here.md) and [gone](../../../../{name}/gone.md).\n");

        var links = ProseLinkTargets(root);
        Assert.Equal([$"../../../../{name}/gone.md"], links);

        // The checkout of the mounted repo is missing on this machine: the
        // mount row says so, the links under it are not judged.
        Directory.Delete(Path.Combine(root.Path, rel), recursive: true);
        Assert.Empty(ProseLinkTargets(root));
    }

    private static List<string?> ProseLinkTargets(TempKnitRoot root) =>
        JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement.GetProperty("prose_links")
            .EnumerateArray().Select(l => l.GetProperty("target").GetString()).ToList();

    // ---- Resolve on read, auto-detect on write --------------------------

    [Fact]
    public void A_literal_path_is_stored_split_and_the_chosen_mount_is_reported()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);

        // No --mount: the caller passes the path they can see.
        var r = root.Run("kb", "add", "source", "plan", "--kind", "file", "--path", $"{rel}/specs/plan.md");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("docs", r.Stderr);

        var text = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "sources", "plan.md"));
        Assert.Contains("path: specs/plan.md", text);
        Assert.Contains("mount: docs", text);
    }

    [Fact]
    public void A_repo_relative_path_is_stored_verbatim_with_no_mount()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);

        Assert.Equal(0, root.Run("kb", "add", "source", "inside", "--kind", "file", "--path", "docs/notes.md").ExitCode);

        var text = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "sources", "inside.md"));
        Assert.Contains("path: docs/notes.md", text);
        Assert.DoesNotContain("mount:", text);
    }

    [Fact]
    public void A_literal_path_no_mount_covers_is_refused_with_the_declaring_verb()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "source", "plan", "--kind", "file", "--path", "../nowhere/plan.md", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit mount add", r.Stderr);
        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "kb", "sources", "plan.md")));
    }

    [Fact]
    public void A_literal_path_outside_the_named_mount_is_refused()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);

        var r = root.Run(
            "kb", "add", "source", "plan", "--kind", "file",
            "--path", "../nowhere/plan.md", "--mount", "docs", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("outside mount", r.Stderr);
    }

    [Fact]
    public void Show_reports_where_a_mounted_path_actually_lands()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        Assert.Equal(0, root.Run(
            "kb", "add", "source", "plan", "--kind", "file", "--path", "specs/plan.md", "--mount", "docs").ExitCode);

        var human = root.Run("kb", "show", "source:plan");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("resolved", human.Stdout);

        var json = JsonDocument.Parse(root.Run("kb", "show", "source:plan", "--json").Stdout).RootElement;
        Assert.EndsWith("plan.md", json.GetProperty("resolved_path").GetString());
        Assert.False(json.TryGetProperty("mount_reason", out _));
    }

    [Fact]
    public void Show_reports_the_config_problem_instead_of_a_path_when_the_mount_is_broken()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);
        Assert.Equal(0, root.Run(
            "kb", "add", "source", "plan", "--kind", "file", "--path", "specs/plan.md", "--mount", "docs").ExitCode);
        Assert.Equal(0, root.Run("mount", "set", "docs", "../gone-for-good").ExitCode);

        var json = JsonDocument.Parse(root.Run("kb", "show", "source:plan", "--json").Stdout).RootElement;
        Assert.Equal("mount 'docs' unresolved", json.GetProperty("mount_reason").GetString());
        Assert.False(json.TryGetProperty("resolved_path", out _));
    }

    [Fact]
    public void Source_path_prints_one_capture_clean_line()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);
        Assert.Equal(0, root.Run(
            "kb", "add", "source", "plan", "--kind", "file", "--path", "specs/plan.md", "--mount", "docs").ExitCode);

        var r = root.Run("kb", "show", "source:plan", "--source-path");
        Assert.Equal(0, r.ExitCode);
        var line = Assert.Single(r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.True(Path.IsPathFullyQualified(line.Trim()));
        Assert.EndsWith("plan.md", line.Trim());
    }

    [Fact]
    public void Source_path_works_on_a_legacy_path_with_no_mount()
    {
        // The migration aid: it points at the file so the path can be checked
        // before a mount is declared for it.
        using var root = new TempKnitRoot();
        Init(root);
        WriteLegacySource(root, "legacy", "../Docs/old.pdf");

        var r = root.Run("kb", "show", "source:legacy", "--source-path");
        Assert.Equal(0, r.ExitCode);
        Assert.EndsWith("old.pdf", r.Stdout.Trim());
    }

    [Fact]
    public void Source_path_on_a_link_says_so_rather_than_printing_a_url()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "rfc", "--kind", "link", "--url", "https://example.org/rfc").ExitCode);

        var r = root.Run("kb", "show", "source:rfc", "--source-path", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Equal("", r.Stdout.Trim());
        Assert.Contains("link", r.Stderr);
    }

    [Fact]
    public void Source_path_fails_loudly_when_the_mount_is_broken()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("mount", "add", "docs", Outside(root, $"d-{Guid.NewGuid():N}")).ExitCode);
        Assert.Equal(0, root.Run(
            "kb", "add", "source", "plan", "--kind", "file", "--path", "specs/plan.md", "--mount", "docs").ExitCode);
        Assert.Equal(0, root.Run("mount", "set", "docs", "../gone-for-good").ExitCode);

        var r = root.Run("kb", "show", "source:plan", "--source-path", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Equal("", r.Stdout.Trim());
    }

    [Fact]
    public void Setting_the_real_path_adopts_a_pre_mount_entry()
    {
        // The migration path for entries written before mounts existed: no
        // bulk command, but nothing has to be split by hand either.
        using var root = new TempKnitRoot();
        Init(root);
        var rel = Outside(root, $"docs-{Guid.NewGuid():N}");
        WriteLegacySource(root, "legacy", $"{rel}/specs/plan.md");
        Assert.Equal(0, root.Run("mount", "add", "docs", rel).ExitCode);

        Assert.Equal(0, root.Run("kb", "set", "source:legacy", "path", $"{rel}/specs/plan.md").ExitCode);

        var text = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "sources", "legacy.md"));
        Assert.Contains("path: specs/plan.md", text);
        Assert.Contains("mount: docs", text);
    }

    /// <summary>
    /// A source as written before mounts existed: a bare escaping path and no
    /// mount. Edited onto disk because the CLI now rebases such a path.
    /// </summary>
    private static void WriteLegacySource(TempKnitRoot root, string id, string path)
    {
        Assert.Equal(0, root.Run("kb", "add", "source", id, "--kind", "file", "--path", "placeholder.md").ExitCode);
        var file = Path.Combine(root.Path, ".knit", "kb", "sources", $"{id}.md");
        File.WriteAllText(file, File.ReadAllText(file).Replace("path: placeholder.md", $"path: {path}"));
    }

    private static List<JsonElement> Mounts(TempKnitRoot root)
    {
        var r = root.Run("mount", "list", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts").EnumerateArray().ToList();
    }
}
