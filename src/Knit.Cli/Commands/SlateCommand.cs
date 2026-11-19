using System.CommandLine;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Ids;
using Knit.Core.Slates;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class SlateCommand
{
    public static Command Build()
    {
        var cmd = new Command("slate", "Slate registry operations (.knit/slates.md)");
        cmd.Subcommands.Add(BuildAdd());
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildRename());
        cmd.Subcommands.Add(BuildMove());
        cmd.Subcommands.Add(BuildSet());
        cmd.Subcommands.Add(BuildArchive());
        cmd.Subcommands.Add(BuildRm());
        return cmd;
    }

    private static Command BuildAdd()
    {
        var c = new Command("add", "Add a slate to the registry (creates slates.md on first use)");
        var nameArg = new Argument<string>("name") { Description = "Slugified on the way in (`Sprint 1` → `sprint-1`); the slug is the slate's only name" };
        var statusOpt = new Option<string?>("--status") { Description = "planned|active|archived (default: planned)" };
        var noteOpt = new Option<string?>("--note") { Description = "Free-text note — the place for a display name the slug can't carry" };
        c.Arguments.Add(nameArg);
        c.Options.Add(statusOpt);
        c.Options.Add(noteOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var rawName = parse.GetValue(nameArg)!;
            var mint = Slug.Describe(rawName);
            var name = mint.Value;
            var status = SlateStatus.Planned;
            if (parse.GetValue(statusOpt) is { } statusRaw
                && !SlateStatuses.TryParse(statusRaw, out status))
            {
                return Out.Error($"unknown status '{statusRaw}' (expected planned|active|archived)", ErrorCode.BadArgument, json);
            }

            var rows = SlateRegistry.Load(ctx.Root).ToList();
            if (rows.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Out.Error($"slate '{name}' already exists", ErrorCode.Conflict, json);

            rows.Add(new SlateRow(name, status, parse.GetValue(noteOpt)));
            SlateRegistry.Save(ctx.Root, rows);

            Out.MintNote(rawName, mint);
            if (json) EmitRowJson(rows[^1], mint, rawName);
            else AnsiConsole.MarkupLine($"[green]created[/] {Markup.Escape(name)} [dim]({status.ToWire()})[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildList()
    {
        var c = new Command("list", "List slates (active first; --all includes archived)");
        var allOpt = new Option<bool>("--all") { Description = "Include archived slates" };
        c.Options.Add(allOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var rows = SlateRegistry.Load(ctx.Root);
            var all = parse.GetValue(allOpt);
            var shown = rows.Where(r => r.Status == SlateStatus.Active)
                .Concat(rows.Where(r => r.Status == SlateStatus.Planned))
                .Concat(all ? rows.Where(r => r.Status == SlateStatus.Archived) : [])
                .ToList();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartArray();
                    foreach (var r in shown)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", r.Name);
                        w.WriteString("status", r.Status.ToWire());
                        if (r.Notes is not null) w.WriteString("notes", r.Notes);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            if (shown.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]no slates[/]");
                return 0;
            }
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumns("slate", "status", "notes");
            foreach (var r in shown)
            {
                var dim = r.Status == SlateStatus.Archived;
                Markup Cell(string s) =>
                    new Markup(dim ? $"[dim]{Markup.Escape(s)}[/]" : Markup.Escape(s))
                        .Overflow(Overflow.Ellipsis);
                table.AddRow(Cell(r.Name), Cell(r.Status.ToWire()), Cell(r.Notes ?? ""));
            }
            AnsiConsole.Write(table);
            return 0;
        });
        return c;
    }

    private static Command BuildRename()
    {
        var c = new Command("rename", "Rename a slate; rewrites slate: on every assigned item");
        var oldArg = new Argument<string>("old");
        var newArg = new Argument<string>("new");
        c.Arguments.Add(oldArg);
        c.Arguments.Add(newArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var oldName = Slug.From(parse.GetValue(oldArg)!);
            var rawNew = parse.GetValue(newArg)!;
            var mint = Slug.Describe(rawNew);
            var newName = mint.Value;
            if (oldName == newName)
                return Out.Error("old and new name are the same", ErrorCode.BadArgument, json);

            var rows = SlateRegistry.Load(ctx.Root).ToList();
            if (rows.Any(r => r.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return Out.Error($"slate '{newName}' already exists", ErrorCode.Conflict, json);

            var idx = rows.FindIndex(r => r.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            var onItems = SlateItems.Count(ctx.Store, oldName);
            if (idx < 0 && onItems == 0)
                return Out.Error($"no slate '{oldName}' in the registry or on items", ErrorCode.NotFound, json);

            if (idx >= 0)
            {
                rows[idx] = rows[idx] with { Name = newName };
                SlateRegistry.Save(ctx.Root, rows);
            }
            var count = SlateItems.Rename(ctx.Store, oldName, newName);

            Out.MintNote(rawNew, mint);
            if (json)
            {
                EmitRenameJson(oldName, newName, count, mint, rawNew);
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]renamed[/] {Markup.Escape(oldName)} → {Markup.Escape(newName)} [dim]({count} item{(count == 1 ? "" : "s")} updated)[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildMove()
    {
        var c = new Command("move", "Reorder a slate row (--before <name> | --after <name>)");
        var nameArg = new Argument<string>("name");
        var beforeOpt = new Option<string?>("--before");
        var afterOpt = new Option<string?>("--after");
        c.Arguments.Add(nameArg);
        c.Options.Add(beforeOpt);
        c.Options.Add(afterOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var before = parse.GetValue(beforeOpt);
            var after = parse.GetValue(afterOpt);
            if ((before is null) == (after is null))
                return Out.Error("specify exactly one of --before or --after", ErrorCode.BadArgument, json);

            var name = Slug.From(parse.GetValue(nameArg)!);
            var anchor = Slug.From(before ?? after!);
            if (name == anchor)
                return Out.Error("cannot move a slate relative to itself", ErrorCode.BadArgument, json);

            var rows = SlateRegistry.Load(ctx.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return Out.Error($"no slate '{name}' in the registry", ErrorCode.NotFound, json);
            var row = rows[idx];
            rows.RemoveAt(idx);
            var anchorIdx = rows.FindIndex(r => r.Name.Equals(anchor, StringComparison.OrdinalIgnoreCase));
            if (anchorIdx < 0) return Out.Error($"no slate '{anchor}' in the registry", ErrorCode.NotFound, json);
            rows.Insert(before is not null ? anchorIdx : anchorIdx + 1, row);
            SlateRegistry.Save(ctx.Root, rows);

            if (json) Console.WriteLine($"{{\"name\":\"{name}\"}}");
            else AnsiConsole.MarkupLine($"[green]moved[/] {Markup.Escape(name)}");
            return 0;
        });
        return c;
    }

    private static Command BuildSet()
    {
        var c = new Command("set", "Update a slate row (status=… | note=…)");
        var nameArg = new Argument<string>("name");
        var assignmentsArg = new Argument<string[]>("assignments") { Description = "status=planned|active|archived, note=…" };
        c.Arguments.Add(nameArg);
        c.Arguments.Add(assignmentsArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var assignments = parse.GetValue(assignmentsArg) ?? [];
            if (assignments.Length == 0)
                return Out.Error("expected at least one status=… or note=… assignment", ErrorCode.BadArgument, json);

            var name = Slug.From(parse.GetValue(nameArg)!);
            var rows = SlateRegistry.Load(ctx.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return Out.Error($"no slate '{name}' in the registry", ErrorCode.NotFound, json);

            var row = rows[idx];
            foreach (var assignment in assignments)
            {
                var eq = assignment.IndexOf('=');
                if (eq <= 0) return Out.Error($"expected field=value, got '{assignment}'", ErrorCode.BadArgument, json);
                var field = assignment[..eq].Trim();
                var value = assignment[(eq + 1)..].Trim();
                switch (field)
                {
                    case "status":
                        if (!SlateStatuses.TryParse(value, out var status))
                            return Out.Error($"unknown status '{value}' (expected planned|active|archived)", ErrorCode.BadArgument, json);
                        row = row with { Status = status };
                        break;
                    case "note":
                        row = row with { Notes = value.Length == 0 ? null : value };
                        break;
                    default:
                        return Out.Error($"unknown field '{field}' (expected status or note)", ErrorCode.BadArgument, json);
                }
            }
            rows[idx] = row;
            SlateRegistry.Save(ctx.Root, rows);

            if (json) EmitRowJson(row);
            else AnsiConsole.MarkupLine($"[green]updated[/] {Markup.Escape(name)}");
            return 0;
        });
        return c;
    }

    private static Command BuildArchive()
    {
        var c = new Command("archive", "Archive a slate (shortcut for set status=archived)");
        var nameArg = new Argument<string>("name");
        c.Arguments.Add(nameArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var name = Slug.From(parse.GetValue(nameArg)!);
            var rows = SlateRegistry.Load(ctx.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return Out.Error($"no slate '{name}' in the registry", ErrorCode.NotFound, json);

            rows[idx] = rows[idx] with { Status = SlateStatus.Archived };
            SlateRegistry.Save(ctx.Root, rows);

            if (json) EmitRowJson(rows[idx]);
            else AnsiConsole.MarkupLine($"[green]archived[/] {Markup.Escape(name)}");
            return 0;
        });
        return c;
    }

    private static Command BuildRm()
    {
        var c = new Command("rm",
            "Remove a slate: drops the registry row and clears slate: from every item carrying it " +
            "(items that inherited it lose it too). Also clears a slate that is only on items.");
        var nameArg = new Argument<string>("name");
        c.Arguments.Add(nameArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var name = Slug.From(parse.GetValue(nameArg)!);
            var rows = SlateRegistry.Load(ctx.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 && SlateItems.Count(ctx.Store, name) == 0)
                return Out.Error($"no slate '{name}' in the registry or on any item", ErrorCode.NotFound, json);

            if (idx >= 0)
            {
                rows.RemoveAt(idx);
                SlateRegistry.Save(ctx.Root, rows);
            }
            var cleared = SlateItems.Clear(ctx.Store, name);

            if (json)
            {
                Console.WriteLine($"{{\"name\":\"{name}\",\"items_cleared\":{cleared}}}");
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]removed[/] {Markup.Escape(name)} ({cleared} item{(cleared == 1 ? "" : "s")} cleared)");
            return 0;
        });
        return c;
    }

    private static void EmitRenameJson(
        string oldName, string newName, int count, SlugResult mint, string mintInput)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("old", oldName);
            w.WriteString("new", newName);
            w.WriteNumber("items_updated", count);
            WriteMint(w, mint, mintInput);
            w.WriteEndObject();
        }
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>The new name isn't the one that was typed.</summary>
    private static void WriteMint(Utf8JsonWriter w, SlugResult? mint, string? mintInput)
    {
        if (mint is not { Changed: true } m) return;
        w.WriteBoolean("id_trimmed", true);
        w.WriteString("id_input", mintInput ?? "");
        w.WriteString("id_reason", m.Reason!);
    }

    private static void EmitRowJson(SlateRow row, SlugResult? mint = null, string? mintInput = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("name", row.Name);
            w.WriteString("status", row.Status.ToWire());
            if (row.Notes is not null) w.WriteString("notes", row.Notes);
            WriteMint(w, mint, mintInput);
            w.WriteEndObject();
        }
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
