using System.CommandLine;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.IO;
using Knit.Core.Refs;
using Knit.Core.Svg;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// Universal viewer: dispatches to <c>knit show</c> or <c>knit kb show</c>
/// based on the bucket. Single entry point for cross-domain refs. A
/// <c>#section</c>, <c>--sections</c> or a <c>.knit/…</c> path ref is
/// answered here, from the doc's file.
/// </summary>
public static class RefShowCommand
{
    public static Command Build()
    {
        var cmd = new Command("ref", "Cross-domain ref operations");
        var show = new Command("show",
            "Resolve and render a ref: bucket:slug, or a .knit/… path for a doc without one. "
            + "A #<section> suffix prints that section only (subsections included); "
            + "#<file>.svg prints the outline of the SVG next to the doc.");
        var refArg = new Argument<string>("ref");
        var pathOnlyOpt = new Option<bool>("--path-only");
        var sectionsOpt = new Option<bool>("--sections") { Description = "List the doc's h2/h3 headings with the anchor a section ref uses" };
        show.Arguments.Add(refArg);
        show.Options.Add(pathOnlyOpt);
        show.Options.Add(sectionsOpt);

        show.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var refStr = parse.GetValue(refArg)!;
            if (!DocRef.TryParse(refStr, out var d))
            {
                var why = refStr.TrimStart().StartsWith(".knit/", StringComparison.Ordinal)
                    ? $"malformed ref '{refStr}': a path ref is a .knit/…/<name>.md file"
                    : Ref.Parse(refStr.Split('#')[0]).GetErrorOrDefault()?.Message ?? $"malformed ref '{refStr}'";
                return Out.Error(why, ErrorCode.BadArgument, json);
            }
            if (d.SvgFile is not null) return ShowSvgOutline(ctx.Root, d, json);
            if (d.IsPointer)
                return Out.Error($"'{refStr}' is a diagram pointer, not a section — show the doc with '{d.Doc}'",
                    ErrorCode.BadArgument, json);

            var sections = parse.GetValue(sectionsOpt);
            if (d.Ref is { } r && d.Fragment is null && !sections)
                return Dispatch(r, json, parse.GetValue(pathOnlyOpt), parse.GetValue(GlobalOptions.Root));

            var (path, exists) = d.Resolve(ctx.Root);
            if (!exists) return Out.Error($"{d.Doc} not found", ErrorCode.NotFound, json);
            if (parse.GetValue(pathOnlyOpt))
            {
                Console.WriteLine(path);
                return 0;
            }
            var body = AtomicFile.ReadAllText(path).Replace("\r\n", "\n");
            if (sections) return ListSections(d, path, body, json);
            if (d.Section is null) return ShowFile(d, path, body, json);

            var text = DocSections.Section(body, d.Section);
            if (text is null)
                return Out.Error(
                    $"{d.Doc} has no section '{d.Section}' — list them with 'knit ref show {d.Doc} --sections'",
                    ErrorCode.NotFound, json);
            if (json)
            {
                WriteJson(w =>
                {
                    w.WriteString("ref", d.ToString());
                    w.WriteString("path", path);
                    w.WriteString("section", d.Section);
                    w.WriteString("text", text);
                });
                return 0;
            }
            Console.WriteLine(text);
            return 0;
        });
        cmd.Subcommands.Add(show);
        return cmd;
    }

    private static int Dispatch(Ref r, bool json, bool pathOnly, string? root)
    {
        var argList = r.Bucket switch
        {
            Bucket.Item => new List<string> { "show", r.Slug },
            Bucket.Design => ["design", "show", r.Slug],
            _ => ["kb", "show", r.ToString()],
        };
        if (json) argList.Add("--json");
        if (pathOnly && r.Bucket != Bucket.Item) argList.Add("--path-only");
        if (root is not null) argList.AddRange(["--root", root]);
        return Program.Main(argList.ToArray());
    }

    private static int ListSections(DocRef d, string path, string body, bool json)
    {
        var outline = DocSections.Outline(body);
        if (json)
        {
            WriteJson(w =>
            {
                w.WriteString("ref", d.Doc);
                w.WriteString("path", path);
                w.WriteStartArray("sections");
                foreach (var h in outline)
                {
                    w.WriteStartObject();
                    w.WriteNumber("level", h.Level);
                    w.WriteString("heading", h.Text);
                    w.WriteString("anchor", h.Anchor);
                    w.WriteString("ref", $"{d.Doc}#{h.Anchor}");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
            return 0;
        }
        if (outline.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no h2/h3 headings[/]");
            return 0;
        }
        foreach (var h in outline)
            AnsiConsole.MarkupLine(
                $"{new string(' ', (h.Level - 2) * 2)}{Markup.Escape(h.Text)}  [dim]{Markup.Escape($"{d.Doc}#{h.Anchor}")}[/]");
        return 0;
    }

    private static int ShowFile(DocRef d, string path, string body, bool json)
    {
        if (json)
        {
            WriteJson(w =>
            {
                w.WriteString("ref", d.Doc);
                w.WriteString("path", path);
                w.WriteString("body", body);
            });
            return 0;
        }
        Console.WriteLine(body.TrimEnd());
        return 0;
    }

    // The meaning of an SVG next to the doc, from its data attributes, and where
    // each node sits — not the paths and shapes, which are the file's to read.
    private static int ShowSvgOutline(string root, DocRef d, bool json)
    {
        var (docPath, exists) = d.Resolve(root);
        if (!exists) return Out.Error($"{d.Doc} not found", ErrorCode.NotFound, json);
        var path = Path.Combine(Path.GetDirectoryName(docPath)!, d.SvgFile!);
        if (!File.Exists(path))
            return Out.Error($"no {d.SvgFile} next to {d.Doc} ({path})", ErrorCode.NotFound, json);
        var svg = SvgDoc.TryParse(AtomicFile.ReadAllText(path), out var error);
        if (svg?.Root is null)
            return Out.Error($"{path} is not well-formed XML: {error}", ErrorCode.BadArgument, json);
        var outline = SvgDoc.Outline(svg);
        var pointerBase = $"{d.Doc}#{d.SvgFile}";
        if (json)
        {
            WriteJson(w =>
            {
                w.WriteString("ref", pointerBase);
                w.WriteString("path", path);
                if (outline.Desc is null) w.WriteNull("desc");
                else w.WriteString("desc", outline.Desc);
                w.WriteStartArray("elements");
                foreach (var e in outline.Elements)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", e.Kind);
                    WriteOptional(w, "id", e.Id);
                    WriteOptional(w, "title", e.Title);
                    WriteOptional(w, "from", e.From);
                    WriteOptional(w, "to", e.To);
                    WriteOptional(w, "group", e.Group);
                    WriteOptional(w, "at", e.At);
                    if (e.Id is not null) w.WriteString("pointer", $"{pointerBase}/{e.Kind}/{e.Id}");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
            return 0;
        }
        AnsiConsole.MarkupLine(outline.Desc is null ? "[dim]no <desc>[/]" : Markup.Escape(outline.Desc));
        foreach (var kind in SvgDoc.Kinds)
        {
            var rows = outline.Elements.Where(e => e.Kind == kind).ToList();
            if (rows.Count == 0) continue;
            AnsiConsole.MarkupLine($"[bold]{kind}s[/]");
            foreach (var e in rows)
            {
                var ends = kind == "edge" ? $" {e.From ?? "?"} → {e.To ?? "?"}" : "";
                var title = e.Title is null ? "" : $" \"{e.Title}\"";
                var at = e.At is null ? "" : $" [dim]at {Markup.Escape(e.At)}[/]";
                var group = e.Group is null ? "" : $" [dim]in {Markup.Escape(e.Group)}[/]";
                AnsiConsole.MarkupLine($"  {Markup.Escape((e.Id ?? "(no id)") + ends + title)}{at}{group}");
            }
        }
        return 0;
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    private static void WriteJson(Action<Utf8JsonWriter> fill)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            fill(w);
            w.WriteEndObject();
        }
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
