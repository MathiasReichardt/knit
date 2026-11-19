namespace Knit.Cli.Commands;

/// <summary>
/// One install style: a skill folder name plus the instruction file its agents
/// read. <see cref="Agents"/> is only a label — agent names appear in prompts
/// and detection, never as a choice of their own (AG1).
/// </summary>
public sealed record SkillStyle(string Name, string Dir, string InstructionFile, string Agents)
{
    public string RepoRoot(string cwd) => Path.Combine(cwd, Dir, "skills");

    public string GlobalRoot() => Path.Combine(SkillStyles.Home(), Dir, "skills");

    /// <summary>Prompt label: the folder, then the agents that read it (AG6).</summary>
    public string Label => $"{Dir}/skills ({Agents})";
}

/// <summary>
/// The two install styles and the agent markers that pre-check them (AG1, AG6).
/// Every agent reads one of the two folders, so this is the whole table.
/// </summary>
public static class SkillStyles
{
    /// <summary>
    /// Points the home folder agents are looked up in somewhere else. Exists for
    /// tests above all — without it a run reads (and installs into) the
    /// developer's real <c>~/.claude</c> / <c>~/.agents</c>.
    /// </summary>
    public const string HomeVariable = "KNIT_AGENT_HOME";

    public static readonly SkillStyle Claude = new("claude", ".claude", "CLAUDE.md", "Claude Code, OpenCode");
    public static readonly SkillStyle Agents = new("agents", ".agents", "AGENTS.md", "Codex, OpenCode");

    public static readonly IReadOnlyList<SkillStyle> All = [Claude, Agents];

    public static string ValidNames => string.Join(", ", All.Select(s => s.Name));

    public static string Home()
    {
        var overridden = Environment.GetEnvironmentVariable(HomeVariable);
        return string.IsNullOrWhiteSpace(overridden)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(overridden);
    }

    private static string CodexHome()
    {
        var overridden = Environment.GetEnvironmentVariable("CODEX_HOME");
        return string.IsNullOrWhiteSpace(overridden) ? Path.Combine(Home(), ".codex") : overridden;
    }

    public static SkillStyle? Parse(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Styles whose agent leaves a marker in the repo or the home folder, in
    /// <see cref="All"/> order. Nothing found → <see cref="Claude"/>.
    /// </summary>
    public static IReadOnlyList<SkillStyle> Detect(string cwd)
    {
        var home = Home();
        var markers = new (SkillStyle Style, string Path)[]
        {
            (Claude, Path.Combine(cwd, ".claude")),
            (Claude, Path.Combine(cwd, "CLAUDE.md")),
            (Claude, Path.Combine(home, ".claude")),
            (Agents, Path.Combine(cwd, ".agents")),
            (Agents, Path.Combine(cwd, ".codex")),
            (Agents, Path.Combine(cwd, ".opencode")),
            (Agents, Path.Combine(cwd, "AGENTS.md")),
            (Agents, Path.Combine(home, ".agents")),
            (Agents, CodexHome()),
            (Agents, Path.Combine(home, ".config", "opencode")),
        };
        var found = All
            .Where(s => markers.Any(m => m.Style == s && (Directory.Exists(m.Path) || File.Exists(m.Path))))
            .ToList();
        return found.Count > 0 ? found : [Claude];
    }
}
