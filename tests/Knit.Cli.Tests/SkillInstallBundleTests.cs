using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SkillInstallBundleTests
{
    [Fact]
    public void Install_bundle_to_custom_path_writes_all_six_skills()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        var target = Path.Combine(root.Path, "skills-out");
        var r = root.Run("skill", "install", "--target", target, "--non-interactive");
        Assert.Equal(0, r.ExitCode);

        foreach (var name in new[] { "knit", "knit-cli", "knit-kb", "knit-design", "knit-work", "knit-projects" })
        {
            var path = Path.Combine(target, name, "SKILL.md");
            Assert.True(File.Exists(path), $"missing {path}");
            var content = File.ReadAllText(path);
            Assert.Contains($"name: {name}", content);
        }
    }

    [Fact]
    public void Install_writes_every_file_a_skill_links_and_no_link_dangles()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var target = Path.Combine(root.Path, "skills-out");
        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);

        var references = Directory.GetFiles(Path.Combine(target, "knit-cli", "references"), "*.md");
        Assert.NotEmpty(references);

        foreach (var file in Directory.GetFiles(target, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(target, file).Replace('\\', '/');
            var isReference = rel.Contains("/references/");
            foreach (var link in RelativeMdLinks(File.ReadAllText(file)))
            {
                // Reference files are leaves: one hop from their SKILL.md, no links onward.
                Assert.False(isReference, $"{rel} links {link}");
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, link));
                Assert.True(File.Exists(resolved), $"{rel}: dangling link {link}");
                var targetRel = Path.GetRelativePath(target, resolved).Replace('\\', '/');
                if (targetRel.Contains("/references/"))
                    Assert.Equal(targetRel.Split('/')[0], rel.Split('/')[0]);
            }
        }

        var core = File.ReadAllText(Path.Combine(target, "knit-cli", "SKILL.md"));
        foreach (var reference in references)
            Assert.Contains($"(references/{Path.GetFileName(reference)})", core);
    }

    [Fact]
    public void The_bundle_cites_skills_by_name_without_a_slash()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var target = Path.Combine(root.Path, "skills-out");
        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);

        foreach (var file in Directory.GetFiles(target, "*.md", SearchOption.AllDirectories))
        {
            var slash = Regex.Match(File.ReadAllText(file), @"(?<![\w.])/knit(-[a-z]+)?\b");
            Assert.False(slash.Success, $"{Path.GetRelativePath(target, file)}: '{slash.Value}' — cite as `knit-cli` skill");
        }
    }

    [Fact]
    public void Update_removes_a_reference_file_the_bundle_no_longer_ships()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "project", "--non-interactive").ExitCode);

        var stale = Path.Combine(root.Path, ".claude", "skills", "knit-cli", "references", "retired.md");
        File.WriteAllText(stale, "# gone");

        Assert.Equal(0, root.Run("skill", "update", "--non-interactive").ExitCode);
        Assert.False(File.Exists(stale));
    }

    private static IEnumerable<string> RelativeMdLinks(string markdown)
    {
        var prose = Regex.Replace(markdown, "```.*?```", "", RegexOptions.Singleline);
        prose = Regex.Replace(prose, "`[^`\n]*`", "");
        return Regex.Matches(prose, @"\]\(([^)#\s:]+\.md)(#[^)]*)?\)")
            .Select(m => m.Groups[1].Value);
    }

    [Fact]
    public void Install_non_interactive_without_target_defaults_to_project_scope()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        var r = root.Run("skill", "install", "--non-interactive");
        Assert.Equal(0, r.ExitCode);

        // installs to the single project-scope destination (proving it did not
        // silently pick global ~/.claude/skills, which needs an explicit target)
        var projectSkills = Path.Combine(root.Path, ".claude", "skills");
        foreach (var name in new[] { "knit", "knit-cli", "knit-kb", "knit-design", "knit-work", "knit-projects" })
            Assert.True(File.Exists(Path.Combine(projectSkills, name, "SKILL.md")), $"missing {name}");
    }

    [Fact]
    public void Install_stamps_a_bundle_marker_with_the_running_version()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var target = Path.Combine(root.Path, "skills-out");
        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);

        var marker = Path.Combine(target, ".knit-bundle.json");
        Assert.True(File.Exists(marker));
        var json = File.ReadAllText(marker);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"installed_at\"", json);
    }

    [Fact]
    public void Install_skips_the_per_repo_copy_when_a_global_bundle_exists()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "global", "--non-interactive").ExitCode);

        var r = root.Run("skill", "install", "--non-interactive");
        Assert.Equal(0, r.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".claude", "skills")));
        Assert.Contains("global skill bundle already installed", r.Stdout);
    }

    [Fact]
    public void Explicit_project_target_still_pins_a_local_copy_alongside_a_global_bundle()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "global", "--non-interactive").ExitCode);

        Assert.Equal(0, root.Run("skill", "install", "--target", "project", "--non-interactive").ExitCode);
        Assert.True(File.Exists(Path.Combine(root.Path, ".claude", "skills", "knit", "SKILL.md")));
    }

    [Fact]
    public void Update_check_reports_a_stale_global_bundle_and_update_refreshes_it()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "global", "--non-interactive").ExitCode);

        var marker = Path.Combine(root.GlobalSkillsPath, ".knit-bundle.json");
        File.WriteAllText(marker, "{\"version\":\"0.0.1-ancient\",\"installed_at\":\"2000-01-01T00:00:00.0000000+00:00\"}");

        var check = root.Run("skill", "update", "--global", "--check");
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("0.0.1-ancient", check.Stdout);
        Assert.Contains("outdated", check.Stdout);

        // the stale stamp also steers the non-interactive install notice
        Assert.Contains("out of date", root.Run("skill", "install", "--non-interactive").Stdout);

        Assert.Equal(0, root.Run("skill", "update", "--global", "--non-interactive").ExitCode);
        Assert.DoesNotContain("0.0.1-ancient", File.ReadAllText(marker));
        Assert.Contains("up to date", root.Run("skill", "update", "--global", "--check").Stdout);
    }

    [Fact]
    public void A_bundle_newer_than_the_binary_is_flagged_not_called_outdated()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "global", "--non-interactive").ExitCode);
        File.WriteAllText(Path.Combine(root.GlobalSkillsPath, ".knit-bundle.json"),
            "{\"version\":\"999.0.0\",\"installed_at\":\"2000-01-01T00:00:00.0000000+00:00\"}");

        var check = root.Run("skill", "update", "--global", "--check");
        Assert.Contains("skill bundle newer than this knit", check.Stdout);
        Assert.DoesNotContain("outdated", check.Stdout);

        var install = root.Run("skill", "install", "--non-interactive");
        Assert.Contains("skill bundle newer than this knit", install.Stdout);
        Assert.DoesNotContain("out of date", install.Stdout);

        var update = root.Run("skill", "update", "--global", "--non-interactive");
        Assert.Equal(0, update.ExitCode);
        Assert.Contains("downgrading skill bundle", update.Stdout);
    }

    [Fact]
    public void Update_without_an_installed_bundle_fails()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        var r = root.Run("skill", "update", "--non-interactive");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no skill bundle", r.Stderr);
    }

    [Fact]
    public void Install_refuses_overwrite_without_force()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var target = Path.Combine(root.Path, "skills-out");

        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);
        var r = root.Run("skill", "install", "--target", target, "--non-interactive");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void Install_with_force_overwrites()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var target = Path.Combine(root.Path, "skills-out");

        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive", "--force").ExitCode);
    }
}
