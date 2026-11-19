using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Domain;
using Knit.Data.Search;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class SearchCommand
{
    public static Command Build()
    {
        var cmd = new Command("search", "Full-text search over items, KB entries, design docs and project docs");
        var queryArg = new Argument<string>("query") { Description = "Words to search for (substring, case-insensitive)" };
        var kindOpt = new Option<string>("--kind")
        {
            DefaultValueFactory = _ => "all",
            Description = "Restrict to one corpus: all | item | kb | design | project",
        };
        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(kindOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var kindText = (parse.GetValue(kindOpt) ?? "all").Trim().ToLowerInvariant();
            if (!TryParseKind(kindText, out var kind))
                return Out.Error($"unknown --kind '{kindText}' (all | item | kb | design | project)", ErrorCode.BadArgument, json);

            var query = parse.GetValue(queryArg)!;
            // Match the web's 2-char floor: a 1-char substring is all noise.
            if (query.Trim().Length < 2)
                return Out.Error("query must be at least 2 characters", ErrorCode.BadArgument, json);
            // Same shared scanner the web uses.
            var searcher = new LiveScanSearcher(ctx.Root, ctx.Yaml);
            var found = searcher.Query(query, kind, int.MaxValue);

            var hits = new List<JsonSearchHit>(found.Count);
            foreach (var h in found)
            {
                if (h.Kind == SearchKind.Item)
                {
                    var item = ctx.Store.Read(h.Id);
                    if (item is null) continue;
                    hits.Add(new JsonSearchHit(
                        "item", item.Id, item.Type.ToWire(), item.State.ToWire(),
                        item.Title, h.Snippet));
                    continue;
                }
                hits.Add(new JsonSearchHit(
                    WireKind(h.Kind), Address(h), null, null, h.Title, h.Snippet));
            }

            if (json)
            {
                Out.Json(new JsonSearchResults(hits), AgentJsonContext.Default.JsonSearchResults);
                return 0;
            }

            if (hits.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim](no matches)[/]");
                return 0;
            }
            foreach (var h in hits)
            {
                // Items are the implicit kind — no tag, so doc hits stand out.
                var addr = h.Kind == "item" ? Out.Short(ctx, h.Id) : h.Id;
                var tag = h.Kind == "item" ? "" : $"  [dim]{h.Kind}[/]";
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(addr)}[/]  {Markup.Escape(h.Title)}{tag}");
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(h.Snippet)}[/]");
            }
            return 0;
        });
        return cmd;
    }

    private static bool TryParseKind(string s, out SearchKind kind)
    {
        switch (s)
        {
            case "" or "all": kind = SearchKind.All; return true;
            case "item": kind = SearchKind.Item; return true;
            case "kb": kind = SearchKind.Kb; return true;
            case "design": kind = SearchKind.Design; return true;
            case "project": kind = SearchKind.Project; return true;
            default: kind = SearchKind.All; return false;
        }
    }

    private static string WireKind(SearchKind k) => k switch
    {
        SearchKind.Kb => "kb",
        SearchKind.Design => "design",
        SearchKind.Project => "project",
        _ => "item",
    };

    /// <summary>
    /// The string a reader can act on: KB ids are already <c>bucket:slug</c>
    /// refs, design slugs become <c>design:slug</c> (both resolve via
    /// <c>knit ref show</c>); project docs keep their
    /// <c>&lt;project&gt;/&lt;doc-path&gt;</c> address — there is no ref form
    /// for a doc below the folder root.
    /// </summary>
    private static string Address(SearchHit h) => h.Kind switch
    {
        SearchKind.Design => $"design:{h.Id}",
        _ => h.Id,
    };
}
