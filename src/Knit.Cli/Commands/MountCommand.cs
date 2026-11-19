using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit mount</c> — declare the named read-only directories outside the repo
/// that KB sources may point into. A mount is not a root: exactly one
/// root holds knit state (read + write), any number of mounts are read-only
/// view targets.
/// </summary>
public static class MountCommand
{
    private const string MountsKey = "mounts";

    public static Command Build()
    {
        var cmd = new Command("mount", "Named external directories KB sources may point into");
        cmd.Subcommands.Add(BuildAdd("add", "Declare a mount (fails if the name is taken in that config)"));
        cmd.Subcommands.Add(BuildAdd("set", "Declare or repoint a mount"));
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildRemove());
        // Bare `knit mount` lists — the effective paths are what you want to
        // see when a source refuses to open.
        cmd.SetRepoAction((ctx, parse) => List(ctx, parse.GetValue(GlobalOptions.Json)));
        return cmd;
    }

    private static Option<bool> LocalOption() => new("--local")
    {
        Description = $"Write {ConfigLoader.LocalFileName} (uncommitted, this machine only) instead of {ConfigLoader.FileName}",
    };

    private static Option<bool> GlobalOption() => new("--global")
    {
        Description = "Declare the mount for every repo on this machine (user-level config); the path must be absolute or `~`-anchored",
    };

    private static IReadOnlyList<MountInfo> Describe(CliContext ctx)
    {
        var knitDir = RepoDiscovery.KnitDir(ctx.Root);
        return MountResolver.Describe(
            ctx.Root,
            ConfigLoader.ReadGlobal().Node?.Deserialize(KnitConfigJsonContext.Default.KnitConfig),
            ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.FileName),
            ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.LocalFileName));
    }

    private static MountSource SourceOf(string label) =>
        label == ConfigSnapshot.GlobalLabel ? MountSource.Global
        : label == ConfigLoader.LocalFileName ? MountSource.Local
        : MountSource.Repo;

    internal static string Wire(MountSource source) => source switch
    {
        MountSource.Global => "global",
        MountSource.Local => "local",
        _ => "repo",
    };

    internal static string LabelOf(MountSource source) => source switch
    {
        MountSource.Global => ConfigSnapshot.GlobalLabel,
        MountSource.Local => ConfigLoader.LocalFileName,
        _ => ConfigLoader.FileName,
    };

    private static Command BuildAdd(string verb, string description)
    {
        var c = new Command(verb, description);
        var nameArg = new Argument<string>("name") { Description = MountResolver.NameRule };
        var pathArg = new Argument<string>("path")
        {
            Description = "Directory, relative to the repo root (`../company-docs`); an absolute path works but is not portable",
        };
        var localOpt = LocalOption();
        var globalOpt = GlobalOption();
        c.Arguments.Add(nameArg);
        c.Arguments.Add(pathArg);
        c.Options.Add(localOpt);
        c.Options.Add(globalOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var name = parse.GetValue(nameArg)!;
            if (!MountResolver.IsValidName(name))
                return Out.Error($"invalid mount name '{name}' — {MountResolver.NameRule}", ErrorCode.BadArgument, json);

            var target = ConfigCommand.Target(ctx.Root, parse.GetValue(localOpt), parse.GetValue(globalOpt));
            if (target.Error is not null)
                return Out.Error(target.Error, ErrorCode.BadArgument, json);
            var file = target.Label;

            // Stored with forward slashes so a committed config reads the same
            // on either OS.
            var declared = parse.GetValue(pathArg)!.Replace('\\', '/');
            // The one restriction the global layer carries: a repo-relative
            // path resolves against whichever repo is being read, so the same
            // line would name a different directory in every one of them.
            if (parse.GetValue(globalOpt) && !MountResolver.IsGlobalMountPath(declared))
                return Out.Error(
                    $"'{declared}' is relative — a global mount path must be absolute or `~`-anchored, " +
                    "or it resolves to a different directory in every repo",
                    ErrorCode.BadArgument, json);

            if (ConfigLoader.ReadForWrite(target.Path) is not { } raw)
                return Out.Error(ConfigLoader.Unwritable(file), ErrorCode.Conflict, json);
            if (raw[MountsKey] is not JsonObject mounts)
            {
                mounts = new JsonObject();
                raw[MountsKey] = mounts;
            }
            if (verb == "add" && mounts.ContainsKey(name))
                return Out.Error(
                    $"mount '{name}' already declared in {file} — use `knit mount set` to repoint it",
                    ErrorCode.Conflict, json);

            mounts[name] = declared;
            ConfigLoader.SaveRawFile(target.Path, raw);

            var info = MountResolver.Describe(ctx.Root, name, declared, SourceOf(file));
            // Said at the moment it can still be fixed; `knit verify` repeats
            // it for a config that arrived this way. A global mount is required
            // to be absolute, so portability is not a defect there.
            if (!info.Portable && info.Source != MountSource.Global)
                Console.Error.WriteLine($"note: '{declared}' is absolute — this config is not portable to another machine");
            if (json)
            {
                EmitJson(info);
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]{verb}[/] {Markup.Escape(name)} [dim]-> {Markup.Escape(info.EffectivePath)}[/] [dim]({file})[/]");
            if (!info.Resolves)
                AnsiConsole.MarkupLine("[yellow]unresolved[/] [dim]the directory does not exist yet[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildRemove()
    {
        var c = new Command("remove", "Drop a mount declaration");
        var nameArg = new Argument<string>("name");
        var localOpt = LocalOption();
        var globalOpt = GlobalOption();
        c.Arguments.Add(nameArg);
        c.Options.Add(localOpt);
        c.Options.Add(globalOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var name = parse.GetValue(nameArg)!;
            var target = ConfigCommand.Target(ctx.Root, parse.GetValue(localOpt), parse.GetValue(globalOpt));
            if (target.Error is not null)
                return Out.Error(target.Error, ErrorCode.BadArgument, json);
            var file = target.Label;
            if (ConfigLoader.ReadForWrite(target.Path) is not { } raw)
                return Out.Error(ConfigLoader.Unwritable(file), ErrorCode.Conflict, json);
            if (raw[MountsKey] is not JsonObject mounts || !mounts.ContainsKey(name))
                return Out.Error($"mount '{name}' is not declared in {file}", ErrorCode.NotFound, json);

            mounts.Remove(name);
            if (mounts.Count == 0) raw.Remove(MountsKey);
            ConfigLoader.SaveRawFile(target.Path, raw);

            // Dropping an override hands the mount back to a lower layer
            // instead of deleting it, so say which of the two happened.
            var remaining = Describe(ctx).FirstOrDefault(m => m.Name == name);
            var stillDeclared = remaining is not null;
            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("name", name);
                    w.WriteString("removed_from", file);
                    w.WriteBoolean("still_declared", stillDeclared);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]removed[/] {Markup.Escape(name)} [dim]({file})[/]");
            if (remaining is not null)
                AnsiConsole.MarkupLine(
                    $"[dim]{Markup.Escape(LabelOf(remaining.Source))} still declares it — " +
                    $"{Markup.Escape(remaining.DeclaredPath)} is now in effect[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildList()
    {
        var c = new Command("list", "List mounts with their effective paths");
        c.SetRepoAction((ctx, parse) => List(ctx, parse.GetValue(GlobalOptions.Json)));
        return c;
    }

    private static int List(CliContext ctx, bool json)
    {
        var mounts = Describe(ctx);

        if (json)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteStartArray("mounts");
                foreach (var m in mounts) WriteMount(w, m);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
            return 0;
        }

        RenderTable(mounts);
        return 0;
    }

    /// <summary>
    /// The one mount rendering. <c>knit config list</c> shows the
    /// <c>mounts</c> key through this rather than inventing a second one.
    /// </summary>
    internal static void RenderTable(IReadOnlyList<MountInfo> mounts)
    {
        if (mounts.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no mounts declared[/] [dim](`knit mount add <name> <path>`)[/]");
            return;
        }

        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn("name");
        table.AddColumn("declared");
        table.AddColumn("effective");
        table.AddColumn("from");
        table.AddColumn("state");
        foreach (var m in mounts)
        {
            table.AddRow(
                new Markup($"[bold]{Markup.Escape(m.Name)}[/]"),
                new Markup($"[dim]{Markup.Escape(m.DeclaredPath)}[/]"),
                new Markup(Markup.Escape(m.EffectivePath)),
                new Markup($"[dim]{Markup.Escape(LabelOf(m.Source))}[/]"),
                new Markup(StateMarkup(m)));
            // Shadowing is deliberate — "in this repo, `docs` is the vendored
            // copy" — so it is shown where mounts are listed and never warned
            // about on every command that touches one.
            foreach (var shadow in m.Shadowed)
                table.AddRow(
                    new Markup(""),
                    new Markup($"[dim]{Markup.Escape(shadow.DeclaredPath)}[/]"),
                    new Markup(""),
                    new Markup($"[dim]{Markup.Escape(LabelOf(shadow.Source))}[/]"),
                    new Markup("[dim]overridden[/]"));
        }
        AnsiConsole.Write(table);
    }

    private static string StateMarkup(MountInfo m)
    {
        if (!m.Resolves) return "[red]unresolved[/]";
        // A global mount is required to be absolute, so reporting it as "not
        // portable" would be reporting the rule as a defect.
        if (m.Source == MountSource.Global) return "[green]ok[/]";
        return m.Portable ? "[green]ok[/]" : "[yellow]ok, not portable[/]";
    }

    private static void EmitJson(MountInfo info)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            WriteMount(w, info);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    internal static void WriteMount(Utf8JsonWriter w, MountInfo m)
    {
        w.WriteStartObject();
        w.WriteString("name", m.Name);
        w.WriteString("declared_path", m.DeclaredPath);
        w.WriteString("effective_path", m.EffectivePath);
        w.WriteString("source", Wire(m.Source));
        w.WriteBoolean("resolves", m.Resolves);
        w.WriteBoolean("portable", m.Portable);
        if (m.Shadowed.Count > 0)
        {
            w.WriteStartArray("shadowed");
            foreach (var shadow in m.Shadowed)
            {
                w.WriteStartObject();
                w.WriteString("source", Wire(shadow.Source));
                w.WriteString("declared_path", shadow.DeclaredPath);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }
}
