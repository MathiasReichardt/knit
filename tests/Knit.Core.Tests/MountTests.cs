using Knit.Core.Config;

namespace Knit.Core.Tests;

public sealed class MountTempDir : IDisposable
{
    public string Root { get; }
    public string KnitDir { get; }

    public MountTempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), $"knit-mount-{Guid.NewGuid():N}", "repo");
        KnitDir = Path.Combine(Root, ".knit");
        Directory.CreateDirectory(KnitDir);
    }

    /// <summary>A sibling directory of the repo, the shape a mount points at.</summary>
    public string Sibling(string name)
    {
        var dir = Path.GetFullPath(Path.Combine(Root, "..", name));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Write(string fileName, string json)
        => File.WriteAllText(Path.Combine(KnitDir, fileName), json);

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true); } catch { }
    }
}

public class MountNameTests
{
    [Theory]
    [InlineData("docs")]
    [InlineData("company-docs")]
    [InlineData("k8")]
    [InlineData("a1-b2")]
    public void Accepts_lowercase_kebab(string name) => Assert.True(MountResolver.IsValidName(name));

    [Theory]
    // The ≥2-char floor is what keeps `C:/x` from parsing as a mount in the
    // prose form, so single characters are rejected even though they'd slug
    // fine.
    [InlineData("c")]
    [InlineData("")]
    [InlineData("Docs")]
    [InlineData("docs/sub")]
    [InlineData("company docs")]
    [InlineData("-docs")]
    [InlineData("docs-")]
    [InlineData("a--b")]
    [InlineData("docs_1")]
    public void Rejects_everything_else(string name) => Assert.False(MountResolver.IsValidName(name));

    [Fact]
    public void Rejects_null() => Assert.False(MountResolver.IsValidName(null));
}

public class MountResolverTests : IDisposable
{
    private readonly MountTempDir _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Relative_value_resolves_against_the_repo_root()
    {
        var target = _t.Sibling("company-docs");
        var info = MountResolver.Describe(_t.Root, "docs", "../company-docs", MountSource.Repo);

        Assert.Equal(target, info.EffectivePath);
        Assert.True(info.Resolves);
        Assert.True(info.Portable);
    }

    [Fact]
    public void Absolute_value_resolves_but_is_not_portable()
    {
        var target = _t.Sibling("elsewhere");
        var info = MountResolver.Describe(_t.Root, "docs", target, MountSource.Repo);

        Assert.True(info.Resolves);
        Assert.False(info.Portable);
    }

    [Fact]
    public void Missing_directory_is_unresolved_not_an_error()
    {
        var info = MountResolver.Describe(_t.Root, "docs", "../never-created", MountSource.Repo);

        Assert.False(info.Resolves);
        Assert.EndsWith("never-created", info.EffectivePath);
    }

    [Fact]
    public void Local_wins_over_repo_per_name_and_keeps_the_others()
    {
        _t.Sibling("committed");
        var localTarget = _t.Sibling("machine-only");
        var repo = new KnitConfig(Mounts: new() { ["docs"] = "../committed", ["specs"] = "../committed" });
        var local = new KnitConfig(Mounts: new() { ["docs"] = localTarget });

        var described = MountResolver.Describe(_t.Root, repo, local);

        Assert.Equal(["docs", "specs"], described.Select(m => m.Name));
        var docs = described[0];
        Assert.Equal(MountSource.Local, docs.Source);
        Assert.Equal(localTarget, docs.EffectivePath);
        Assert.Equal(MountSource.Repo, described[1].Source);
    }

    [Fact]
    public void Local_wins_even_when_it_does_not_resolve()
    {
        // No disk probing: the effective path must not depend on whether a
        // directory happens to exist, or "why is my mount wrong?" becomes
        // unanswerable.
        _t.Sibling("committed");
        var repo = new KnitConfig(Mounts: new() { ["docs"] = "../committed" });
        var local = new KnitConfig(Mounts: new() { ["docs"] = "../gone" });

        var docs = MountResolver.Describe(_t.Root, repo, local).Single();

        Assert.Equal(MountSource.Local, docs.Source);
        Assert.False(docs.Resolves);
    }

    [Fact]
    public void Resolves_a_file_inside_the_mount()
    {
        var dir = _t.Sibling("company-docs");
        Directory.CreateDirectory(Path.Combine(dir, "specs"));
        var file = Path.Combine(dir, "specs", "plan.md");
        File.WriteAllText(file, "# plan");
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../company-docs" });

        var ok = MountResolver.TryResolveFile(_t.Root, cfg, "docs", "specs/plan.md", out var full, out var err);

        Assert.True(ok);
        Assert.Null(err);
        Assert.Equal(file, full);
    }

    [Fact]
    public void Backslashes_in_the_request_path_are_normalized()
    {
        var dir = _t.Sibling("company-docs");
        Directory.CreateDirectory(Path.Combine(dir, "specs"));
        File.WriteAllText(Path.Combine(dir, "specs", "plan.md"), "# plan");
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../company-docs" });

        Assert.True(MountResolver.TryResolveFile(_t.Root, cfg, "docs", @"specs\plan.md", out var full, out _));
        Assert.EndsWith("plan.md", full);
    }

    [Fact]
    public void Dot_dot_cannot_escape_the_mount()
    {
        _t.Sibling("company-docs");
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../company-docs" });

        var ok = MountResolver.TryResolveFile(_t.Root, cfg, "docs", "../../secrets.txt", out _, out var err);

        Assert.False(ok);
        Assert.Contains("escapes mount", err);
    }

    [Fact]
    public void Absolute_request_path_is_refused()
    {
        _t.Sibling("company-docs");
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../company-docs" });

        Assert.False(MountResolver.TryResolveFile(
            _t.Root, cfg, "docs", Path.Combine(Path.GetTempPath(), "x.md"), out _, out var err));
        Assert.Contains("absolute", err);
    }

    [Fact]
    public void Unknown_mount_names_the_verb_that_declares_it()
    {
        Assert.False(MountResolver.TryResolveDir(_t.Root, new KnitConfig(), "docs", out _, out var err));
        Assert.Contains("unknown mount 'docs'", err);
        Assert.Contains("knit mount add docs", err);
    }

    [Fact]
    public void Unresolved_mount_reads_as_a_config_problem()
    {
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../gone" });

        Assert.False(MountResolver.TryResolveDir(_t.Root, cfg, "docs", out _, out var err));
        Assert.Equal("mount 'docs' unresolved", err);
    }

    [Fact]
    public void Invalid_name_is_rejected_before_the_config_is_consulted()
    {
        var cfg = new KnitConfig(Mounts: new() { ["C"] = "../anything" });

        Assert.False(MountResolver.TryResolveDir(_t.Root, cfg, "C", out _, out var err));
        Assert.Contains("invalid mount name", err);
    }
}

public class ConfigOverlayTests : IDisposable
{
    private readonly MountTempDir _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Local_overrides_only_the_keys_it_declares()
    {
        // The whole reason the overlay merges JSON rather than records: a
        // local file that says nothing about due_soon_days must leave it at 7,
        // even though the deserialized record can't tell "absent" from "3".
        _t.Write(ConfigLoader.FileName, """{"handle":"alice","due_soon_days":7}""");
        _t.Write(ConfigLoader.LocalFileName, """{"handle":"bob"}""");

        var cfg = ConfigLoader.Load(_t.KnitDir);

        Assert.Equal("bob", cfg.Handle);
        Assert.Equal(7, cfg.DueSoonDays);
    }

    [Fact]
    public void Mounts_merge_per_name_rather_than_replacing_the_map()
    {
        _t.Write(ConfigLoader.FileName, """{"mounts":{"docs":"../a","specs":"../b"}}""");
        _t.Write(ConfigLoader.LocalFileName, """{"mounts":{"docs":"../local-a"}}""");

        var mounts = ConfigLoader.Load(_t.KnitDir).Mounts!;

        Assert.Equal("../local-a", mounts["docs"]);
        Assert.Equal("../b", mounts["specs"]);
    }

    [Fact]
    public void Local_alone_loads()
    {
        _t.Write(ConfigLoader.LocalFileName, """{"mounts":{"docs":"../a"}}""");

        Assert.Equal("../a", ConfigLoader.Load(_t.KnitDir).Mounts!["docs"]);
    }

    [Fact]
    public void Neither_file_loads_as_defaults()
    {
        var cfg = ConfigLoader.Load(_t.KnitDir);
        Assert.Null(cfg.Mounts);
        Assert.Equal(new KnitConfig(), cfg);
    }

    [Fact]
    public void A_layer_can_be_read_on_its_own_so_a_value_can_be_attributed()
    {
        _t.Write(ConfigLoader.FileName, """{"mounts":{"docs":"../a"}}""");
        _t.Write(ConfigLoader.LocalFileName, """{"mounts":{"docs":"../local-a"}}""");

        Assert.Equal("../a", ConfigLoader.TryLoadLayer(_t.KnitDir, ConfigLoader.FileName)!.Mounts!["docs"]);
        Assert.Equal("../local-a", ConfigLoader.TryLoadLayer(_t.KnitDir, ConfigLoader.LocalFileName)!.Mounts!["docs"]);
    }

    [Fact]
    public void Unknown_keys_survive_a_raw_round_trip()
    {
        // `knit mount` writes through the raw layer, so a hand-added key must
        // not be dropped by a mount edit.
        _t.Write(ConfigLoader.FileName, """{"handle":"alice","experimental":{"x":1}}""");

        var raw = ConfigLoader.ReadForWrite(Path.Combine(_t.KnitDir, ConfigLoader.FileName))!;
        raw["mounts"] = new System.Text.Json.Nodes.JsonObject { ["docs"] = "../a" };
        ConfigLoader.SaveRawLayer(_t.KnitDir, ConfigLoader.FileName, raw);

        var text = File.ReadAllText(Path.Combine(_t.KnitDir, ConfigLoader.FileName));
        Assert.Contains("experimental", text);
        Assert.Equal("../a", ConfigLoader.Load(_t.KnitDir).Mounts!["docs"]);
    }

    [Fact]
    public void A_malformed_layer_does_not_take_the_other_down()
    {
        _t.Write(ConfigLoader.FileName, """{"handle":"alice"}""");
        _t.Write(ConfigLoader.LocalFileName, "{ not json");

        Assert.Equal("alice", ConfigLoader.Load(_t.KnitDir).Handle);
    }
}

/// <summary>Resolving a stored locator and rebasing a literal path onto a mount.</summary>
public class MountRebaseTests : IDisposable
{
    private readonly MountTempDir _t = new();

    public void Dispose() => _t.Dispose();

    private KnitConfig WithDocs(out string dir)
    {
        dir = _t.Sibling("company-docs");
        return new KnitConfig(Mounts: new() { ["docs"] = "../company-docs" });
    }

    [Fact]
    public void A_plain_repo_relative_path_is_not_literal()
    {
        Assert.False(MountResolver.IsLiteralPath(_t.Root, "docs/notes.md"));
        Assert.False(MountResolver.IsLiteralPath(_t.Root, "./docs/notes.md"));
    }

    [Theory]
    [InlineData("../Docs/x.pdf")]
    [InlineData("sub/../../Docs/x.pdf")]
    public void A_path_that_climbs_out_of_the_repo_is_literal(string path)
        => Assert.True(MountResolver.IsLiteralPath(_t.Root, path));

    [Fact]
    public void An_absolute_path_is_literal()
        => Assert.True(MountResolver.IsLiteralPath(_t.Root, Path.Combine(Path.GetTempPath(), "x.pdf")));

    [Fact]
    public void A_literal_path_is_split_into_the_covering_mount_and_the_rest()
    {
        var cfg = WithDocs(out _);

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../company-docs/specs/plan.md", null);

        Assert.Null(rebase.Error);
        Assert.Equal("docs", rebase.Mount);
        Assert.Equal("specs/plan.md", rebase.Path);
    }

    [Fact]
    public void A_path_no_mount_covers_names_the_verb_that_would_fix_it()
    {
        var cfg = WithDocs(out _);

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../elsewhere/plan.md", null);

        Assert.Null(rebase.Path);
        Assert.Contains("no declared mount covers it", rebase.Error);
        Assert.Contains("knit mount add", rebase.Error);
        Assert.False(rebase.Ambiguous);
    }

    [Fact]
    public void Overlapping_mounts_are_a_conflict_rather_than_a_guess()
    {
        _t.Sibling("company-docs");
        Directory.CreateDirectory(Path.Combine(_t.Sibling("company-docs"), "specs"));
        var cfg = new KnitConfig(Mounts: new()
        {
            ["docs"] = "../company-docs",
            ["specs"] = "../company-docs/specs",
        });

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../company-docs/specs/plan.md", null);

        Assert.True(rebase.Ambiguous);
        Assert.Contains("mount 'docs'", rebase.Error);
        Assert.Contains("mount 'specs'", rebase.Error);
    }

    [Fact]
    public void Naming_the_mount_resolves_an_overlap_instead_of_erroring()
    {
        Directory.CreateDirectory(Path.Combine(_t.Sibling("company-docs"), "specs"));
        var cfg = new KnitConfig(Mounts: new()
        {
            ["docs"] = "../company-docs",
            ["specs"] = "../company-docs/specs",
        });

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../company-docs/specs/plan.md", "specs");

        Assert.Null(rebase.Error);
        Assert.Equal("plan.md", rebase.Path);
    }

    [Fact]
    public void A_named_mount_that_does_not_cover_the_path_is_refused()
    {
        var cfg = WithDocs(out _);

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../elsewhere/plan.md", "docs");

        Assert.Contains("outside mount 'docs'", rebase.Error);
    }

    [Fact]
    public void An_unresolved_mount_still_claims_the_paths_under_it()
    {
        // Lexical on purpose: which mount owns a path must not flip depending
        // on whether the directory happens to exist right now.
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../never-created" });

        var rebase = MountResolver.RebaseLiteral(_t.Root, cfg, "../never-created/plan.md", null);

        Assert.Equal("docs", rebase.Mount);
        Assert.Equal("plan.md", rebase.Path);
    }

    [Fact]
    public void A_mounted_locator_resolves_to_where_it_lands()
    {
        var cfg = WithDocs(out var dir);

        var target = MountResolver.ResolveSource(_t.Root, cfg, "docs", "specs/plan.md");

        Assert.Null(target.Reason);
        Assert.Equal(Path.Combine(dir, "specs", "plan.md"), target.FullPath);
    }

    [Fact]
    public void A_legacy_escaping_path_still_resolves_without_a_mount()
    {
        // The CLI is not a trust boundary, and an un-adopted `../` path has to
        // stay readable or there is no way to see where it points.
        var target = MountResolver.ResolveSource(_t.Root, new KnitConfig(), null, "../Docs/x.pdf");

        Assert.Null(target.Reason);
        Assert.EndsWith(Path.Combine("Docs", "x.pdf"), target.FullPath);
    }

    [Fact]
    public void A_broken_mount_resolves_to_its_reason_not_a_path()
    {
        var cfg = new KnitConfig(Mounts: new() { ["docs"] = "../gone" });

        var target = MountResolver.ResolveSource(_t.Root, cfg, "docs", "plan.md");

        Assert.Equal("mount 'docs' unresolved", target.Reason);
        Assert.Equal("", target.FullPath);
    }
}
