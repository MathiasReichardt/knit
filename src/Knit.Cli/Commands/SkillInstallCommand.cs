using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Knit.Cli.Output;
using Knit.Core;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// Stamp written beside an installed bundle (<c>&lt;skills-root&gt;/.knit-bundle.json</c>).
/// Kept out of the <c>SKILL.md</c> files on purpose — those follow the Agent
/// Skills frontmatter contract and must not carry knit-private fields.
/// </summary>
public sealed record SkillBundleMarker
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("installed_at")]
    public string InstalledAt { get; init; } = "";
}

[JsonSerializable(typeof(SkillBundleMarker))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class SkillBundleMarkerJsonContext : JsonSerializerContext;

public static class SkillInstallCommand
{
    private const string ResourcePrefix = "Knit.Cli.EmbeddedSkill/";
    private const string RouterSkill = "knit";
    private const string ReferencesDir = "references";
    private const string MarkerFile = ".knit-bundle.json";

    /// <summary>
    /// Embedded bundle files, keyed by their path under <c>skills/</c> with
    /// <c>/</c> separators (<c>knit-cli/SKILL.md</c>, <c>knit-cli/references/root.md</c>).
    /// </summary>
    private static IEnumerable<(string Path, string Resource)> BundleFiles() =>
        typeof(SkillInstallCommand).Assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(r => (Path: r[ResourcePrefix.Length..].Replace('\\', '/'), Resource: r))
            .OrderBy(f => f.Path, StringComparer.Ordinal);

    public static Command Build()
    {
        var cmd = new Command("skill", "Manage the embedded agent skill bundle");

        var install = new Command("install", "Install the six-skill bundle (knit, knit-cli, knit-kb, knit-design, knit-work, knit-projects)");
        var targetOpt = new Option<string?>("--target") { Description = "'project', 'global', or a custom path" };
        var styleOpt = StyleOption();
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite existing files" };
        var nonInteractiveOpt = new Option<bool>("--non-interactive") { Description = "Fail if any prompt would be needed" };
        install.Options.Add(targetOpt);
        install.Options.Add(styleOpt);
        install.Options.Add(forceOpt);
        install.Options.Add(nonInteractiveOpt);
        install.SetAction(parse => RunInstall(
            parse.GetValue(targetOpt),
            parse.GetValue(styleOpt),
            parse.GetValue(forceOpt),
            parse.GetValue(nonInteractiveOpt)));
        cmd.Subcommands.Add(install);

        var update = new Command("update",
            "Refresh every installed bundle (repo and global, .claude/skills and .agents/skills) to this binary's version");
        var globalOpt = new Option<bool>("--global") { Description = "Only the global bundles" };
        var updateTargetOpt = new Option<string?>("--target") { Description = "Only 'project', 'global', or a custom path" };
        var updateStyleOpt = StyleOption();
        var checkOpt = new Option<bool>("--check") { Description = "Report installed vs running version; write nothing" };
        var updateNonInteractiveOpt = new Option<bool>("--non-interactive") { Description = "Fail if any prompt would be needed" };
        update.Options.Add(globalOpt);
        update.Options.Add(updateTargetOpt);
        update.Options.Add(updateStyleOpt);
        update.Options.Add(checkOpt);
        update.Options.Add(updateNonInteractiveOpt);
        update.SetAction(parse => RunUpdate(
            parse.GetValue(globalOpt),
            parse.GetValue(updateTargetOpt),
            parse.GetValue(updateStyleOpt),
            parse.GetValue(checkOpt),
            parse.GetValue(updateNonInteractiveOpt)));
        cmd.Subcommands.Add(update);

        return cmd;
    }

    private static Option<string[]> StyleOption() => new("--style")
    {
        Description = "Skill folder style, repeatable: claude (.claude/skills) | agents (.agents/skills)",
    };

    private static string Cwd => Directory.GetCurrentDirectory();

    private static bool HasBundle(string root) =>
        File.Exists(Path.Combine(root, RouterSkill, "SKILL.md"));

    private static string? ReadMarkerVersion(string root)
    {
        var path = Path.Combine(root, MarkerFile);
        if (!File.Exists(path)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize(
                File.ReadAllText(path), SkillBundleMarkerJsonContext.Default.SkillBundleMarker);
            return string.IsNullOrWhiteSpace(marker?.Version) ? null : marker.Version;
        }
        catch
        {
            // An unreadable stamp is a missing stamp — never a reason to fail
            // an install; bundles installed before the stamp existed hit this.
            return null;
        }
    }

    private static string RunningVersion() => VersionInfo.Get(typeof(SkillInstallCommand).Assembly);

    private enum BundleAge { Current, Older, Newer }

    // Numeric compare: string order puts 0.9.0 after 0.10.0. An unstamped or
    // unparseable bundle counts as older, the case `skill update` fixes.
    private static BundleAge Age(string? installed, string running)
    {
        if (installed == running) return BundleAge.Current;
        return TryCore(installed, out var i) && TryCore(running, out var r) && i > r
            ? BundleAge.Newer
            : BundleAge.Older;
    }

    private static bool TryCore(string? version, out Version core)
    {
        core = new Version();
        if (version is null) return false;
        var dash = version.IndexOf('-');
        return Version.TryParse(dash > 0 ? version[..dash] : version, out core!);
    }

    // The skills may name commands this binary lacks; agents then fail on them.
    private static void WarnNewer(string root, string installed, string running) =>
        AnsiConsole.MarkupLine(
            $"[yellow]skill bundle newer than this knit[/] [dim]{Markup.Escape(root)} — installed {Markup.Escape(installed)}, running {Markup.Escape(running)}; update the knit binary[/]");

    private static int? ParseStyles(string[]? names, out List<SkillStyle> styles)
    {
        styles = [];
        foreach (var name in names ?? [])
        {
            if (SkillStyles.Parse(name) is not { } style)
                return Out.Error($"unknown --style '{name}' — valid: {SkillStyles.ValidNames}", ErrorCode.BadArgument);
            if (!styles.Contains(style)) styles.Add(style);
        }
        return null;
    }

    private static bool IsScope(string? value, string scope) =>
        string.Equals(value, scope, StringComparison.OrdinalIgnoreCase);

    private static string Root(SkillStyle style, bool global) => global ? style.GlobalRoot() : style.RepoRoot(Cwd);

    private const string ScopeRepo = "this repo — pins this version";
    private const string ScopeGlobal = "global — every repo";
    private const string ScopeCustom = "custom path";
    private const string ScopeSkip = "skip (install later with `knit skill install`)";

    /// <summary>
    /// AG6's destination prompt: the skill folders, labelled with the agents
    /// that read them and pre-checked from detection, then the scope. An empty
    /// selection is a skip.
    /// </summary>
    private static (List<SkillStyle> Styles, string Scope) PromptDestinations(
        IReadOnlyList<SkillStyle> offered, IReadOnlyList<SkillStyle> prechecked, params string[] scopes)
    {
        var folders = new MultiSelectionPrompt<string>()
            .Title("Install the Knit skills into which folders?")
            .NotRequired()
            .InstructionsText("[dim](space toggles, enter accepts)[/]")
            .AddChoices(offered.Select(s => s.Label));
        foreach (var style in prechecked.Where(offered.Contains))
            folders.Select(style.Label);
        var picked = AnsiConsole.Prompt(folders);
        var styles = offered.Where(s => picked.Contains(s.Label)).ToList();
        if (styles.Count == 0) return (styles, ScopeSkip);
        var scope = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Where?").AddChoices(scopes));
        return (styles, scope);
    }

    /// <summary>
    /// `knit init` hook: a fresh repo whose agent has no skills gets a tracker
    /// it cannot drive. Reports every installed bundle that is not current, and
    /// offers the pre-checked folders that have none yet. Returns true when every
    /// pre-checked folder has a bundle afterwards.
    /// </summary>
    internal static bool OfferOnInit(bool canPrompt, string? skills)
    {
        var running = RunningVersion();

        // An installed, current bundle says nothing: init's output is a new
        // user's first impression, and "everything is fine" is noise.
        foreach (var inGlobal in new[] { true, false })
        foreach (var style in SkillStyles.All)
        {
            var root = Root(style, inGlobal);
            if (!HasBundle(root)) continue;
            var installed = ReadMarkerVersion(root);
            var age = Age(installed, running);
            if (age == BundleAge.Newer)
                WarnNewer(root, installed!, running);
            else if (age == BundleAge.Older)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]skill bundle outdated[/] [dim]{Markup.Escape(root)} — installed {Markup.Escape(installed ?? "unknown")}, running {Markup.Escape(running)}[/]");
                AnsiConsole.MarkupLine($"[dim]  refresh: knit skill update{(inGlobal ? " --global" : "")}[/]");
            }
        }

        var detected = SkillStyles.Detect(Cwd);
        var missing = detected.Where(s => !HasBundle(s.RepoRoot(Cwd)) && !HasBundle(s.GlobalRoot())).ToList();
        if (missing.Count == 0) return true;

        List<SkillStyle> styles = missing;
        bool global;
        if (skills is not null)
        {
            // Asked for by name: answers the prompt, in a terminal or not.
            if (IsScope(skills, "skip")) return false;
            global = IsScope(skills, "global");
        }
        else if (!canPrompt)
        {
            // Same default as the agents-file pointer: write nothing outside
            // what the caller asked for.
            return false;
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("No Knit [bold]agent skills[/] for "
                                   + Markup.Escape(string.Join(", ", missing.Select(s => s.Label)))
                                   + " — your AI agent won't know how to drive Knit.");
            var offered = SkillStyles.All.Where(s => !HasBundle(s.RepoRoot(Cwd)) && !HasBundle(s.GlobalRoot())).ToList();
            string scope;
            (styles, scope) = PromptDestinations(offered, missing, ScopeGlobal, ScopeRepo, ScopeSkip);
            if (scope == ScopeSkip) return false;
            global = scope == ScopeGlobal;
        }

        foreach (var style in styles)
            if (Install(Root(style, global), force: true, nonInteractive: !canPrompt) != 0)
                return false;
        return missing.All(s => styles.Contains(s));
    }

    /// <summary>The values `init --skills` takes, checked before init writes anything.</summary>
    internal static int? ValidateSkillsValue(string? skills) =>
        skills is null || new[] { "global", "project", "skip" }.Any(v => IsScope(skills, v))
            ? null
            : Out.Error($"unknown --skills '{skills}' — valid: global, project, skip", ErrorCode.BadArgument);

    private static int RunInstall(string? target, string[]? styleNames, bool force, bool nonInteractive)
    {
        if (ParseStyles(styleNames, out var styles) is { } bad) return bad;
        var canPrompt = !nonInteractive && !Console.IsInputRedirected;

        if (!string.IsNullOrEmpty(target) && !IsScope(target, "project") && !IsScope(target, "global"))
        {
            if (styles.Count > 0)
                return Out.Error("--target <path> names one folder and --style picks folders by style — pass one",
                    ErrorCode.BadArgument);
            return Install(Path.GetFullPath(target), force, nonInteractive);
        }

        bool? global = target is null ? null : IsScope(target, "global");
        var chosen = styles.Count > 0 ? styles : SkillStyles.Detect(Cwd).ToList();

        // A global bundle already covers every repo — a per-repo copy only
        // pins a version. Skip unless the caller asked for a scope explicitly
        // (or forced the write).
        if (global is null && !force)
        {
            var running = RunningVersion();
            var covered = chosen.Where(s => HasBundle(s.GlobalRoot())).ToList();
            foreach (var style in covered)
            {
                var root = style.GlobalRoot();
                var installed = ReadMarkerVersion(root);
                AnsiConsole.MarkupLine($"[yellow]global skill bundle already installed[/] [dim]{Markup.Escape(root)}[/]");
                AnsiConsole.MarkupLine($"[dim]  installed: {Markup.Escape(installed ?? "unknown")}   running: {Markup.Escape(running)}[/]");
                if (Age(installed, running) == BundleAge.Newer)
                    WarnNewer(root, installed!, running);
            }

            if (covered.Count > 0 && covered.Count == chosen.Count)
            {
                var anyOlder = covered.Any(s => Age(ReadMarkerVersion(s.GlobalRoot()), running) == BundleAge.Older
                                                && ReadMarkerVersion(s.GlobalRoot()) is not null);
                var anyNewer = covered.Any(s => Age(ReadMarkerVersion(s.GlobalRoot()), running) == BundleAge.Newer);
                if (!canPrompt)
                {
                    AnsiConsole.MarkupLine("[dim]agents discover global skills in every repo — skipping the per-repo install (0 files written)[/]");
                    if (!anyNewer)
                        AnsiConsole.MarkupLine(anyOlder
                            ? "[dim]bundle is out of date; refresh it with `knit skill update --global`[/]"
                            : "[dim]pass --target project to pin a local copy anyway[/]");
                    return 0;
                }

                const string useGlobal = "use the global bundle (install nothing)";
                const string updateGlobal = "update the global bundle";
                const string pinHere = "install here anyway (pin this repo)";
                var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .AddChoices(useGlobal, $"{updateGlobal} to {running}", pinHere));
                if (choice == useGlobal) return 0;
                if (choice != pinHere)
                    return InstallAll(covered, global: true, force: true, nonInteractive);
                global = false;
            }
            else if (covered.Count > 0)
            {
                chosen = chosen.Except(covered).ToList();
                AnsiConsole.MarkupLine("[dim]agents discover global skills in every repo — skipping "
                                       + Markup.Escape(string.Join(", ", covered.Select(s => s.Label))) + "[/]");
            }
        }

        if (global is null)
        {
            if (styles.Count == 0 && canPrompt)
            {
                var (picked, scope) = PromptDestinations(SkillStyles.All, chosen, ScopeRepo, ScopeGlobal, ScopeCustom);
                if (scope == ScopeSkip) return 0;
                if (scope == ScopeCustom)
                    return Install(Path.GetFullPath(AnsiConsole.Ask<string>("custom path:")), force, nonInteractive);
                chosen = picked;
                global = scope == ScopeGlobal;
            }
            else
            {
                // Default to the safe, contained scope; `global` writes outside
                // the repo, so it must be requested explicitly.
                global = false;
                if (styles.Count == 0)
                    AnsiConsole.MarkupLine("[dim]no --target given; installing to project scope[/]");
            }
        }

        return InstallAll(chosen, global.Value, force, nonInteractive);
    }

    private static int InstallAll(IEnumerable<SkillStyle> styles, bool global, bool force, bool nonInteractive)
    {
        foreach (var style in styles)
            if (Install(Root(style, global), force, nonInteractive) is var exit and not 0)
                return exit;
        return 0;
    }

    private static int Install(string root, bool force, bool nonInteractive)
    {
        var exit = Write(root, force, nonInteractive, out var skills, out var references);
        if (exit == 0 && skills > 0)
            AnsiConsole.MarkupLine($"[green]installed[/] {Markup.Escape(root)} "
                                   + $"[dim]({skills} skills{(references > 0 ? $", +{references} reference files" : "")})[/]");
        return exit;
    }

    private static int RunUpdate(bool global, string? target, string[]? styleNames, bool check, bool nonInteractive)
    {
        if (ParseStyles(styleNames, out var styles) is { } bad) return bad;
        if (global && target is not null)
            return Out.Error("--global and --target both pick a scope — pass one", ErrorCode.BadArgument);

        List<string> candidates;
        if (!string.IsNullOrEmpty(target) && !IsScope(target, "project") && !IsScope(target, "global"))
        {
            if (styles.Count > 0)
                return Out.Error("--target <path> names one folder and --style picks folders by style — pass one",
                    ErrorCode.BadArgument);
            candidates = [Path.GetFullPath(target)];
        }
        else
        {
            bool[] scopes = global || IsScope(target, "global") ? [true]
                : IsScope(target, "project") ? [false]
                : [false, true];
            var picked = styles.Count > 0 ? styles : SkillStyles.All;
            candidates = [.. scopes.SelectMany(g => picked.Select(s => Root(s, g)))];
        }

        var found = candidates.Where(HasBundle).ToList();
        if (found.Count == 0)
        {
            if (check)
            {
                AnsiConsole.MarkupLine($"[dim]no skill bundle installed at {Markup.Escape(string.Join(", ", candidates))}[/]");
                return 0;
            }
            return Out.Error(
                $"no skill bundle at {string.Join(", ", candidates)}; run `knit skill install` first",
                ErrorCode.NotFound);
        }

        // Every bundle found, one line each — a folder left out because a flag
        // was forgotten would drift silently.
        var running = RunningVersion();
        foreach (var root in found)
        {
            var installed = ReadMarkerVersion(root);
            var age = Age(installed, running);
            if (check)
            {
                if (age == BundleAge.Newer) WarnNewer(root, installed!, running);
                else
                    AnsiConsole.MarkupLine(age == BundleAge.Current
                        ? $"[green]up to date[/] {Markup.Escape(root)} [dim]{Markup.Escape(running)}[/]"
                        : $"[yellow]outdated[/] {Markup.Escape(root)} [dim]— installed {Markup.Escape(installed ?? "unknown")}, running {Markup.Escape(running)}; run `knit skill update`[/]");
                continue;
            }

            if (Write(root, force: true, nonInteractive, out _, out _) is var exit and not 0) return exit;
            AnsiConsole.MarkupLine(
                $"[green]updated[/] {Markup.Escape(root)} [dim]{Markup.Escape(installed ?? "unknown")} → {Markup.Escape(running)}[/]");
        }
        return 0;
    }

    private static int Write(string destRoot, bool force, bool nonInteractive, out int skills, out int references)
    {
        skills = 0;
        references = 0;
        var installed = ReadMarkerVersion(destRoot);
        var running = RunningVersion();
        if (HasBundle(destRoot) && Age(installed, running) == BundleAge.Newer)
            AnsiConsole.MarkupLine(
                $"[yellow]downgrading skill bundle[/] [dim]{Markup.Escape(destRoot)} {Markup.Escape(installed!)} → {Markup.Escape(running)} — this knit is older than the installed skills[/]");

        var asm = typeof(SkillInstallCommand).Assembly;
        foreach (var skill in BundleFiles().GroupBy(f => f.Path[..f.Path.IndexOf('/')]))
        {
            var destDir = Path.Combine(destRoot, skill.Key);
            var destFile = Path.Combine(destDir, "SKILL.md");
            if (File.Exists(destFile) && !force)
            {
                if (nonInteractive)
                    return Out.Error($"refused to overwrite {destFile}; pass --force", ErrorCode.Conflict);
                if (!AnsiConsole.Confirm($"overwrite [yellow]{Markup.Escape(destDir)}[/]?", false))
                    continue;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, resource) in skill)
            {
                var dest = Path.Combine(destRoot, path);
                paths.Add(Path.GetFullPath(dest));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, ReadResource(asm, resource));
            }
            PruneStaleReferences(Path.Combine(destDir, ReferencesDir), paths);

            references += skill.Count() - 1;
            skills++;
        }

        if (skills > 0)
            WriteMarker(destRoot);
        return 0;
    }

    // A reference file dropped or renamed in a newer bundle would otherwise
    // linger as an unlinked stale copy an agent could still open.
    private static void PruneStaleReferences(string referencesDir, HashSet<string> written)
    {
        if (!Directory.Exists(referencesDir)) return;
        foreach (var file in Directory.EnumerateFiles(referencesDir, "*.md"))
            if (!written.Contains(Path.GetFullPath(file)))
                File.Delete(file);
    }

    private static void WriteMarker(string destRoot)
    {
        var marker = new SkillBundleMarker
        {
            Version = RunningVersion(),
            InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        Directory.CreateDirectory(destRoot);
        File.WriteAllText(
            Path.Combine(destRoot, MarkerFile),
            JsonSerializer.Serialize(marker, SkillBundleMarkerJsonContext.Default.SkillBundleMarker));
    }

    private static string ReadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
