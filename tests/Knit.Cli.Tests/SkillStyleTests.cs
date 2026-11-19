using Knit.Cli.Commands;

namespace Knit.Cli.Tests;

/// <summary>
/// Two install styles reach every agent: <c>claude</c> (<c>.claude/skills</c>)
/// and <c>agents</c> (<c>.agents/skills</c>). Detection pre-checks them, and
/// `update` refreshes every bundle it finds, so no folder drifts.
/// </summary>
[Collection(nameof(CliCollection))]
public class SkillStyleTests
{
    private static readonly string[] BundleNames =
        ["knit", "knit-cli", "knit-kb", "knit-design", "knit-work", "knit-projects"];

    private static TempKnitRoot Inited()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        return root;
    }

    private static string RepoAgents(TempKnitRoot root) => Path.Combine(root.Path, ".agents", "skills");
    private static string RepoClaude(TempKnitRoot root) => Path.Combine(root.Path, ".claude", "skills");

    // Redirected output wraps at 80 columns, and a temp path is longer than that.
    private static string Flat(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static void Stamp(string skillsRoot, string version) =>
        File.WriteAllText(Path.Combine(skillsRoot, ".knit-bundle.json"),
            $"{{\"version\":\"{version}\",\"installed_at\":\"2000-01-01T00:00:00.0000000+00:00\"}}");

    [Fact]
    public void Style_agents_writes_the_agents_folder_with_references()
    {
        using var root = Inited();

        Assert.Equal(0, root.Run("skill", "install", "--style", "agents", "--non-interactive").ExitCode);

        foreach (var name in BundleNames)
            Assert.True(File.Exists(Path.Combine(RepoAgents(root), name, "SKILL.md")), $"missing {name}");
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(RepoAgents(root), "knit-cli", "references"), "*.md"));
        Assert.False(Directory.Exists(RepoClaude(root)));
    }

    [Fact]
    public void Both_styles_install_both_folders()
    {
        using var root = Inited();

        Assert.Equal(0, root.Run("skill", "install", "--style", "claude", "--style", "agents", "--non-interactive").ExitCode);

        Assert.True(File.Exists(Path.Combine(RepoClaude(root), "knit", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(RepoAgents(root), "knit", "SKILL.md")));
    }

    [Fact]
    public void Style_with_target_global_writes_the_home_agents_folder()
    {
        using var root = Inited();

        Assert.Equal(0, root.Run("skill", "install", "--style", "agents", "--target", "global", "--non-interactive").ExitCode);

        Assert.True(File.Exists(Path.Combine(root.GlobalAgentsSkillsPath, "knit", "SKILL.md")));
        Assert.False(Directory.Exists(RepoAgents(root)));
    }

    [Fact]
    public void Unknown_style_fails_with_the_valid_values()
    {
        using var root = Inited();

        var r = root.Run("skill", "install", "--style", "codex", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("claude, agents", r.Stderr);
    }

    [Fact]
    public void Detection_prechecks_both_folders_for_claude_and_codex_markers()
    {
        using var root = new TempKnitRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, ".claude"));
        Directory.CreateDirectory(Path.Combine(root.Path, ".codex"));

        var detected = SkillStyles.Detect(root.Path);

        Assert.Equal([SkillStyles.Claude, SkillStyles.Agents], detected);
        Assert.Equal(".claude/skills (Claude Code, OpenCode)", SkillStyles.Claude.Label);
        Assert.Equal(".agents/skills (Codex, OpenCode)", SkillStyles.Agents.Label);
    }

    [Fact]
    public void Detection_reads_user_level_markers_and_falls_back_to_claude()
    {
        using var root = new TempKnitRoot();
        Assert.Equal([SkillStyles.Claude], SkillStyles.Detect(root.Path));

        Directory.CreateDirectory(Path.Combine(root.AgentHome, ".config", "opencode"));
        Assert.Equal([SkillStyles.Agents], SkillStyles.Detect(root.Path));
    }

    [Fact]
    public void No_tty_install_writes_the_prechecked_folders_and_names_them()
    {
        using var root = Inited();
        Directory.CreateDirectory(Path.Combine(root.Path, ".claude"));
        Directory.CreateDirectory(Path.Combine(root.Path, ".codex"));

        var r = root.Run("skill", "install", "--non-interactive");

        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(Path.Combine(RepoClaude(root), "knit", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(RepoAgents(root), "knit", "SKILL.md")));
        Assert.Contains(Flat(RepoClaude(root)), Flat(r.Stdout));
        Assert.Contains(Flat(RepoAgents(root)), Flat(r.Stdout));
    }

    [Fact]
    public void Bare_update_refreshes_every_bundle_one_line_each()
    {
        using var root = Inited();
        Assert.Equal(0, root.Run("skill", "install", "--target", "project", "--style", "claude", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("skill", "install", "--target", "global", "--style", "agents", "--non-interactive").ExitCode);
        Stamp(RepoClaude(root), "0.0.1-ancient");
        Stamp(root.GlobalAgentsSkillsPath, "0.0.2-ancient");

        var r = root.Run("skill", "update", "--non-interactive");

        Assert.Equal(0, r.ExitCode);
        var updates = Flat(r.Stdout).Split("updated", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, updates.Length);
        Assert.Single(updates, u => u.StartsWith(Flat(RepoClaude(root))) && u.Contains("0.0.1-ancient→"));
        Assert.Single(updates, u => u.StartsWith(Flat(root.GlobalAgentsSkillsPath)) && u.Contains("0.0.2-ancient→"));
        Assert.DoesNotContain("ancient", File.ReadAllText(Path.Combine(root.GlobalAgentsSkillsPath, ".knit-bundle.json")));
    }

    [Fact]
    public void Update_style_and_global_narrow_the_refresh()
    {
        using var root = Inited();
        Assert.Equal(0, root.Run("skill", "install", "--style", "claude", "--style", "agents", "--non-interactive").ExitCode);
        Stamp(RepoClaude(root), "0.0.1-ancient");
        Stamp(RepoAgents(root), "0.0.1-ancient");

        Assert.Equal(0, root.Run("skill", "update", "--style", "agents", "--non-interactive").ExitCode);

        Assert.Contains("ancient", File.ReadAllText(Path.Combine(RepoClaude(root), ".knit-bundle.json")));
        Assert.DoesNotContain("ancient", File.ReadAllText(Path.Combine(RepoAgents(root), ".knit-bundle.json")));
        Assert.NotEqual(0, root.Run("skill", "update", "--global", "--non-interactive").ExitCode);
    }

    [Fact]
    public void Update_takes_a_custom_target()
    {
        using var root = Inited();
        var target = Path.Combine(root.Path, "skills-out");
        Assert.Equal(0, root.Run("skill", "install", "--target", target, "--non-interactive").ExitCode);
        Stamp(target, "0.0.1-ancient");

        var r = root.Run("skill", "update", "--target", target, "--non-interactive");

        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("ancient", File.ReadAllText(Path.Combine(target, ".knit-bundle.json")));
        Assert.NotEqual(0, root.Run("skill", "update", "--global", "--target", target).ExitCode);
    }

    [Fact]
    public void Init_flags_an_outdated_agents_bundle()
    {
        using var root = Inited();
        Assert.Equal(0, root.Run("skill", "install", "--style", "agents", "--non-interactive").ExitCode);
        Stamp(RepoAgents(root), "0.0.1-ancient");

        var r = root.Run("init", "--handle", "a", "--display", "A");

        Assert.Contains("skill bundle outdated", r.Stdout);
        Assert.Contains(Flat(RepoAgents(root)), Flat(r.Stdout));
    }

    [Fact]
    public void Init_offers_the_agents_folder_beside_an_existing_claude_bundle()
    {
        using var root = Inited();
        Assert.Equal(0, root.Run("skill", "install", "--style", "claude", "--non-interactive").ExitCode);
        Directory.CreateDirectory(Path.Combine(root.Path, ".codex"));

        var r = root.Run("init", "--handle", "a", "--display", "A", "--skills", "project");

        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(Path.Combine(RepoAgents(root), "knit", "SKILL.md")));
        Assert.Contains(Flat(RepoAgents(root)), Flat(r.Stdout));
        Assert.DoesNotContain(Flat(RepoClaude(root)), Flat(r.Stdout));
    }

    [Fact]
    public void Init_rejects_an_unknown_skills_value_before_scaffolding()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--handle", "a", "--display", "A", "--skills", "bogus");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("global, project, skip", r.Stderr);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit")));
    }

    [Fact]
    public void Init_skills_skip_installs_nothing_and_asks_nothing()
    {
        using var root = new TempKnitRoot();

        var r = root.RunWithInput("", "init", "--handle", "a", "--display", "A", "--skills", "skip");

        Assert.Equal(0, r.ExitCode);
        Assert.False(Directory.Exists(RepoClaude(root)));
        Assert.False(Directory.Exists(root.GlobalSkillsPath));
    }
}
