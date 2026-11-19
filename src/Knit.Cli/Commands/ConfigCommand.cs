using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Paths;
using Knit.Core.Sharing;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit config</c> — see and change <c>.knit/config.json</c>.
/// Every key had shipped invisible: the file was written by <c>knit init</c>
/// and <c>knit mount</c> and otherwise hand-edited, so "why is my due-soon
/// window 5 days, and did I set it?" was unanswerable without opening a file
/// and knowing the defaults by heart. Source is the point of the command.
/// </summary>
public static class ConfigCommand
{
    public static Command Build()
    {
        var cmd = new Command("config", "Show and change repo settings (.knit/config.json)");
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildGet());
        cmd.Subcommands.Add(BuildSet());
        cmd.Subcommands.Add(BuildUnset());
        cmd.Subcommands.Add(ConfigSetupCommand.Build());
        // Bare `knit config` lists — the effective values are what you want
        // when a setting doesn't seem to apply.
        cmd.SetRepoAction((ctx, parse) => List(ctx, parse.GetValue(GlobalOptions.Json)));
        return cmd;
    }

    private static Option<bool> LocalOption() => new("--local")
    {
        Description = $"Write {ConfigLoader.LocalFileName} (uncommitted, this machine only) instead of {ConfigLoader.FileName}",
    };

    private static Option<bool> GlobalOption() => new("--global")
    {
        Description = "Write the user-level config, which every repo on this machine inherits",
    };

    private static Option<bool> RepoOption() => new("--repo")
    {
        Description = $"Write {ConfigLoader.FileName} (committed, shared) — the default for every key but handle / display",
    };

    /// <summary>
    /// Which layer a write lands in, or the reason the flags contradict each
    /// other. <c>Label</c> is what the report prints; <c>Path</c> is the file,
    /// since the global one is not inside the repo.
    /// </summary>
    internal static (string Label, string Path, string? Error) Target(string root, bool local, bool global, bool repo = false)
    {
        string[] passed = [.. new[] { (local, "--local"), (repo, "--repo"), (global, "--global") }.Where(f => f.Item1).Select(f => f.Item2)];
        if (passed.Length > 1) return ("", "", $"{string.Join(" and ", passed)} name different files — pass one");
        var knitDir = RepoDiscovery.KnitDir(root);
        if (global) return (ConfigSnapshot.GlobalLabel, UserPaths.GlobalConfigFile, null);
        var file = local ? ConfigLoader.LocalFileName : ConfigLoader.FileName;
        return (file, Path.Combine(knitDir, file), null);
    }

    private static Command BuildList()
    {
        var c = new Command("list", "Every known key with its effective value, default and source");
        c.SetRepoAction((ctx, parse) => List(ctx, parse.GetValue(GlobalOptions.Json)));
        return c;
    }

    private static int List(CliContext ctx, bool json)
    {
        var snapshot = ConfigReport.Build(ctx.Root);
        if (Unreadable(snapshot, json) is { } err) return err;

        if (json)
        {
            EmitSnapshot(snapshot, Requirements.Check(ctx.Resolution));
            return 0;
        }

        // Under a redirect every value below is the target's — the checkout
        // is a pointer and nothing else — so its own knit_root never shows here.
        if (ctx.Resolution.Redirected)
            AnsiConsole.MarkupLine(
                $"[yellow]note:[/] {Markup.Escape(ctx.Resolution.Checkout!)} redirects to this root — the settings " +
                "below are the target's; `knit root` shows the redirect");

        var table = new Table().Border(TableBorder.None);
        table.AddColumn("key");
        table.AddColumn("value");
        table.AddColumn("source");
        table.AddColumn("default");
        foreach (var e in snapshot.Keys)
        {
            var name = e.Known
                ? $"[bold]{Markup.Escape(e.Name)}[/]"
                : $"[bold]{Markup.Escape(e.Name)}[/] [yellow](unknown key)[/]";
            table.AddRow(
                new Markup(name),
                new Markup(Markup.Escape(Shown(e.Value))),
                new Markup(e.IsSet
                    ? Markup.Escape(e.SourceLabel)
                    : $"[dim]{Markup.Escape(e.SourceLabel)}[/]"),
                new Markup($"[dim]{Markup.Escape(Shown(e.Default))}[/]"));
        }
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("[dim]mounts[/]");
        MountCommand.RenderTable(snapshot.Mounts);

        if (snapshot.Forges.Count > 0)
        {
            AnsiConsole.MarkupLine("[dim]forges[/]");
            foreach (var f in snapshot.Forges)
                AnsiConsole.MarkupLine(
                    $"  [bold]{Markup.Escape(f.Host)}[/] {Markup.Escape(f.Value)} [dim]({Markup.Escape(f.SourceLabel)})[/]"
                    + (f.Error is null ? "" : $" [red]invalid — {Markup.Escape(f.Error)}[/]"));
        }

        var requirements = Requirements.Check(ctx.Resolution);
        if (requirements.Count > 0)
        {
            AnsiConsole.MarkupLine("[dim]requires[/]");
            foreach (var q in requirements)
                AnsiConsole.MarkupLine(
                    $"  [bold]{Markup.Escape(q.Setting)}[/] {(q.Met ? "[green]set[/]" : "[red]not set here[/]")} " +
                    $"[dim]— {Markup.Escape(q.Hint)}[/]");
        }

        if (snapshot.IgnoredGlobal.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"[yellow]ignored in the global config ({snapshot.IgnoredGlobal.Count})[/]");
            foreach (var ignored in snapshot.IgnoredGlobal)
                AnsiConsole.MarkupLine(
                    $"  [bold]{Markup.Escape(ignored.Name)}[/] [dim]{Markup.Escape(ignored.Reason)}[/]");
        }

        // The source column answers "where does this come from?"; the legend
        // answers the question that follows it — "so which file do I edit?" —
        // without widening the table with a path per row.
        AnsiConsole.WriteLine();
        foreach (var layer in snapshot.Layers)
            AnsiConsole.MarkupLine(
                $"[dim]{Markup.Escape(layer.Label)}[/] [dim]→[/] [dim]{Markup.Escape(layer.Path)}[/]"
                + (File.Exists(layer.Path) ? "" : " [dim](not present)[/]"));
        return 0;
    }

    private static Command BuildGet()
    {
        var c = new Command("get", "Print one value alone on stdout, so command substitution works");
        var keyArg = new Argument<string>("key") { Description = ConfigKeys.NameList };
        c.Arguments.Add(keyArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var name = parse.GetValue(keyArg)!;
            if (ForgeHost(name) is { } host) return GetForge(ctx, host, json);
            if (Lookup(name, json) is { } bad) return bad;

            var snapshot = ConfigReport.Build(ctx.Root);
            if (Unreadable(snapshot, json) is { } err) return err;

            var entry = snapshot.Keys.FirstOrDefault(e => e.Name == name);
            if (entry is null)
                return Out.Error($"'{name}' has no single value — see `knit config list`", ErrorCode.BadArgument, json);
            if (json)
            {
                EmitEntry(entry);
                return 0;
            }
            // Bare, unstyled, stdout: this output is meant to be captured.
            Console.WriteLine(entry.Value);
            return 0;
        });
        return c;
    }

    private const string LayerHelp =
        $"{ConfigLoader.LocalFileName} with --local, the user-level config with --global; " +
        $"handle / display default to --local — --repo names {ConfigLoader.FileName}";

    /// <summary>
    /// <see cref="Target"/> with the key's own default: a personal key
    /// (handle / display) names this checkout, as <c>git config</c> does —
    /// the committed file only when <c>--repo</c> asks for it.
    /// </summary>
    private static (string Label, string Path, string? Error) KeyTarget(
        string root, string name, bool local, bool global, bool repo) =>
        Target(root,
            local || (!global && !repo && ConfigKeys.Find(name) is { Scope: ConfigScope.Personal }),
            global, repo);

    private static Command BuildSet()
    {
        var c = new Command("set", $"Set a key in {ConfigLoader.FileName} ({LayerHelp})");
        var keyArg = new Argument<string>("key") { Description = ConfigKeys.NameList };
        var valueArg = new Argument<string>("value");
        var localOpt = LocalOption();
        var repoOpt = RepoOption();
        var globalOpt = GlobalOption();
        var webOpt = new Option<string?>("--web")
        {
            Description = $"With {Forges.Key}.<host>: the address the host's repos live under, when the host is no web host " +
                "(an SSH alias, a separate SSH host, a sub-path install)",
        };
        c.Arguments.Add(keyArg);
        c.Arguments.Add(valueArg);
        c.Options.Add(localOpt);
        c.Options.Add(repoOpt);
        c.Options.Add(globalOpt);
        c.Options.Add(webOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var name = parse.GetValue(keyArg)!;
            var web = parse.GetValue(webOpt);
            if (web is not null && ForgeHost(name) is null)
                return Out.Error($"--web applies to {Forges.Key}.<host> only", ErrorCode.BadArgument, json);
            if (RequirementSetting(name) is { } setting)
                return SetRequirement(ctx, setting, parse.GetValue(valueArg)!,
                    parse.GetValue(localOpt), parse.GetValue(globalOpt), json);
            var global = parse.GetValue(globalOpt);
            var target = KeyTarget(ctx.Root, name, parse.GetValue(localOpt), global, parse.GetValue(repoOpt));
            if (target.Error is not null)
                return Out.Error(target.Error, ErrorCode.BadArgument, json);
            if (ForgeHost(name) is { } host)
            {
                var written = ConfigWriter.SetForge(target.Path, target.Label, host, parse.GetValue(valueArg)!, web);
                return written.Succeeded ? ReportForge(ctx, host, "set", target.Label, json) : WriteError(written, json);
            }

            var result = ConfigWriter.Set(target.Path, target.Label, global, name, parse.GetValue(valueArg)!);
            return result.Succeeded
                ? Report(ctx, name, "set", target.Label, json)
                : WriteError(result, json);
        });
        return c;
    }

    private static Command BuildUnset()
    {
        var c = new Command("unset", $"Remove a key from a layer so the value falls back ({LayerHelp})");
        var keyArg = new Argument<string>("key") { Description = ConfigKeys.NameList };
        var localOpt = LocalOption();
        var repoOpt = RepoOption();
        var globalOpt = GlobalOption();
        c.Arguments.Add(keyArg);
        c.Options.Add(localOpt);
        c.Options.Add(repoOpt);
        c.Options.Add(globalOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var name = parse.GetValue(keyArg)!;
            if (RequirementSetting(name) is { } setting) return UnsetRequirement(ctx, setting, json);
            var target = KeyTarget(ctx.Root, name, parse.GetValue(localOpt), parse.GetValue(globalOpt), parse.GetValue(repoOpt));
            if (target.Error is not null)
                return Out.Error(target.Error, ErrorCode.BadArgument, json);
            if (ForgeHost(name) is { } host)
            {
                var removed = ConfigWriter.UnsetForge(target.Path, target.Label, host);
                return removed.Succeeded ? ReportForge(ctx, host, "unset", target.Label, json) : WriteError(removed, json);
            }

            var result = ConfigWriter.Unset(target.Path, target.Label, name);
            return result.Succeeded
                ? Report(ctx, name, "unset", target.Label, json)
                : WriteError(result, json);
        });
        return c;
    }

    /// <summary>
    /// What the value reads as now — a write to a layer says nothing about the
    /// effective value on its own, since the local overlay may still win and
    /// an unset falls back to the other layer or the default.
    /// </summary>
    private static int Report(CliContext ctx, string name, string verb, string file, bool json)
    {
        var entry = ConfigReport.Build(ctx.Root).Keys.First(e => e.Name == name);
        if (json)
        {
            EmitEntry(entry);
            return 0;
        }
        AnsiConsole.MarkupLine($"[green]{verb}[/] {Markup.Escape(name)} [dim]({Markup.Escape(file)})[/]");
        AnsiConsole.MarkupLine(
            $"[dim]now[/] {Markup.Escape(Shown(entry.Value))} [dim]from {Markup.Escape(entry.SourceLabel)}[/]");
        // After a set, the written layer holds the key, so any other source
        // is a higher layer winning over it.
        if (verb == "set" && entry.SourceLabel != file)
            AnsiConsole.MarkupLine(
                $"[yellow]shadowed[/] by {Markup.Escape(entry.SourceLabel)} — " +
                $"'knit config unset {(entry.SourceLabel == ConfigLoader.LocalFileName ? "--local" : "--repo")} " +
                $"{Markup.Escape(name)}' to let it apply");
        return 0;
    }

    private static int GetForge(CliContext ctx, string host, bool json)
    {
        var snapshot = ConfigReport.Build(ctx.Root);
        if (Unreadable(snapshot, json) is { } err) return err;
        var row = snapshot.Forges.FirstOrDefault(f => string.Equals(f.Host, host, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            if (Forges.BuiltIn(host) is not { } builtIn)
                return Out.Error($"'{Forges.Key}.{host}' is not set and is not a built-in host", ErrorCode.NotFound, json);
            row = new ForgeRow(host, builtIn.KindText, ConfigValueSource.Default, null);
        }
        if (json)
        {
            EmitJson(w => WriteForge(w, row));
            return 0;
        }
        Console.WriteLine(row.Value);
        return 0;
    }

    private static int ReportForge(CliContext ctx, string host, string verb, string file, bool json)
    {
        var row = ConfigReport.Build(ctx.Root).Forges
            .FirstOrDefault(f => string.Equals(f.Host, host, StringComparison.OrdinalIgnoreCase));
        if (row is null && Forges.BuiltIn(host) is { } builtIn)
            row = new ForgeRow(host, builtIn.KindText, ConfigValueSource.Default, null);
        if (json)
        {
            EmitJson(w =>
            {
                if (row is not null)
                {
                    WriteForge(w, row);
                    return;
                }
                w.WriteStartObject();
                w.WriteString("host", host);
                w.WriteNull("value");
                w.WriteNull("source");
                w.WriteEndObject();
            });
            return 0;
        }
        AnsiConsole.MarkupLine($"[green]{verb}[/] {Markup.Escape($"{Forges.Key}.{host}")} [dim]({Markup.Escape(file)})[/]");
        if (row is null) return 0;
        AnsiConsole.MarkupLine($"[dim]now[/] {Markup.Escape(row.Value)} [dim]from {Markup.Escape(row.SourceLabel)}[/]");
        if (verb == "set" && row.SourceLabel != file)
            AnsiConsole.MarkupLine($"[yellow]shadowed[/] by {Markup.Escape(row.SourceLabel)}");
        return 0;
    }

    private static void WriteForge(Utf8JsonWriter w, ForgeRow f)
    {
        w.WriteStartObject();
        w.WriteString("host", f.Host);
        w.WriteString("value", f.Value);
        w.WriteString("source", f.SourceLabel);
        if (f.Error is not null) w.WriteString("error", f.Error);
        w.WriteEndObject();
    }

    private static void EmitJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) write(w);
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string? RequirementSetting(string name) =>
        ConfigKeys.MapEntry(name) is (Requirements.Key, var setting) ? setting : null;

    private static string? ForgeHost(string name) =>
        ConfigKeys.MapEntry(name) is (Forges.Key, var host) ? host : null;

    /// <summary>
    /// A requirement is the project's contract with every checkout, so it is
    /// committed — and written to the checkout, never a redirect target: the
    /// clone that reads it is a clone of this repo.
    /// </summary>
    private static int SetRequirement(CliContext ctx, string setting, string hint, bool local, bool global, bool json)
    {
        var name = $"{Requirements.Key}.{setting}";
        if (global || local)
            return Out.Error(
                $"'{name}' is what every clone reads, so it lives in {ConfigLoader.FileName} only — drop " +
                (global ? "--global" : "--local"),
                ErrorCode.BadArgument, json);
        if (!Requirements.IsKnownSetting(setting))
            return Out.Error(
                $"'{setting}' is not a local-only setting a checkout can be asked for — use {Requirements.SettingRule}",
                ErrorCode.BadArgument, json);
        hint = hint.Trim();
        if (hint.Length == 0)
            return Out.Error(
                "the hint must not be empty — it is what a fresh checkout is told to obtain, e.g. \"clone <url>\"",
                ErrorCode.BadArgument, json);

        var checkout = ctx.Resolution.Checkout ?? ctx.Root;
        var knitDir = RepoDiscovery.KnitDir(checkout);
        if (ConfigLoader.ReadForWrite(Path.Combine(knitDir, ConfigLoader.FileName)) is not { } raw)
            return Out.Error(ConfigLoader.Unwritable(ConfigLoader.FileName), ErrorCode.Conflict, json);
        if (raw[Requirements.Key] is not JsonObject requires)
        {
            requires = new JsonObject();
            raw[Requirements.Key] = requires;
        }
        requires[setting] = hint;
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName, raw);
        return ReportRequirement(ctx, setting, "declared", json);
    }

    private static int UnsetRequirement(CliContext ctx, string setting, bool json)
    {
        var checkout = ctx.Resolution.Checkout ?? ctx.Root;
        var knitDir = RepoDiscovery.KnitDir(checkout);
        if (ConfigLoader.ReadForWrite(Path.Combine(knitDir, ConfigLoader.FileName)) is not { } raw)
            return Out.Error(ConfigLoader.Unwritable(ConfigLoader.FileName), ErrorCode.Conflict, json);
        if (raw[Requirements.Key] is not JsonObject requires || !requires.Remove(setting))
            return Out.Error($"'{Requirements.Key}.{setting}' is not declared in {ConfigLoader.FileName}",
                ErrorCode.NotFound, json);
        if (requires.Count == 0) raw.Remove(Requirements.Key);
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName, raw);
        if (json)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("setting", setting);
                w.WriteBoolean("declared", false);
                w.WriteEndObject();
            }
            Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            return 0;
        }
        AnsiConsole.MarkupLine(
            $"[green]unset[/] {Markup.Escape($"{Requirements.Key}.{setting}")} [dim]({ConfigLoader.FileName})[/]");
        return 0;
    }

    private static int ReportRequirement(CliContext ctx, string setting, string verb, bool json)
    {
        // Check reads the file afresh, so the resolution from before the write still answers.
        var q = Requirements.Check(ctx.Resolution).First(x => x.Setting == setting);
        if (json)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                WriteRequirement(w, q);
            Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            return 0;
        }
        AnsiConsole.MarkupLine(
            $"[green]{verb}[/] {Markup.Escape($"{Requirements.Key}.{setting}")} [dim]({ConfigLoader.FileName}) — " +
            "commit it; a checkout without the setting refuses until 'knit config setup'[/]");
        if (!q.Met)
            AnsiConsole.MarkupLine(
                "[yellow]note:[/] this checkout has not filled it in either — 'knit config setup'");
        return 0;
    }

    internal static void WriteRequirement(Utf8JsonWriter w, Requirement q)
    {
        w.WriteStartObject();
        w.WriteString("setting", q.Setting);
        w.WriteString("hint", q.Hint);
        w.WriteBoolean("met", q.Met);
        w.WriteEndObject();
    }

    private static int WriteError(ConfigWriteResult result, bool json) =>
        Out.Error(result.Error!, result.Failure switch
        {
            ConfigWriteFailure.NotSet => ErrorCode.NotFound,
            ConfigWriteFailure.Unreadable => ErrorCode.Conflict,
            _ => ErrorCode.BadArgument,
        }, json);

    private static int? Lookup(string name, bool json) =>
        ConfigKeys.Find(name) is null
            ? Out.Error($"unknown config key '{name}' — known keys: {ConfigKeys.NameList}", ErrorCode.BadArgument, json)
            : null;

    private static int? Unreadable(ConfigSnapshot snapshot, bool json) =>
        snapshot.Unreadable.Count == 0
            ? null
            : Out.Error(
                $"cannot read {string.Join(" and ", snapshot.Unreadable)} — the JSON does not parse, so every key in it " +
                "would be reported as a default it is not; fix the file (`knit verify` lists it too)",
                ErrorCode.Conflict, json);

    private static string Shown(string value) => value.Length == 0 ? "—" : value;

    private static void EmitEntry(ConfigEntry entry)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            WriteEntry(w, entry);
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void EmitSnapshot(ConfigSnapshot snapshot, IReadOnlyList<Requirement> requirements)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject("keys");
            foreach (var e in snapshot.Keys)
            {
                w.WritePropertyName(e.Name);
                WriteEntry(w, e);
            }
            w.WriteEndObject();
            w.WriteStartArray("mounts");
            foreach (var m in snapshot.Mounts) MountCommand.WriteMount(w, m);
            w.WriteEndArray();
            w.WriteStartArray("requires");
            foreach (var q in requirements) WriteRequirement(w, q);
            w.WriteEndArray();
            w.WriteStartArray(Forges.Key);
            foreach (var f in snapshot.Forges) WriteForge(w, f);
            w.WriteEndArray();
            w.WriteStartArray("unreadable");
            foreach (var f in snapshot.Unreadable) w.WriteStringValue(f);
            w.WriteEndArray();
            w.WriteStartArray("ignored_global");
            foreach (var ignored in snapshot.IgnoredGlobal)
            {
                w.WriteStartObject();
                w.WriteString("key", ignored.Name);
                w.WriteString("reason", ignored.Reason);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("layers");
            foreach (var layer in snapshot.Layers) w.WriteString(layer.Label, layer.Path);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteEntry(Utf8JsonWriter w, ConfigEntry e)
    {
        w.WriteStartObject();
        w.WriteString("value", e.Value);
        w.WriteString("source", e.SourceLabel);
        w.WriteString("default", e.Default);
        w.WriteBoolean("is_set", e.IsSet);
        w.WriteBoolean("known", e.Known);
        w.WriteBoolean("settable", e.Settable);
        w.WriteEndObject();
    }
}
