using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class ShowCommand
{
    public static Command Build()
    {
        var cmd = new Command("show", "Show an item: title, frontmatter, body sections (h2-keyed), and comments");
        var idArg = new Argument<string>("id") { Description = "Item id or unique prefix" };
        var pathOnlyOpt = new Option<bool>("--path-only") { Description = "Print only the repo-relative path to the item's index.md" };
        var watchOpt = Watch.Option();
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(pathOnlyOpt);
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            var idOrPrefix = parse.GetValue(idArg)!;
            var pathOnly = parse.GetValue(pathOnlyOpt);
            if (pathOnly)
            {
                var file = ctx.Store.FindItemFile(idOrPrefix);
                if (file is null)
                    return Out.Error($"item not found or prefix is ambiguous: {idOrPrefix}", ErrorCode.NotFound, json);
                var rel = Path.GetRelativePath(ctx.Root, file).Replace('\\', '/');
                if (json) Console.WriteLine($"{{\"path\": \"{rel}\"}}");
                else Console.WriteLine(rel);
                return 0;
            }

            var (item, err) = ItemUtils.Resolve(ctx, idOrPrefix);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            if (json)
            {
                ShowJson.Write(item, ctx.Root, ctx.EffectiveSlate(item.Id),
                    LinkGraph.HeldBy(item, ctx.Store.Read)?.Id, ctx.EffectiveMilestone(item.Id));
                return 0;
            }

            IRenderable Render()
            {
                var (refreshed, _) = ItemUtils.Resolve(ctx, idOrPrefix);
                return BuildHuman(ctx, refreshed ?? item);
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        });
        return cmd;
    }

    private static IRenderable BuildHuman(CliContext ctx, Item item)
    {
        var pieces = new List<IRenderable>
        {
            new Rule($"[bold]{item.Type.Icon()} {Markup.Escape(item.Title)}[/]").LeftJustified(),
            new Markup($"[dim]{Markup.Escape(item.Id)}[/]"),
            Text.Empty,
        };

        var t = new Table().HideHeaders().Border(TableBorder.None);
        t.AddColumns("k", "v");
        AddRow(t, "type", $"{item.Type.Icon()} {item.Type.ToWire()}");
        AddRow(t, "state", $"{item.State.Glyph()} {item.State.ToWire()}");
        if (item.BlockedSummary is { } blocked)
            AddRow(t, "blocked", $"[red]⊘[/] {Markup.Escape(blocked)}");
        if (LinkGraph.HeldBy(item, ctx.Store.Read) is { } holder)
            AddRow(t, "held", $"[red]⊘[/][dim]↑[/] blocked parent {Out.Titled(ctx, holder)}");
        if (!string.IsNullOrEmpty(item.Assignee)) AddRow(t, "assignee", Markup.Escape(item.Assignee));
        AddRow(t, "priority", Markup.Escape(PriorityFormat.Cell(item.Priority)));
        if (item.Due is not null)
            AddRow(t, "due", DueFormat.Cell(item.Due, item.State, DateOnly.FromDateTime(DateTime.Now), ctx.Config.DueSoonDays));
        if (item.Health is { } health) AddRow(t, "health", Markup.Escape(health.ToWire()));
        if (!string.IsNullOrEmpty(item.Parent)) AddRow(t, "parent", Out.Titled(ctx, item.Parent));
        if (!string.IsNullOrEmpty(item.Slate)) AddRow(t, "slate", Markup.Escape(item.Slate));
        if (item.Tags.Count > 0) AddRow(t, "tags", Markup.Escape(string.Join(", ", item.Tags)));
        if (!string.IsNullOrEmpty(item.Design)) AddRow(t, "design", Markup.Escape(item.Design));
        if (!string.IsNullOrEmpty(item.Project)) AddRow(t, "project", Markup.Escape(item.Project));
        if (!string.IsNullOrEmpty(item.Rank)) AddRow(t, "rank", Markup.Escape(item.Rank));
        if (!string.IsNullOrEmpty(item.Effort)) AddRow(t, "effort", Markup.Escape(item.Effort));
        pieces.Add(t);

        if (item.LinkRows.Count > 0)
        {
            pieces.Add(Text.Empty);
            var links = new Table().Title("[bold]Links[/]").Border(TableBorder.Rounded);
            links.AddColumn("[dim]Rel[/]");
            links.AddColumn("[dim]Target[/]");
            links.AddColumn("[dim]Notes[/]");
            foreach (var r in item.LinkRows)
            {
                links.AddRow(
                    Markup.Escape(LinkRels.ToWire(r.Rel)),
                    $"{Markup.Escape(r.TargetText)} [dim]{Markup.Escape(r.TargetPath)}[/]",
                    Markup.Escape(r.Notes ?? ""));
            }
            pieces.Add(links);
        }

        var sections = BodySections.Split(item.Body);
        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(item.Body))
        {
            // Defensive — Split should always cover non-empty body via "body" key.
            pieces.Add(Text.Empty);
            pieces.Add(new Panel(new Text(item.Body.TrimEnd())).Header("Body").Expand());
        }
        else
        {
            foreach (var (key, value) in sections)
            {
                // Empty sections are shown, not hidden: a heading nobody
                // filled is either work to do or a template section to delete
                // (`knit edit --delete-section`), and hiding it is how they
                // survive unnoticed in the first place.
                pieces.Add(Text.Empty);
                pieces.Add(new Panel(string.IsNullOrWhiteSpace(value)
                        ? new Markup("[dim](empty)[/]")
                        : new Text(value))
                    .Header($" {Markup.Escape(PrettifyKey(key))} ")
                    .Expand());
            }
        }

        if (item.Comments.Count > 0)
        {
            pieces.Add(Text.Empty);
            pieces.Add(new Rule("[bold]Comments[/]").LeftJustified());
            foreach (var c in item.Comments)
            {
                pieces.Add(new Rule(
                    $"[dim]{c.Created.UtcDateTime:yyyy-MM-dd HH:mm} UTC · {Markup.Escape(c.Author)}[/]")
                    .LeftJustified());
                pieces.Add(new Text(c.Body.TrimEnd()));
                pieces.Add(Text.Empty);
            }
        }

        return new Rows(pieces);
    }

    /// <summary>
    /// Add a row to the show table. The <paramref name="value"/> is treated
    /// as Spectre markup — callers must <see cref="Markup.Escape(string)"/>
    /// any user-supplied text portion before concatenating in icon / glyph
    /// markup. Escaping the whole string would also escape intentional
    /// <c>[color]</c> tags coming from the icon helpers, rendering them
    /// as literal text.
    /// </summary>
    private static void AddRow(Table t, string key, string value)
        => t.AddRow($"[dim]{key}[/]", value);

    private static string PrettifyKey(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        }
        return string.Join(' ', parts);
    }
}
