using System.CommandLine;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Designs;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class DesignCommand
{
    public static Command Build()
    {
        var cmd = new Command("design", "Design-doc operations (.knit/designs/)");
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildShow());
        cmd.Subcommands.Add(BuildNew());
        cmd.Subcommands.Add(BuildEdit());
        cmd.Subcommands.Add(BuildArchive());
        return cmd;
    }

    private static Command BuildEdit()
    {
        var c = new Command("edit", "Write a body section on a design doc (non-interactive; for scripts and agents)");
        var slugArg = new Argument<string>("slug");
        var setSectionOpt = new Option<string?>("--set-section") { Description = "h2 section name to replace or append. Forbidden: Links, Comments." };
        var valueOpt = new Option<string?>("--value") { Description = "Body text for the section (required). Empty string clears the content but keeps the heading." };
        var deleteSectionOpt = new Option<string[]>("--delete-section")
        {
            Description = "h2 section to remove, heading and all. Repeatable. Use this for template headings that don't apply; --set-section --value \"\" is the other operation (keeps the heading). Forbidden: Links, Comments.",
            AllowMultipleArgumentsPerToken = false,
        };
        c.Arguments.Add(slugArg);
        c.Options.Add(setSectionOpt);
        c.Options.Add(valueOpt);
        c.Options.Add(deleteSectionOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var section = parse.GetValue(setSectionOpt);
            var deleteSections = parse.GetValue(deleteSectionOpt) ?? [];
            if (deleteSections.Length > 0 && !string.IsNullOrEmpty(section))
                return Out.Error("--delete-section and --set-section are separate operations; run them one at a time", ErrorCode.BadArgument, json);
            if (deleteSections.Length == 0 && string.IsNullOrEmpty(section))
                return Out.Error("--set-section or --delete-section is required", ErrorCode.BadArgument, json);
            var value = parse.GetValue(valueOpt);
            if (deleteSections.Length == 0 && value is null)
                return Out.Error("--value is required with --set-section", ErrorCode.BadArgument, json);

            // Active docs only, matching `design show` — an archived design is a
            // record, and `design archive` is the verb that moves one.
            var slug = parse.GetValue(slugArg)!;
            var store = new DesignStore(ctx.Root, ctx.Yaml);
            var read = store.Read(slug);
            if (read is null)
                return Out.Error($"no design doc at {DesignsLayout.DesignDocPath(ctx.Root, slug)}", ErrorCode.NotFound, json);
            var (fm, body) = read.Value;

            string newBody;
            if (deleteSections.Length > 0)
            {
                newBody = body;
                foreach (var name in deleteSections)
                {
                    bool removed;
                    try { removed = Knit.Core.Frontmatter.BodySections.TryDeleteSection(newBody, name, out newBody); }
                    catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
                    if (!removed)
                    {
                        var present = Knit.Core.Frontmatter.BodySections.Headings(body);
                        var have = present.Count == 0 ? "none" : string.Join(", ", present);
                        return Out.Error($"no section '{name}' on {slug} (has: {have})", ErrorCode.NotFound, json);
                    }
                }
            }
            else
            {
                try { newBody = Knit.Core.Frontmatter.BodySections.SetSection(body, section!, value!); }
                catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
            }

            var path = store.Write(slug, fm, newBody);
            if (json)
            {
                Console.WriteLine($"{{\"slug\":\"{slug}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");
                return 0;
            }
            var verb = deleteSections.Length > 0 ? "deleted section" : "set section";
            var named = deleteSections.Length > 0 ? string.Join(", ", deleteSections) : section!;
            AnsiConsole.MarkupLine($"[green]{verb}[/] {Markup.Escape(slug)}.[bold]## {Markup.Escape(named)}[/] [dim]{Markup.Escape(path)}[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildList()
    {
        var c = new Command("list", "List active design docs (or archived with --completed)");
        var completedOpt = new Option<bool>("--completed") { Description = "List archived designs under completed/" };
        c.Options.Add(completedOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var completed = parse.GetValue(completedOpt);
            var paths = completed
                ? DesignsLayout.CompletedDesignDocs(ctx.Root)
                : DesignsLayout.ActiveDesignDocs(ctx.Root);

            // A path alone doesn't say what a design is about or whether it is
            // still live, so read each doc for its title + `## Status` line.
            var store = new DesignStore(ctx.Root, ctx.Yaml);
            var rows = new List<(string Slug, string Title, string? Status, string Path)>();
            foreach (var p in paths)
            {
                var slug = DesignsLayout.SlugFromPath(p);
                var read = store.ReadPath(p);
                rows.Add(read is null
                    ? (slug, slug, null, p)
                    : (slug, DesignMeta.TitleOf(slug, read.Value.Frontmatter, read.Value.Body),
                        DesignMeta.StatusOf(read.Value.Body), p));
            }

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartArray();
                    foreach (var r in rows)
                    {
                        w.WriteStartObject();
                        w.WriteString("slug", r.Slug);
                        w.WriteString("title", r.Title);
                        if (r.Status is null) w.WriteNull("status");
                        else w.WriteString("status", r.Status);
                        w.WriteString("path", r.Path);
                        w.WriteBoolean("completed", completed);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            if (rows.Count == 0)
            {
                AnsiConsole.MarkupLine(completed ? "[dim]no archived designs[/]" : "[dim]no active designs[/]");
                return 0;
            }
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumns("slug", "title", "status", "path");
            foreach (var r in rows)
                table.AddRow(
                    Markup.Escape(r.Slug),
                    Markup.Escape(r.Title),
                    r.Status is null ? "[dim]—[/]" : Markup.Escape(r.Status),
                    $"[dim]{Markup.Escape(r.Path)}[/]");
            AnsiConsole.Write(table);
            return 0;
        });
        return c;
    }

    private static Command BuildShow()
    {
        var c = new Command("show", "Show a design doc");
        var slugArg = new Argument<string>("slug");
        var pathOnlyOpt = new Option<bool>("--path-only");
        c.Arguments.Add(slugArg);
        c.Options.Add(pathOnlyOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var path = DesignsLayout.DesignDocPath(ctx.Root, slug);
            if (!File.Exists(path))
                return Out.Error($"no design doc at {path}", ErrorCode.NotFound, json);

            if (parse.GetValue(pathOnlyOpt))
            {
                Console.WriteLine(path);
                return 0;
            }

            var store = new DesignStore(ctx.Root, ctx.Yaml);
            var read = store.Read(slug);
            if (read is null) return Out.Error("unable to read design doc", ErrorCode.Internal, json);
            var (fm, body) = read.Value;

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("slug", slug);
                    w.WriteString("path", path);
                    w.WriteStartObject("frontmatter");
                    foreach (var (k, v) in fm)
                        w.WriteString(k, v?.ToString() ?? "");
                    w.WriteEndObject();
                    w.WriteString("body", body);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(slug)}[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(path)}[/]");
            AnsiConsole.WriteLine();
            if (fm.Count > 0)
            {
                var t = new Table().HideHeaders().Border(TableBorder.None);
                t.AddColumns("k", "v");
                foreach (var (k, v) in fm)
                    t.AddRow($"[dim]{Markup.Escape(k)}[/]", Markup.Escape(v?.ToString() ?? ""));
                AnsiConsole.Write(t);
                AnsiConsole.WriteLine();
            }
            AnsiConsole.Write(new Panel(new Text(body.TrimEnd())).Header("Body").Expand());
            return 0;
        });
        return c;
    }

    private static Command BuildNew()
    {
        var c = new Command("new", "Scaffold a new design doc (<slug>-design.md)");
        var slugArg = new Argument<string>("slug");
        var titleOpt = new Option<string?>("--title") { Description = "Title in the design doc (defaults to the slug)" };
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite an existing design doc" };
        c.Arguments.Add(slugArg);
        c.Options.Add(titleOpt);
        c.Options.Add(forceOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var title = parse.GetValue(titleOpt) ?? slug;
            var force = parse.GetValue(forceOpt);

            var store = new DesignStore(ctx.Root, ctx.Yaml);
            var result = store.New(slug, title, force);
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }

            var path = result.GetValueOrThrow();
            if (json)
            {
                Console.WriteLine($"{{\"slug\":\"{slug}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]created[/] {Markup.Escape(slug)} [dim]{Markup.Escape(path)}[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildArchive()
    {
        var c = new Command("archive", "Move an active design doc into completed/<slug>/");
        var slugArg = new Argument<string>("slug");
        var cycleOpt = new Option<string?>("--cycle") { Description = "Archive filename stem (default: the slug)" };
        c.Arguments.Add(slugArg);
        c.Options.Add(cycleOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var cycle = parse.GetValue(cycleOpt) ?? slug;
            var result = DesignArchive.MoveToCompleted(
                ctx.Root, slug, cycle, new Knit.Data.ProseRefRewriter(ctx.Store, ctx.Root));
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }
            var v = result.GetValueOrThrow();
            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteString("slug", slug);
                    w.WriteString("path", v.Path);
                    w.WriteStartArray("files_relinked");
                    foreach (var f in v.FilesRelinked) w.WriteStringValue(f);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            var proseNote = v.FilesRelinked.Count > 0 ? $" [dim](repointed links in {v.FilesRelinked.Count} file(s))[/]" : "";
            AnsiConsole.MarkupLine($"[green]archived[/] {Markup.Escape(slug)} [dim]→ {Markup.Escape(v.Path)}[/]{proseNote}");
            return 0;
        });
        return c;
    }
}
