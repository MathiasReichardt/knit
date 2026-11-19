using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Core.Config;
using Knit.Core.Sharing;
using Knit.Core.Discovery;
using Knit.Core.Paths;
using Knit.Core.Templates;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class InitCommand
{
    public static Command Build()
    {
        var cmd = new Command("init",
            "Scaffold .knit/ in the current directory. Also writes .knit/.gitignore "
            + "with `knit-feedback.md` (local feedback notes) and `config.local.json` "
            + "(per-machine overlay). Replicate manually if you "
            + "delete the file or migrate an existing repo. Safe to re-run: on an "
            + "existing .knit/ it prints the getting-started hint and exits 0 without "
            + "changing anything.");
        var handleOpt = new Option<string?>("--handle") { Description = "Your handle (skips prompt)" };
        var displayOpt = new Option<string?>("--display") { Description = "Your display name (skips prompt)" };
        var nonInteractiveOpt = new Option<bool>("--non-interactive") { Description = "Fail if any prompt would be needed" };
        var agentsFileOpt = new Option<string?>("--agents-file") { Description = "Add the Knit section to the detected CLAUDE.md / AGENTS.md: project | skip (skips the prompt)" };
        var skillsOpt = new Option<string?>("--skills") { Description = "Install the agent skill bundle into the detected skill folders: global | project | skip (skips the prompt)" };
        var externalOpt = new Option<string?>("--external")
        {
            Description = "Use an existing knit repo as this project's root: writes only a config.json that requires "
                          + "knit_root, then points this checkout at <path>. No items, templates or identity here",
        };
        var hintOpt = new Option<string?>("--hint")
        {
            Description = "With --external: what a fresh clone is told to obtain, e.g. \"clone <url> anywhere\"",
        };
        cmd.Options.Add(handleOpt);
        cmd.Options.Add(displayOpt);
        cmd.Options.Add(nonInteractiveOpt);
        cmd.Options.Add(agentsFileOpt);
        cmd.Options.Add(skillsOpt);
        cmd.Options.Add(externalOpt);
        cmd.Options.Add(hintOpt);

        cmd.SetAction(parse =>
        {
            var handle = parse.GetValue(handleOpt);
            var display = parse.GetValue(displayOpt);
            var nonInteractive = parse.GetValue(nonInteractiveOpt);
            var agentsFile = parse.GetValue(agentsFileOpt);
            var skills = parse.GetValue(skillsOpt);
            if (SkillInstallCommand.ValidateSkillsValue(skills) is { } badSkills) return badSkills;
            if (AgentsPointer.Validate(agentsFile) is { } badAgentsFile) return badAgentsFile;
            if (parse.GetValue(externalOpt) is { } external)
                return RunExternal(external, parse.GetValue(hintOpt), nonInteractive);
            if (parse.GetValue(hintOpt) is not null)
                return Output.Out.Error("--hint goes with --external", Output.ErrorCode.BadArgument);
            return Run(handle, display, nonInteractive, agentsFile, skills);
        });
        return cmd;
    }

    private const string UseInherited = "use it here (nothing written to this repo)";
    private const string PinInherited = "pin it for this checkout (config.local.json)";
    private const string UseDifferent = "use a different identity in this checkout";

    // A second `.knit/` below a redirecting checkout is the split state the
    // redirect exists to prevent. Unconditional: with knit root validating its
    // target, "init there first, then point at it" is the one order that works.
    private static int? BelowRedirect(string cwd)
    {
        var enclosing = RepoDiscovery.ResolveDetailed(cwd);
        return enclosing.Redirected && !Directory.Exists(RepoDiscovery.KnitDir(cwd))
            ? Output.Out.Error(
                $"'{enclosing.Checkout}' redirects its knit root to '{enclosing.OverridePath}' — " +
                "work there instead of scaffolding a second .knit/ here, or clear it first with 'knit root --off'",
                Output.ErrorCode.Conflict)
            : null;
    }

    /// <summary>
    /// A fresh repo whose tracker lives elsewhere: the requirement is
    /// committed, the path never is. Only the minimal pair is written — the
    /// target owns items, templates and every other setting — and the path is
    /// validated before anything is, so a refusal leaves no half-made repo.
    /// </summary>
    private static int RunExternal(string path, string? hint, bool nonInteractive)
    {
        var cwd = Directory.GetCurrentDirectory();
        if (BelowRedirect(cwd) is { } below) return below;
        if (Directory.Exists(RepoDiscovery.KnitDir(cwd)))
            return Output.Out.Error(
                $"{RepoDiscovery.KnitDir(cwd)} already exists — point it with 'knit root <path>' and declare it with " +
                $"'knit config set {Requirements.Key}.{RepoDiscovery.KnitRootKey} \"<hint>\"'",
                Output.ErrorCode.Conflict);

        var (end, via, error, code) = KnitRootCommand.Validate(cwd, path);
        if (end is null) return Output.Out.Error(error!, code);

        // Asked, not defaulted: it is the one line every later clone reads.
        if (string.IsNullOrWhiteSpace(hint))
        {
            if (nonInteractive || Console.IsInputRedirected)
                return Output.Out.Error(
                    "--hint required — what a fresh clone is told to obtain, e.g. \"clone <url> anywhere\"",
                    Output.ErrorCode.BadArgument);
            hint = AnsiConsole.Ask<string>("Hint for fresh clones — where to get the knit root:");
        }

        var knitDir = RepoDiscovery.KnitDir(cwd);
        Directory.CreateDirectory(knitDir);
        RepoDiscovery.EnsureKnitGitignore(cwd);
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName, new JsonObject
        {
            ["format_version"] = FormatVersion.Current,
            [Requirements.Key] = new JsonObject { [RepoDiscovery.KnitRootKey] = hint.Trim() },
        });
        AnsiConsole.MarkupLine(
            $"[green]initialized[/] {Markup.Escape(knitDir)} [dim]— requires a knit root on every checkout[/]");
        var exit = KnitRootCommand.Write(cwd, end, via, rootOverride: null, json: false);
        AnsiConsole.MarkupLine(
            "[dim]commit .knit/config.json and .knit/.gitignore; every other clone runs 'knit config setup'[/]");
        return exit;
    }

    private static int Run(string? handle, string? display, bool nonInteractive, string? agentsFile, string? skills)
    {
        var cwd = Directory.GetCurrentDirectory();
        var knitDir = RepoDiscovery.KnitDir(cwd);
        if (BelowRedirect(cwd) is { } below) return below;

        // No stdin means Spectre's Ask/Confirm throw rather than prompt — treat it
        // as --non-interactive so the first command a new user runs can't stack-trace.
        var canPrompt = !nonInteractive && !Console.IsInputRedirected;

        if (Directory.Exists(knitDir))
        {
            AnsiConsole.MarkupLine($"[dim].knit/ already initialized at {Markup.Escape(knitDir)}[/]");
            AgentsPointer.OfferOnInit(cwd, canPrompt, agentsFile);
            PrintGettingStarted(SkillInstallCommand.OfferOnInit(canPrompt, skills));
            return 0;
        }
        var why = nonInteractive ? " in --non-interactive mode" : " — no terminal is available to prompt";

        // Identity describes the person, not the project. An inherited
        // value is shown and left where it is by default — committing a copy
        // would tell the next person who clones this repo that they are
        // whoever ran `init`, and would go stale the day that handle changes.
        var global = ConfigLoader.ReadGlobal().Node?.Deserialize(KnitConfigJsonContext.Default.KnitConfig);
        var inheritedHandle = string.IsNullOrEmpty(global?.Handle) ? null : global.Handle;
        var inheritedDisplay = string.IsNullOrEmpty(global?.Display) ? null : global.Display;
        var pinInherited = false;
        var saveGlobally = false;

        if (string.IsNullOrEmpty(handle) && inheritedHandle is not null && canPrompt)
        {
            // Asking "your handle?" when the answer is already known is
            // ceremony; the real question is which of three things to do with
            // the value that already resolves here.
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title($"Identity [green]{Markup.Escape(inheritedHandle)}[/]"
                       + $" [dim]/ {Markup.Escape(inheritedDisplay ?? inheritedHandle)}[/]"
                       + " comes from your global config. In this repo:")
                .AddChoices(UseInherited, PinInherited, UseDifferent));
            if (choice == PinInherited) pinInherited = true;
            if (choice == UseDifferent)
            {
                handle = AnsiConsole.Ask("Your [green]handle[/] in this repo:", inheritedHandle);
                display = AnsiConsole.Ask("Your [green]display name[/] in this repo:", inheritedDisplay ?? handle);
            }
        }

        if (string.IsNullOrEmpty(handle))
        {
            if (inheritedHandle is not null) handle = inheritedHandle;
            else if (canPrompt) handle = AnsiConsole.Ask<string>("Your [green]handle[/] (short, e.g. alice):");
            else return Output.Out.Error($"--handle required{why}", Output.ErrorCode.BadArgument);
        }
        if (string.IsNullOrEmpty(display))
        {
            if (inheritedDisplay is not null) display = inheritedDisplay;
            else if (canPrompt) display = AnsiConsole.Ask("Your [green]display name[/]:", handle);
            else return Output.Out.Error($"--display required{why}", Output.ErrorCode.BadArgument);
        }

        // Offered once per machine, at the one moment the person has just
        // typed their name — never the default, and never asked again once the
        // file exists. Not `global is null`: that is also a file that does not
        // parse, and saving would replace it.
        if (!File.Exists(UserPaths.GlobalConfigFile) && canPrompt
            && AnsiConsole.Confirm(
                "Save this identity for [green]every repo[/] on this machine "
                + $"[dim]({Markup.Escape(UserPaths.GlobalConfigFile)})[/]?", defaultValue: false))
        {
            saveGlobally = true;
            ConfigLoader.SaveRawFile(UserPaths.GlobalConfigFile,
                new JsonObject { ["handle"] = handle, ["display"] = display });
            inheritedHandle = handle;
            inheritedDisplay = display;
        }

        Directory.CreateDirectory(knitDir);
        // items/, kb/ and .knit/.cache/ are all created by the first write
        // that needs them. Scaffolding an empty items/ was pointless anyway:
        // git does not track empty directories, so it never survived a clone,
        // and a repo using only the KB would carry it forever.

        RepoDiscovery.EnsureKnitGitignore(cwd);
        // Only the keys just answered, written as a raw layer: saving the
        // record would materialise every default, and `knit config list` would
        // then report a due-soon window the user never chose as set in
        // config.json.
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName,
            new JsonObject { ["format_version"] = FormatVersion.Current });
        // Only what this checkout actually decides: a value taken unchanged
        // from the global layer keeps resolving through it, unless the answer
        // was "pin it here". Never config.json — the committed layer beats
        // every cloner's global one, so they would all record as you.
        var identity = new JsonObject();
        if (pinInherited || handle != inheritedHandle) identity["handle"] = handle;
        if (pinInherited || display != inheritedDisplay) identity["display"] = display;
        if (identity.Count > 0)
        {
            if (ConfigLoader.ReadForWrite(Path.Combine(knitDir, ConfigLoader.LocalFileName)) is not { } local)
                return Output.Out.Error(ConfigLoader.Unwritable(ConfigLoader.LocalFileName), Output.ErrorCode.Conflict);
            foreach (var (key, value) in identity) local[key] = value?.DeepClone();
            ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.LocalFileName, local);
        }
        var written = TemplateEngine.ExtractDefaults(cwd);

        AnsiConsole.MarkupLine($"[green]initialized[/] {Markup.Escape(knitDir)}");
        // Where each identity value lives, said every time: a value the reader
        // doesn't know the home of is one they "fix" in the wrong file.
        var localFile = $"{RepoDiscovery.KnitDirName}/{ConfigLoader.LocalFileName}";
        foreach (var (key, value) in new[] { ("handle", handle), ("display", display) })
            AnsiConsole.MarkupLine(identity.ContainsKey(key)
                ? $"  {key,-7} [green]{Markup.Escape(value!)}[/] → {localFile} [dim](this checkout only, uncommitted)[/]"
                : $"  {key,-7} [green]{Markup.Escape(value!)}[/] " +
                  $"{(saveGlobally ? "→" : "from")} global [dim]({Markup.Escape(UserPaths.GlobalConfigFile)})[/]");
        if (identity.Count > 0)
            AnsiConsole.MarkupLine(
                "[dim]hint: to use it in every repo — " +
                $"'knit config set --global handle {Markup.Escape(handle!)}', then 'knit config unset --local handle' " +
                "(same for display)[/]");
        AnsiConsole.MarkupLine($"[dim]wrote {written.Count} default templates to .knit/templates/[/]");
        PrintForge(cwd, knitDir);

        AgentsPointer.OfferOnInit(cwd, canPrompt, agentsFile);
        var haveSkills = SkillInstallCommand.OfferOnInit(canPrompt, skills);
        PrintGettingStarted(haveSkills);
        return 0;
    }

    // SL6: info, never a prompt — an unknown host is asked about on the first copy.
    private static void PrintForge(string cwd, string knitDir)
    {
        var origin = ShareLinks.Origin(cwd, RepoDiscovery.FindGitRoot(cwd), ConfigLoader.Load(knitDir));
        AnsiConsole.MarkupLine(origin.Kind switch
        {
            ShareOriginKind.Forge =>
                $"[dim]share links: {Forges.Name(origin.Forge!.Kind)} at {Markup.Escape(origin.Host!)} ('knit url <ref>')[/]",
            ShareOriginKind.UnknownHost =>
                $"[dim]share links: unknown forge at {Markup.Escape(origin.Host!)} — set on first copy[/]",
            ShareOriginKind.Invalid => $"[dim]share links: {Markup.Escape(origin.Error!)}[/]",
            _ => "[dim]share links: no origin remote — links read <repo_name>:<path>[/]",
        });
    }

    private static void PrintGettingStarted(bool haveSkills)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]New to Knit?[/] Ask your AI agent about any of these:");
        AnsiConsole.MarkupLine("  • Knit items and their purpose (epic / feature / bug / task)");
        AnsiConsole.MarkupLine("  • KB buckets and their purpose (adr, doc, feature, log, idea, source)");
        AnsiConsole.MarkupLine("  • Design docs vs. executing tracked work");
        AnsiConsole.MarkupLine("  • The local web UI ([green]knit web[/])");
        AnsiConsole.MarkupLine("  • How to create an item");
        AnsiConsole.MarkupLine("  • Committing your work — Knit is git-native; you stage [green].knit/[/] yourself");
        AnsiConsole.MarkupLine("  • Conventions — your own rules for agents (tags, close rules, body structure), "
                               + "for this repo or for you ([green]knit conventions[/])");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]First steps:[/]");
        if (!haveSkills)
            AnsiConsole.MarkupLine("  [green]knit skill install[/]     equip your agent to help with Knit");
        AnsiConsole.MarkupLine("  [green]knit add feature \"…\"[/]   create your first item");
        AnsiConsole.MarkupLine("  [green]knit web[/]                browse the local web UI");
        AnsiConsole.WriteLine();
        // Said on every init: a config layer nobody knows about is a config
        // layer nobody uses, and this is the only moment knit has the reader's
        // attention.
        AnsiConsole.MarkupLine(
            "[dim]Settings can be per repo or per machine — "
            + "[green]knit config set --global <key> <value>[/] applies to every repo you work in; "
            + "[green]knit config list[/] shows every value with the file it came from.[/]");
    }

}
