namespace Knit.Cli.Tests;

/// <summary>
/// A repo scaffolded without skills hands the agent a tracker it cannot drive,
/// so `knit init` reports or offers the bundle. Without a terminal the offer
/// degrades to the explicit `--skills` value — never a silent write outside the
/// repo.
/// </summary>
[Collection(nameof(CliCollection))]
public class InitSkillOfferTests
{
    private static readonly string[] BundleNames =
        ["knit", "knit-cli", "knit-kb", "knit-design", "knit-work", "knit-projects"];

    [Fact]
    public void Init_without_a_tty_installs_no_skills_by_default()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--handle", "a", "--display", "A");

        Assert.Equal(0, r.ExitCode);
        Assert.False(Directory.Exists(root.GlobalSkillsPath));
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".claude", "skills")));
        Assert.Contains("knit skill install", r.Stdout);
    }

    [Fact]
    public void Init_with_skills_project_writes_the_bundle_into_the_repo()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--handle", "a", "--display", "A", "--skills", "project");

        Assert.Equal(0, r.ExitCode);
        foreach (var name in BundleNames)
            Assert.True(File.Exists(Path.Combine(root.Path, ".claude", "skills", name, "SKILL.md")), $"missing {name}");
        Assert.False(Directory.Exists(root.GlobalSkillsPath));
        // the bundle is there — the first-steps list must not still tell the user to install it
        Assert.DoesNotContain("knit skill install", r.Stdout);
    }

    [Fact]
    public void Init_with_skills_global_writes_the_home_bundle()
    {
        using var root = new TempKnitRoot();

        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--skills", "global").ExitCode);

        foreach (var name in BundleNames)
            Assert.True(File.Exists(Path.Combine(root.GlobalSkillsPath, name, "SKILL.md")), $"missing {name}");
        Assert.True(File.Exists(Path.Combine(root.GlobalSkillsPath, ".knit-bundle.json")));
    }

    [Fact]
    public void Init_stays_silent_about_a_current_bundle_and_installs_nothing()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--skills", "global").ExitCode);

        var r = root.Run("init", "--handle", "a", "--display", "A", "--skills", "project");

        Assert.Equal(0, r.ExitCode);
        // an up-to-date bundle is not worth a line in a new user's first output
        Assert.DoesNotContain("skill bundle", r.Stdout);
        Assert.DoesNotContain("knit skill install", r.Stdout);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".claude", "skills")),
            "a global bundle already covers this repo — init must not pin a second copy");
    }

    [Fact]
    public void Init_flags_a_stale_bundle()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--skills", "global").ExitCode);
        File.WriteAllText(Path.Combine(root.GlobalSkillsPath, ".knit-bundle.json"),
            "{\"version\":\"0.0.1-ancient\",\"installed_at\":\"2000-01-01T00:00:00.0000000+00:00\"}");

        var r = root.Run("init", "--handle", "a", "--display", "A");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("0.0.1-ancient", r.Stdout);
        Assert.Contains("skill bundle outdated", r.Stdout);
        Assert.Contains("knit skill update --global", r.Stdout);
    }

    [Fact]
    public void Init_flags_a_bundle_newer_than_the_binary()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--skills", "global").ExitCode);
        File.WriteAllText(Path.Combine(root.GlobalSkillsPath, ".knit-bundle.json"),
            "{\"version\":\"999.0.0\",\"installed_at\":\"2000-01-01T00:00:00.0000000+00:00\"}");

        var r = root.Run("init", "--handle", "a", "--display", "A");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("skill bundle newer than this knit", r.Stdout);
        Assert.DoesNotContain("skill bundle outdated", r.Stdout);
    }
}
