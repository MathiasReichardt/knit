using System.CommandLine;
using System.Text;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Refs;
using Knit.Data;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class ContextCommand
{
    private const string AcceptanceHeading = BodySections.AcceptanceHeading;
    private const string PointersHeading = BodySections.PointersHeading;

    public static Command Build()
    {
        var cmd = new Command("context", "Print an item + parent chain + unioned agent_skills/kb_refs + acceptance + code pointers + blockers");
        var idArg = new Argument<string>("id");
        var noBodyOpt = new Option<bool>("--no-body");
        var noRefsOpt = new Option<bool>("--no-refs");
        var fullOpt = new Option<bool>("--full");
        var watchOpt = Watch.Option();
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(noBodyOpt);
        cmd.Options.Add(noRefsOpt);
        cmd.Options.Add(fullOpt);
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);
            var idValue = parse.GetValue(idArg)!;
            var (item, err) = ItemUtils.Resolve(ctx, idValue);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var noBody = parse.GetValue(noBodyOpt);
            var noRefs = parse.GetValue(noRefsOpt);
            var full = parse.GetValue(fullOpt);

            static void WarnUnresolved(Item current, List<KbRefInfo> resolvedRefs)
            {
                foreach (var u in resolvedRefs.Where(r => !r.Exists))
                    Console.Error.WriteLine(
                        $"warning: item {current.Id[..Math.Min(8, current.Id.Length)]} references " +
                        $"{u.Display} — file not found at {u.ExpectedPath}. " +
                        $"Use 'knit kb rename' or 'knit relink' to fix.");
            }

            if (json)
            {
                var items = ctx.Store.AllItems().ToList();
                var graph = new LinkGraph(items);
                var resolved = ItemContextResolver.Resolve(graph, item);
                var resolvedRefs = noRefs ? new List<KbRefInfo>() : ResolveKbRefs(ctx, resolved);
                WarnUnresolved(item, resolvedRefs);
                var acceptance = BodySections.ExtractList(item.Body, AcceptanceHeading);
                var pointers = BodySections.ExtractList(item.Body, PointersHeading);
                EmitJson(ctx, item, resolved, resolvedRefs, acceptance, pointers, graph, full);
                return 0;
            }

            IRenderable Render()
            {
                var (refreshed, _) = ItemUtils.Resolve(ctx, idValue);
                var current = refreshed ?? item;
                var items = ctx.Store.AllItems().ToList();
                var graph = new LinkGraph(items);
                var resolved = ItemContextResolver.Resolve(graph, current);
                var resolvedRefs = noRefs ? new List<KbRefInfo>() : ResolveKbRefs(ctx, resolved);
                WarnUnresolved(current, resolvedRefs);
                var acceptance = BodySections.ExtractList(current.Body, AcceptanceHeading);
                var pointers = BodySections.ExtractList(current.Body, PointersHeading);
                return BuildHuman(ctx, current, resolved, resolvedRefs, acceptance, pointers, graph, noBody, full);
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        });
        return cmd;
    }

    private sealed record KbRefInfo(
        string Display,
        string TargetPath,
        string ExpectedPath,
        string? Bucket,
        bool Exists,
        string? Title,
        string? Summary,
        string? Url,
        string? Note,
        string InheritedFromShort);

    private static List<KbRefInfo> ResolveKbRefs(CliContext ctx, ResolvedItemContext rc)
    {
        var store = new Knit.Core.Kb.KbStore(ctx.Root, ctx.Yaml);
        var result = new List<KbRefInfo>();
        foreach (var r in rc.KbRefs)
        {
            // r.TargetPath is relative to the source item file's directory.
            // Resolve to absolute by joining with that directory.
            var sourceFile = ctx.Store.FindItemFile(rc.Item.Id);
            var sourceDir = sourceFile is null ? ctx.Store.ItemsDir : Path.GetDirectoryName(sourceFile)!;
            var absolute = Path.GetFullPath(Path.Combine(sourceDir, r.TargetPath));

            var exists = File.Exists(absolute);
            string? bucket = null;
            string? title = null;
            string? summary = null;
            string? url = null;

            var rel = Path.GetRelativePath(Knit.Core.Kb.KbLayout.KbDir(ctx.Root), absolute)
                .Replace('\\', '/');
            if (!rel.StartsWith("..", StringComparison.Ordinal))
            {
                var seg = rel.Split('/').FirstOrDefault();
                if (!string.IsNullOrEmpty(seg)) bucket = SingularizeBucket(seg);
            }

            if (exists)
            {
                var read = store.ReadPath(absolute);
                if (read is not null)
                {
                    title = read.Value.Frontmatter.TryGetValue("title", out var t) ? t?.ToString() : null;
                    summary = read.Value.Frontmatter.TryGetValue("summary", out var s) ? s?.ToString() : null;
                    if (string.Equals(bucket, "source", StringComparison.OrdinalIgnoreCase))
                        url = read.Value.Frontmatter.TryGetValue("url", out var u) ? u?.ToString() : null;
                }
            }

            result.Add(new KbRefInfo(
                Display: string.IsNullOrEmpty(r.TargetText) ? r.TargetPath : r.TargetText,
                TargetPath: r.TargetPath,
                ExpectedPath: absolute,
                Bucket: bucket,
                Exists: exists,
                Title: title,
                Summary: summary,
                Url: url,
                Note: r.Note,
                InheritedFromShort: ShortInherit(r.InheritedFrom)));
        }
        return result;
    }

    private static string? SingularizeBucket(string folder) =>
        BucketFolders.TryFromFolder(folder, out var b) ? b.ToWire() : folder;

    private static string ShortInherit(string id) =>
        string.IsNullOrEmpty(id) ? "" : id[..Math.Min(8, id.Length)];

    /// <summary>
    /// The body as the Description panel should show it: the title h1 dropped
    /// (the rule above already carries it), empty scaffold sections dropped,
    /// and the sections <c>context</c> renders as structured lists of their own
    /// dropped too — printing those in the panel as well listed the acceptance
    /// criteria twice.
    /// </summary>
    private static string DescriptionText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";

        var blocks = new List<(string? Heading, StringBuilder Content)>();
        (string? Heading, StringBuilder Content) current = (null, new StringBuilder());
        var sawContent = false;

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                blocks.Add(current);
                current = (line.Trim(), new StringBuilder());
                sawContent = true;
                continue;
            }
            if (!sawContent)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                sawContent = true;
                if (line.StartsWith("# ", StringComparison.Ordinal)) continue;
            }
            current.Content.Append(line).Append('\n');
        }
        blocks.Add(current);

        var parts = new List<string>();
        foreach (var (heading, content) in blocks)
        {
            var text = content.ToString().Trim('\n').TrimEnd();
            if (text.Length == 0) continue;
            if (heading is not null && RenderedElsewhere(heading[3..].Trim())) continue;
            parts.Add(heading is null ? text : $"{heading}\n\n{text}");
        }
        return string.Join("\n\n", parts);
    }

    private static bool RenderedElsewhere(string heading) =>
        heading.Equals(AcceptanceHeading, StringComparison.OrdinalIgnoreCase)
        || heading.Equals(PointersHeading, StringComparison.OrdinalIgnoreCase)
        || heading.Equals("Links", StringComparison.OrdinalIgnoreCase)
        || heading.Equals("Comments", StringComparison.OrdinalIgnoreCase);

    private static IRenderable BuildHuman(
        CliContext ctx, Item item, ResolvedItemContext rc, List<KbRefInfo> refs,
        IReadOnlyList<BodySections.ListEntry> acceptance, IReadOnlyList<BodySections.ListEntry> pointers, LinkGraph graph,
        bool noBody, bool full)
    {
        var pieces = new List<IRenderable>
        {
            new Rule($"[bold]{item.Type.Icon()} {Markup.Escape(item.Title)}[/]").LeftJustified(),
            new Markup($"[dim]{Markup.Escape(Out.Short(ctx, item.Id))} · {item.Type.ToWire()} · {item.State.Glyph()} {item.State.ToWire()}[/]"),
        };

        if (!string.IsNullOrEmpty(item.Assignee))
            pieces.Add(new Markup($"[dim]assignee:[/] {Markup.Escape(item.Assignee)}"));

        if (!string.IsNullOrEmpty(item.Summary))
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]Summary[/]"));
            pieces.Add(new Text(item.Summary));
        }

        // `feature:` is a KB-doc pointer, `tags:` is the free-form label list —
        // two different fields that were both rendered under one "Tags" label.
        var features = item.Feature.Distinct().ToList();
        if (features.Count > 0)
            pieces.Add(new Markup($"[bold]Feature docs[/]: {Markup.Escape(string.Join(", ", features))}"));

        if (item.Tags.Count > 0)
            pieces.Add(new Markup($"[bold]Tags[/]: {Markup.Escape(string.Join(", ", item.Tags))}"));

        if (rc.ParentChain.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Rule("[bold]Parent chain[/]").LeftJustified());
            foreach (var p in rc.ParentChain)
            {
                var summary = string.IsNullOrEmpty(p.Summary) ? "" : $" — {Markup.Escape(p.Summary)}";
                pieces.Add(new Markup($"  {p.Type.Icon()} [dim]{Markup.Escape(Out.Short(ctx, p.Id))}[/] {Markup.Escape(p.Title)} {p.State.Glyph()}{summary}"));
            }
        }

        if (rc.Blocked.Count > 0) pieces.Add(Text.Empty);
        foreach (var b in rc.Blocked)
        {
            var from = string.IsNullOrEmpty(b.InheritedFrom)
                ? "self"
                : $"parent {Out.TitledPlain(ctx, b.InheritedFrom)}";
            pieces.Add(new Markup($"[red]BLOCKED[/] ({Markup.Escape(from)}): {Markup.Escape(b.Reason)}"));
        }

        if (rc.ExternalRefs.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]External[/]"));
            foreach (var x in rc.ExternalRefs)
            {
                var from = string.IsNullOrEmpty(x.InheritedFrom)
                    ? ""
                    : $" [dim](from {Out.Titled(ctx, x.InheritedFrom)})[/]";
                var note = string.IsNullOrEmpty(x.Note) ? "" : $" [dim]— {Markup.Escape(x.Note)}[/]";
                pieces.Add(new Markup(
                    $"  · {LinkRels.ToWire(x.Rel)}: {Markup.Escape(x.Text)}{note}{from}"));
            }
        }

        if (rc.AgentSkills.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]Session preparation[/]"));
            foreach (var s in rc.AgentSkills)
                pieces.Add(new Markup($"  · {Markup.Escape(s.Skill)}"));
        }

        var description = noBody ? "" : DescriptionText(item.Body);
        if (!string.IsNullOrWhiteSpace(description))
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Panel(new Text(description)).Header("Description").Expand());
        }

        if (acceptance.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]Acceptance criteria[/]"));
            foreach (var a in acceptance)
            {
                var marker = a.Checked switch { true => "[x]", false => "[ ]", null => "-" };
                pieces.Add(new Markup($"  {Markup.Escape(marker)} {Markup.Escape(a.Text)}"));
            }
        }

        if (pointers.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]Code pointers[/]"));
            foreach (var p in pointers) pieces.Add(new Markup($"  - {Markup.Escape(p.Text)}"));
        }

        // Incoming edges — items blocking THIS one (not the items it blocks).
        var blockerIds = graph.BlockersOf(item.Id).ToList();
        if (blockerIds.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]Blockers[/]"));
            foreach (var bid in blockerIds)
            {
                var target = graph.Get(bid);
                var status = target?.State.Glyph() ?? "?";
                var title = target?.Title ?? "(unknown)";
                var summary = string.IsNullOrEmpty(target?.Summary) ? "" : $" — {Markup.Escape(target.Summary)}";
                pieces.Add(new Markup($"  · [dim]{Markup.Escape(Out.Short(ctx, bid))}[/] {Markup.Escape(title)} {status}{summary}"));
            }
        }

        if (refs.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Markup("[bold]References[/]"));
            foreach (var g in refs.GroupBy(r => r.Bucket ?? "ref").OrderBy(g => g.Key))
            {
                pieces.Add(new Markup($"  [dim]{g.Key}[/]"));
                foreach (var r in g)
                {
                    var disp = r.Note ?? r.Summary ?? r.Title ?? r.Display;
                    var inherit = string.IsNullOrEmpty(r.InheritedFromShort) ? "" : $" [dim](from {r.InheritedFromShort})[/]";
                    var unresolved = r.Exists ? "" : " [red]UNRESOLVED[/]";
                    var url = string.IsNullOrEmpty(r.Url) ? "" : $" [dim]{Markup.Escape(r.Url)}[/]";
                    pieces.Add(new Markup($"    · {Markup.Escape(r.Display)}: {Markup.Escape(disp)}{url}{inherit}{unresolved}"));
                }
            }
        }

        if (rc.Design is not null)
        {
            var inherit = string.IsNullOrEmpty(rc.Design.InheritedFrom)
                ? ""
                : $" [dim](from {Out.Titled(ctx, rc.Design.InheritedFrom)})[/]";
            pieces.Add(Text.Empty);
            pieces.Add(new Markup($"[bold]Active design[/]: {Markup.Escape(rc.Design.Slug)}{inherit}"));
        }

        if (rc.Milestone is not null)
        {
            var inherit = string.IsNullOrEmpty(rc.Milestone.InheritedFrom)
                ? ""
                : $" [dim](from {Out.Titled(ctx, rc.Milestone.InheritedFrom)})[/]";
            pieces.Add(Text.Empty);
            pieces.Add(new Markup($"[bold]Milestone[/]: {Markup.Escape(rc.Milestone.QualifiedSlug)}{inherit}"));
        }

        var childCount = graph.ChildrenOf(item.Id).Count;
        pieces.Add(Text.Empty);
        pieces.Add(new Markup($"[dim]children: {childCount} · comments: {item.Comments.Count}[/]"));

        return new Rows(pieces);
    }

    private static void EmitJson(
        CliContext ctx, Item item, ResolvedItemContext rc, List<KbRefInfo> refs,
        IReadOnlyList<BodySections.ListEntry> acceptance, IReadOnlyList<BodySections.ListEntry> pointers, LinkGraph graph, bool full)
    {
        using var stream = new MemoryStream();
        var opts = new JsonWriterOptions { Indented = true };
        using (var w = new Utf8JsonWriter(stream, opts))
        {
            w.WriteStartObject();
            w.WriteString("id", item.Id);
            w.WriteString("short_id", Out.Short(ctx, item.Id));
            w.WriteString("title", item.Title);
            if (!string.IsNullOrEmpty(item.Summary)) w.WriteString("summary", item.Summary);
            w.WriteString("type", item.Type.ToWire());
            w.WriteString("state", item.State.ToWire());
            if (!string.IsNullOrEmpty(item.Assignee)) w.WriteString("assignee", item.Assignee);
            if (item.Feature.Count > 0)
            {
                w.WriteStartArray("feature");
                foreach (var f in item.Feature) w.WriteStringValue(f);
                w.WriteEndArray();
            }
            if (item.Tags.Count > 0)
            {
                w.WriteStartArray("tags");
                foreach (var t in item.Tags) w.WriteStringValue(t);
                w.WriteEndArray();
            }

            w.WriteStartArray("parents");
            foreach (var p in rc.ParentChain)
            {
                w.WriteStartObject();
                w.WriteString("ref", $"item:{p.Id}");
                w.WriteString("short_id", Out.Short(ctx, p.Id));
                w.WriteString("title", p.Title);
                if (!string.IsNullOrEmpty(p.Summary)) w.WriteString("summary", p.Summary);
                w.WriteString("type", p.Type.ToWire());
                w.WriteString("state", p.State.ToWire());
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (rc.Blocked.Count > 0)
            {
                w.WriteStartArray("blocked");
                foreach (var b in rc.Blocked)
                {
                    w.WriteStartObject();
                    w.WriteString("value", b.Reason);
                    if (!string.IsNullOrEmpty(b.InheritedFrom))
                        w.WriteString("inherited_from", $"item:{b.InheritedFrom}");
                    else w.WriteNull("inherited_from");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            if (rc.ExternalRefs.Count > 0)
            {
                w.WriteStartArray("external_refs");
                foreach (var x in rc.ExternalRefs)
                {
                    w.WriteStartObject();
                    w.WriteString("rel", LinkRels.ToWire(x.Rel));
                    w.WriteString("target", x.Text);
                    if (x.Url is null) w.WriteNull("url"); else w.WriteString("url", x.Url);
                    if (x.Note is null) w.WriteNull("note"); else w.WriteString("note", x.Note);
                    if (string.IsNullOrEmpty(x.InheritedFrom)) w.WriteNull("inherited_from");
                    else w.WriteString("inherited_from", $"item:{x.InheritedFrom}");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            if (rc.AgentSkills.Count > 0)
            {
                w.WriteStartArray("agent_skills");
                foreach (var s in rc.AgentSkills) w.WriteStringValue(s.Skill);
                w.WriteEndArray();
            }

            w.WriteString("body", item.Body);

            if (acceptance.Count > 0)
            {
                w.WriteStartArray("acceptance_criteria");
                foreach (var a in acceptance)
                {
                    w.WriteStartObject();
                    w.WriteString("text", a.Text);
                    if (a.Checked is { } c) w.WriteBoolean("checked", c); else w.WriteNull("checked");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            if (pointers.Count > 0)
            {
                w.WriteStartArray("code_pointers");
                foreach (var p in pointers) w.WriteStringValue(p.Text);
                w.WriteEndArray();
            }

            var blockerIds = graph.BlockersOf(item.Id).ToList();
            if (blockerIds.Count > 0)
            {
                w.WriteStartArray("blockers");
                foreach (var bid in blockerIds)
                {
                    var target = graph.Get(bid);
                    w.WriteStartObject();
                    w.WriteString("ref", $"item:{bid}");
                    w.WriteString("short_id", Out.Short(ctx, bid));
                    if (target is not null)
                    {
                        w.WriteString("title", target.Title);
                        if (!string.IsNullOrEmpty(target.Summary)) w.WriteString("summary", target.Summary);
                        w.WriteString("status", target.State.ToWire());
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            if (rc.Design is not null)
            {
                w.WriteStartObject("design");
                w.WriteString("slug", rc.Design.Slug);
                w.WriteString("path", Path.Combine(".knit", "designs", $"{rc.Design.Slug}-design.md"));
                if (!string.IsNullOrEmpty(rc.Design.InheritedFrom))
                    w.WriteString("inherited_from", $"item:{rc.Design.InheritedFrom}");
                else w.WriteNull("inherited_from");
                w.WriteEndObject();
            }

            if (rc.Milestone is not null)
            {
                w.WriteStartObject("milestone");
                w.WriteString("project", rc.Milestone.Project);
                w.WriteString("slug", rc.Milestone.Slug);
                w.WriteString("ref", rc.Milestone.QualifiedSlug);
                w.WriteString("path", Path.Combine(".knit", "projects", rc.Milestone.Project, "milestones", $"{rc.Milestone.Slug}.md"));
                if (!string.IsNullOrEmpty(rc.Milestone.InheritedFrom))
                    w.WriteString("inherited_from", $"item:{rc.Milestone.InheritedFrom}");
                else w.WriteNull("inherited_from");
                w.WriteEndObject();
            }

            if (refs.Count > 0)
            {
                w.WriteStartArray("kb_refs");
                foreach (var r in refs)
                {
                    w.WriteStartObject();
                    w.WriteString("target_text", r.Display);
                    w.WriteString("target_path", r.TargetPath);
                    if (!string.IsNullOrEmpty(r.Bucket)) w.WriteString("bucket", r.Bucket);
                    w.WriteString("expected_path", r.ExpectedPath);
                    if (!string.IsNullOrEmpty(r.Title)) w.WriteString("title", r.Title);
                    if (!string.IsNullOrEmpty(r.Summary)) w.WriteString("summary", r.Summary);
                    if (!string.IsNullOrEmpty(r.Url)) w.WriteString("url", r.Url);
                    if (r.Note is null) w.WriteNull("note"); else w.WriteString("note", r.Note);
                    if (string.IsNullOrEmpty(r.InheritedFromShort)) w.WriteNull("inherited_from");
                    else w.WriteString("inherited_from", $"item:{r.InheritedFromShort}");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            var unresolved = refs.Where(r => !r.Exists).ToList();
            if (unresolved.Count > 0)
            {
                w.WriteStartArray("unresolved_refs");
                foreach (var u in unresolved)
                {
                    w.WriteStartObject();
                    w.WriteString("target_text", u.Display);
                    w.WriteString("target_path", u.TargetPath);
                    w.WriteString("expected_path", u.ExpectedPath);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            var childCount = graph.ChildrenOf(item.Id).Count;
            w.WriteNumber("children_count", childCount);
            w.WriteNumber("comments_total", item.Comments.Count);

            if (full && item.Comments.Count > 0)
            {
                w.WriteStartArray("comments");
                foreach (var c in item.Comments)
                {
                    w.WriteStartObject();
                    w.WriteString("author", c.Author);
                    w.WriteString("date", c.Created.UtcDateTime.ToString("yyyy-MM-dd"));
                    w.WriteString("body", c.Body);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            w.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
