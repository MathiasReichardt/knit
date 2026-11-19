using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Paths;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit config setup</c> — fill in, after a checkout, every local-only
/// setting this project declares under <c>requires</c>, and an identity if
/// none resolves. A dispatcher, not a second mechanism: each answer goes
/// through the writer its focused verb uses. Runs before root resolution like
/// <c>knit root</c>, since a checkout that needs setting up may not resolve.
/// </summary>
public static class ConfigSetupCommand
{
    public static Command Build()
    {
        var c = new Command("setup",
            "Fill in the local-only settings this checkout's config.json requires (knit root, mounts) and your "
            + "handle if none is set — asked one at a time on a terminal, or passed as flags");
        var knitRootOpt = new Option<string?>("--knit-root")
        {
            Description = "The knit repo this checkout uses (as 'knit root <path>'), or '.' for its own .knit/ on purpose",
        };
        var mountOpt = new Option<string[]>("--mount")
        {
            Description = "<name>=<path> for a required mount, written to config.local.json (repeatable)",
            AllowMultipleArgumentsPerToken = false,
        };
        var handleOpt = new Option<string?>("--handle") { Description = "Your handle, saved for every repo on this machine" };
        var displayOpt = new Option<string?>("--display") { Description = "Your display name (defaults to the handle)" };
        var nonInteractiveOpt = new Option<bool>("--non-interactive") { Description = "Fail instead of asking" };
        c.Options.Add(knitRootOpt);
        c.Options.Add(mountOpt);
        c.Options.Add(handleOpt);
        c.Options.Add(displayOpt);
        c.Options.Add(nonInteractiveOpt);

        c.SetAction(parse =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var rootOverride = parse.GetValue(GlobalOptions.Root);
            var r = Resolve(rootOverride);
            if (r.Checkout is null) return KnitRootCommand.NoCheckout(r, rootOverride, json);
            var checkout = r.Checkout;
            var changes = new List<string>();

            if (parse.GetValue(knitRootOpt) is { } knitRoot
                && SetKnitRoot(checkout, knitRoot, changes) is { } rootErr)
                return Out.Error(rootErr.Message, rootErr.Code, json);

            foreach (var spec in parse.GetValue(mountOpt) ?? [])
            {
                var eq = spec.IndexOf('=');
                if (eq <= 0 || eq == spec.Length - 1)
                    return Out.Error($"--mount '{spec}' is not <name>=<path>", ErrorCode.BadArgument, json);
                if (SetMount(Resolve(rootOverride), spec[..eq], spec[(eq + 1)..], changes) is { } mountErr)
                    return Out.Error(mountErr, ErrorCode.BadArgument, json);
            }

            if (parse.GetValue(handleOpt) is { } handle
                && SetIdentity(handle, parse.GetValue(displayOpt), changes) is { } idErr)
                return Out.Error(idErr, ErrorCode.BadArgument, json);

            var canAsk = !json && !parse.GetValue(nonInteractiveOpt)
                         && !Console.IsInputRedirected && !Console.IsOutputRedirected;
            if (canAsk) Interview(rootOverride, changes);

            r = Resolve(rootOverride);
            var requirements = Requirements.Check(r);
            var unmet = requirements.Where(q => !q.Met).Select(q => q.Setting).ToList();
            var noHandle = HandleMissing(r);
            if (noHandle) unmet.Add("handle");
            if (unmet.Count > 0)
                return Out.Error(
                    $"still not set: {string.Join(", ", unmet)} — pass " +
                    string.Join(", ", unmet.Select(Flag)) +
                    (canAsk ? "" : ", or run it on a terminal to be asked"),
                    ErrorCode.Conflict, json);

            if (json) return Emit(r, requirements);
            foreach (var line in changes) AnsiConsole.MarkupLine(line);
            if (changes.Count == 0)
                AnsiConsole.MarkupLine(requirements.Count == 0
                    ? "[dim]nothing to set up — this checkout requires no local settings and your handle is set[/]"
                    : "[dim]nothing to set up — every setting this checkout requires is filled in[/]");
            return 0;
        });
        return c;
    }

    private static RepoDiscovery.RootResolution Resolve(string? rootOverride) =>
        RepoDiscovery.ResolveDetailed(Directory.GetCurrentDirectory(), rootOverride);

    private static bool HandleMissing(RepoDiscovery.RootResolution r) =>
        r.Root is not null && ConfigLoader.Load(RepoDiscovery.KnitDir(r.Root)).Handle is null;

    private static string Flag(string setting) => setting switch
    {
        RepoDiscovery.KnitRootKey => "--knit-root <path>",
        "handle" => "--handle <you>",
        _ => $"--mount {setting[Requirements.MountPrefix.Length..]}=<path>",
    };

    private static (string Message, ErrorCode Code)? SetKnitRoot(string checkout, string value, List<string> changes)
    {
        if (value.Trim() == RepoDiscovery.LocalKnitRoot)
        {
            if (KnitRootCommand.Store(checkout, RepoDiscovery.LocalKnitRoot) is { } err) return (err, ErrorCode.Conflict);
            changes.Add("[green]set[/] knit root [dim]to this checkout's own .knit/, on purpose[/]");
            return null;
        }
        var (end, via, error, code) = KnitRootCommand.Validate(checkout, value);
        if (end is null) return (error!, code);
        if (KnitRootCommand.Store(checkout, end) is { } unreadable) return (unreadable, ErrorCode.Conflict);
        changes.Add($"[green]set[/] knit root → {Markup.Escape(end)}" +
                    (via.Count > 0 ? $" [dim](via {Markup.Escape(string.Join(" → ", via))})[/]" : ""));
        if (KnitRootCommand.SiblingNote(checkout, end) is { } sibling) changes.Add(sibling);
        return null;
    }

    /// <summary>
    /// Into the effective root's <c>config.local.json</c> — the path is this
    /// machine's, and the mount is read from the root the checkout resolves to.
    /// </summary>
    private static string? SetMount(RepoDiscovery.RootResolution r, string name, string path, List<string> changes)
    {
        if (r.Root is null) return "the knit root does not resolve yet — set it first (--knit-root)";
        if (!MountResolver.IsValidName(name)) return $"invalid mount name '{name}' — {MountResolver.NameRule}";
        var knitDir = RepoDiscovery.KnitDir(r.Root);
        if (ConfigLoader.ReadForWrite(Path.Combine(knitDir, ConfigLoader.LocalFileName)) is not { } raw)
            return ConfigLoader.Unwritable(ConfigLoader.LocalFileName);
        if (raw["mounts"] is not JsonObject mounts)
        {
            mounts = new JsonObject();
            raw["mounts"] = mounts;
        }
        mounts[name] = path.Replace('\\', '/');
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.LocalFileName, raw);
        RepoDiscovery.EnsureKnitGitignore(r.Root);
        changes.Add($"[green]set[/] mount {Markup.Escape(name)} → {Markup.Escape(path)} [dim]({ConfigLoader.LocalFileName})[/]");
        return null;
    }

    /// <summary>Identity describes the person, so it is saved for the machine.</summary>
    private static string? SetIdentity(string handle, string? display, List<string> changes)
    {
        var parsed = ConfigKeys.Find("handle")!.Parse(handle);
        if (parsed.Error is not null) return $"invalid handle — {parsed.Error}";
        if (ConfigLoader.ReadForWrite(UserPaths.GlobalConfigFile) is not { } raw)
            return ConfigLoader.Unwritable(UserPaths.GlobalConfigFile);
        raw["handle"] = parsed.Value;
        raw["display"] = string.IsNullOrWhiteSpace(display) ? parsed.Value!.DeepClone() : display.Trim();
        ConfigLoader.SaveRawFile(UserPaths.GlobalConfigFile, raw);
        changes.Add($"[green]set[/] handle {Markup.Escape(handle.Trim())} " +
                    $"[dim](global — {Markup.Escape(UserPaths.GlobalConfigFile)})[/]");
        return null;
    }

    private const string Own = "this checkout's own .knit/, on purpose";
    private const string Pick = "a path to the knit repo";

    private static void Interview(string? rootOverride, List<string> changes)
    {
        foreach (var q in Requirements.Check(Resolve(rootOverride)).Where(q => !q.Met))
        {
            var r = Resolve(rootOverride);
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(q.Setting)}[/] [dim]— {Markup.Escape(q.Hint)}[/]");
            if (q.Setting == RepoDiscovery.KnitRootKey)
            {
                var main = GitWorktree.Counterpart(r.Checkout!);
                var worktree = main is not null && RepoDiscovery.IsRepo(main)
                    ? GitWorktree.Bare(r.Checkout!) is not null
                        ? $"the default branch's worktree ({main})"
                        : $"the main checkout ({main})"
                    : null;
                var prompt = new SelectionPrompt<string>().Title("Where does this checkout's knit state live?");
                if (worktree is not null) prompt.AddChoice(worktree);
                prompt.AddChoices(Pick, Own);
                var choice = AnsiConsole.Prompt(prompt);
                var value = choice == Own ? RepoDiscovery.LocalKnitRoot
                    : choice == Pick ? AnsiConsole.Ask<string>("Knit repo [green]path[/]:")
                    : main!;
                if (SetKnitRoot(r.Checkout!, value, changes) is { } err)
                    AnsiConsole.MarkupLine($"[red]not set:[/] {Markup.Escape(err.Message)}");
            }
            else
            {
                var name = q.Setting[Requirements.MountPrefix.Length..];
                var path = AnsiConsole.Ask<string>($"Path for mount [green]{Markup.Escape(name)}[/]:");
                if (SetMount(r, name, path, changes) is { } err)
                    AnsiConsole.MarkupLine($"[red]not set:[/] {Markup.Escape(err)}");
            }
        }

        if (!HandleMissing(Resolve(rootOverride))) return;
        var handle = AnsiConsole.Ask<string>("Your [green]handle[/] (short, e.g. alice — saved for every repo on this machine):");
        var display = AnsiConsole.Ask("Your [green]display name[/]:", handle);
        if (SetIdentity(handle, display, changes) is { } idErr)
            AnsiConsole.MarkupLine($"[red]not set:[/] {Markup.Escape(idErr)}");
    }

    private static int Emit(RepoDiscovery.RootResolution r, IReadOnlyList<Requirement> requirements)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("checkout", r.Checkout);
            w.WriteString("root", r.Root);
            w.WriteString("handle", r.Root is null ? null : ConfigLoader.Load(RepoDiscovery.KnitDir(r.Root)).Handle);
            w.WriteStartArray("requires");
            foreach (var q in requirements) ConfigCommand.WriteRequirement(w, q);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
        return 0;
    }
}
