using System.Text.RegularExpressions;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// The `## Knit` section in the repo's agent instruction files (AG2, AG6):
/// one copy per file, never an import, refreshed when it differs from the
/// binary's text. The section runs to the next `#` / `##` heading or the end.
/// </summary>
internal static partial class AgentsPointer
{
    private const string Block = """
        ## Knit

        This repo uses Knit (issue tracker + knowledge base).
        - Tracker: `.knit/items/` (created on first `knit add`) — query live
          with `knit list` / `knit show`
        - KB: `.knit/kb/` (created on first use, entry point `kb-index.md`)
        - Skills available — install locally with `knit skill install`.
          Start with the `knit` skill for an overview of what Knit is and
          which skill to use for which workflow.
        """;

    [GeneratedRegex(@"^## Knit[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex KnitHeading();

    [GeneratedRegex(@"^#{1,2}[ \t]", RegexOptions.Multiline)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"(?<![\w@/])@(\./)?AGENTS\.md(?![\w.])")]
    private static partial Regex ImportsAgents();

    private enum State { Missing, Current, Outdated }

    /// <summary>The values `init --agents-file` takes, checked before init writes anything.</summary>
    internal static int? Validate(string? value) =>
        value is null || Is(value, "project") || Is(value, "skip")
            ? null
            : Out.Error($"unknown --agents-file '{value}' — valid: project, skip", ErrorCode.BadArgument);

    /// <summary>
    /// `knit init` hook. An explicit `--agents-file` answers the prompt; no
    /// terminal and no flag writes nothing and only names an outdated section.
    /// </summary>
    internal static void OfferOnInit(string cwd, bool canPrompt, string? agentsFile)
    {
        if (agentsFile is not null && Is(agentsFile, "skip")) return;

        var claude = Path.Combine(cwd, SkillStyles.Claude.InstructionFile);
        var agents = Path.Combine(cwd, SkillStyles.Agents.InstructionFile);
        // A CLAUDE.md that imports AGENTS.md already reads its section there.
        var redirected = File.Exists(claude) && ImportsAgents().IsMatch(File.ReadAllText(claude));
        string[] offered = redirected ? [agents] : [claude, agents];
        var prechecked = SkillStyles.Detect(cwd)
            .Select(s => Path.Combine(cwd, s.InstructionFile))
            .Select(p => redirected && p == claude ? agents : p)
            .Distinct()
            .ToList();

        // A repo that already has an instruction file keeps to it; another
        // agent's file is a separate question, asked only when that agent is found.
        var existing = new[] { claude, agents }
            .Where(File.Exists)
            .Select(p => redirected && p == claude ? agents : p)
            .Distinct()
            .ToList();

        if (agentsFile is not null)
        {
            foreach (var path in existing.Count > 0 ? existing : prechecked)
                Write(path, StateOf(path), announceCurrent: true);
            return;
        }

        if (!canPrompt)
        {
            foreach (var path in offered.Where(p => StateOf(p) == State.Outdated))
                AnsiConsole.MarkupLine(
                    $"[yellow]Knit section outdated[/] [dim]{Markup.Escape(path)} — refresh: knit init --agents-file project[/]");
            return;
        }

        foreach (var path in offered.Where(p => StateOf(p) == State.Outdated))
            if (AnsiConsole.Confirm(
                    $"The Knit section in [yellow]{Markup.Escape(path)}[/] differs from this knit version — replace it?",
                    defaultValue: true))
                Write(path, State.Outdated, announceCurrent: false);

        var missing = offered.Where(p => StateOf(p) == State.Missing).ToList();
        if (existing.Count == 0)
        {
            if (!prechecked.Any(missing.Contains)) return;
            var prompt = new MultiSelectionPrompt<string>()
                .Title("Add a Knit pointer to which instruction files?")
                .NotRequired()
                .InstructionsText("[dim](space toggles, enter accepts)[/]")
                .AddChoices(missing.Select(p => Label(p, agents)));
            foreach (var path in prechecked.Where(missing.Contains))
                prompt.Select(Label(path, agents));
            var picked = AnsiConsole.Prompt(prompt);
            foreach (var path in missing.Where(p => picked.Contains(Label(p, agents))))
                Write(path, State.Missing, announceCurrent: false);
            return;
        }

        foreach (var path in missing.Where(existing.Contains))
            if (AnsiConsole.Confirm($"Add a Knit pointer to [yellow]{Markup.Escape(path)}[/]?", defaultValue: true))
                Write(path, State.Missing, announceCurrent: false);

        var others = missing.Where(p => !existing.Contains(p) && prechecked.Contains(p)).ToList();
        if (others.Count > 0 && AnsiConsole.Confirm(
                $"Also add a Knit pointer for other agents — {Markup.Escape(string.Join(", ", others.Select(p => Label(p, agents))))}?",
                defaultValue: false))
            foreach (var path in others)
                Write(path, State.Missing, announceCurrent: false);
    }

    private static string Label(string path, string agents) => path == agents
        ? $"{Path.GetFileName(path)} (Codex, OpenCode)"
        : $"{Path.GetFileName(path)} (Claude Code; OpenCode only without AGENTS.md)";

    private static bool Is(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static State StateOf(string path)
    {
        if (!File.Exists(path)) return State.Missing;
        var text = File.ReadAllText(path);
        if (Section(text) is not { } section) return State.Missing;
        var current = text[section.Start..section.End].ReplaceLineEndings("\n").TrimEnd();
        return current == Block.ReplaceLineEndings("\n") ? State.Current : State.Outdated;
    }

    private static (int Start, int End)? Section(string text)
    {
        var heading = KnitHeading().Match(text);
        if (!heading.Success) return null;
        var next = SectionHeading().Match(text, heading.Index + heading.Length);
        return (heading.Index, next.Success ? next.Index : text.Length);
    }

    private static void Write(string path, State state, bool announceCurrent)
    {
        if (state == State.Current)
        {
            if (announceCurrent)
                AnsiConsole.MarkupLine($"[dim]Knit pointer already present in {Markup.Escape(path)}[/]");
            return;
        }

        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = Block.ReplaceLineEndings(nl) + nl;
        if (state == State.Outdated)
        {
            var (start, end) = Section(text)!.Value;
            var after = text[end..];
            File.WriteAllText(path, text[..start] + block + (after.Length > 0 ? nl + after : ""));
            AnsiConsole.MarkupLine($"[green]replaced the Knit section in[/] {Markup.Escape(path)}");
            return;
        }

        var lead = text.Length == 0 ? "" : text.EndsWith('\n') ? nl : nl + nl;
        File.WriteAllText(path, text + lead + block);
        AnsiConsole.MarkupLine($"[green]added pointer to[/] {Markup.Escape(path)}");
    }
}
