using Knit.Core.Refs;
using Knit.Data.Search;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Knit.WebApi.Endpoints;

public static class SearchEndpoints
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 30;

    public static void MapSearch(this WebApplication app)
    {
        app.MapGet("/api/search", (HttpRequest req) =>
        {
            var searcher = app.Services.GetService<ISearcher>();
            if (searcher is null)
                return Results.Json(new SearchResultDto(false, []), ApiJsonContext.Default.SearchResultDto);

            var q = (req.Query["q"].ToString() ?? "").Trim();
            var kindStr = (req.Query["kind"].ToString() ?? "all").Trim().ToLowerInvariant();
            var kind = kindStr switch
            {
                "item" => SearchKind.Item,
                "kb" => SearchKind.Kb,
                "design" => SearchKind.Design,
                "project" => SearchKind.Project,
                _ => SearchKind.All,
            };
            var limit = ParseLimit(req.Query["limit"]);

            if (q.Length == 0)
                return Results.Json(new SearchResultDto(false, []), ApiJsonContext.Default.SearchResultDto);

            // ref:bucket:slug / bucket:slug shortcut — direct lookup so a
            // freshly-created KB entry surfaces immediately.
            var maybeRef = TryParseRef(q);
            if (maybeRef is not null)
            {
                var hit = searcher.LookupRef(maybeRef);
                var list = hit is null ? Array.Empty<SearchHitDto>() : new[] { ToDto(hit) };
                return Results.Json(new SearchResultDto(false, list), ApiJsonContext.Default.SearchResultDto);
            }

            // Fetch one past the limit so we can tell the client the list was
            // clipped without the searcher exposing a separate total count.
            var hits = searcher.Query(q, kind, limit + 1);
            var clipped = hits.Count > limit;
            var shown = clipped ? limit : hits.Count;
            var dtos = new List<SearchHitDto>(shown);
            for (var i = 0; i < shown; i++) dtos.Add(ToDto(hits[i]));
            return Results.Json(new SearchResultDto(clipped, dtos), ApiJsonContext.Default.SearchResultDto);
        });
    }

    private static SearchHitDto ToDto(SearchHit h) =>
        new(WireKind(h.Kind), h.Id, h.Title, h.Snippet, FieldName(h.Field), h.BodyOffset);

    private static string WireKind(SearchKind k) => k switch
    {
        SearchKind.Item => "item",
        SearchKind.Kb => "kb",
        SearchKind.Design => "design",
        SearchKind.Project => "project",
        _ => "item",
    };

    private static string FieldName(MatchField f) => f switch
    {
        MatchField.Title => "title",
        MatchField.Body => "body",
        MatchField.Comment => "comment",
        MatchField.Tags => "tags",
        MatchField.Source => "source",
        MatchField.Link => "link",
        _ => "body",
    };

    private static int ParseLimit(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DefaultLimit;
        if (!int.TryParse(raw, out var n)) return DefaultLimit;
        if (n <= 0) return DefaultLimit;
        return Math.Min(n, MaxLimit);
    }

    private static Ref? TryParseRef(string q)
    {
        var s = q.StartsWith("ref:", StringComparison.OrdinalIgnoreCase) ? q[4..] : q;
        if (s.IndexOf(' ') >= 0) return null;
        var parsed = Ref.Parse(s);
        return parsed.IsError ? null : parsed.GetValueOrThrow();
    }
}
