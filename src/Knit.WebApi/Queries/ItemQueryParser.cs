using Knit.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace Knit.WebApi.Queries;

internal static class ItemQueryParser
{
    public static (ItemQuery? query, string? error) Parse(IQueryCollection q)
    {
        var iq = new ItemQuery();

        var (t, e1) = ParseTypes(q["type"]);
        if (e1 is not null) return (null, e1);
        iq.Types = t;

        var (xt, e2) = ParseTypes(q["excludeType"]);
        if (e2 is not null) return (null, e2);
        iq.ExcludeTypes = xt;

        var (s, e3) = ParseStates(q["state"]);
        if (e3 is not null) return (null, e3);
        iq.States = s;

        var (xs, e4) = ParseStates(q["excludeState"]);
        if (e4 is not null) return (null, e4);
        iq.ExcludeStates = xs;

        iq.Open = ParseBool(q["open"]);
        if (iq.Open && (iq.States?.Count > 0 || iq.ExcludeStates?.Count > 0))
            return (null, "open cannot be combined with state / excludeState");

        iq.Assignee = NullIfEmpty(q["assignee"].ToString());
        iq.Mine = ParseBool(q["mine"]);
        iq.Unassigned = ParseBool(q["unassigned"]);
        iq.Under = NullIfEmpty(q["under"].ToString());
        iq.Ready = ParseBool(q["ready"]);
        iq.Query = NullIfEmpty(q["query"].ToString());
        iq.Unparented = ParseBool(q["unparented"]);

        var blockedRaw = q["blocked"].ToString();
        if (!string.IsNullOrWhiteSpace(blockedRaw))
        {
            switch (blockedRaw.Trim().ToLowerInvariant())
            {
                case "only": iq.Blocked = BlockedFilter.Only; break;
                case "hide": iq.Blocked = BlockedFilter.Hide; break;
                case "any": iq.Blocked = BlockedFilter.Any; break;
                default: return (null, $"unknown blocked filter: {blockedRaw} (expected only|hide|any)");
            }
        }

        iq.Overdue = ParseBool(q["overdue"]);
        var dueBeforeRaw = q["dueBefore"].ToString();
        if (!string.IsNullOrWhiteSpace(dueBeforeRaw))
        {
            if (!DueDates.TryParse(dueBeforeRaw, out var b)) return (null, $"invalid dueBefore date: {dueBeforeRaw} (expected YYYY-MM-DD)");
            iq.DueBefore = b;
        }
        var dueAfterRaw = q["dueAfter"].ToString();
        if (!string.IsNullOrWhiteSpace(dueAfterRaw))
        {
            if (!DueDates.TryParse(dueAfterRaw, out var a)) return (null, $"invalid dueAfter date: {dueAfterRaw} (expected YYYY-MM-DD)");
            iq.DueAfter = a;
        }

        iq.Tags = ParseTags(q["tag"]);

        var slateRaw = NullIfEmpty(q["slate"].ToString());
        iq.Slate = slateRaw is null ? null : Knit.Core.Ids.Slug.From(slateRaw);
        iq.NoSlate = ParseBool(q["noSlate"]);
        if (iq.Slate is not null && iq.NoSlate)
            return (null, "slate cannot be combined with noSlate");

        iq.Milestone = NullIfEmpty(q["milestone"].ToString());
        iq.Project = NullIfEmpty(q["project"].ToString());

        var minPriorityRaw = q["minPriority"].ToString();
        if (!string.IsNullOrWhiteSpace(minPriorityRaw))
        {
            if (!Priorities.TryParse(minPriorityRaw, out var mp))
                return (null, $"unknown priority: {minPriorityRaw} (expected someday|normal|important|critical)");
            iq.MinPriority = mp;
        }

        var sortRaw = q["sort"].ToString();
        if (!string.IsNullOrWhiteSpace(sortRaw))
        {
            var (sk, sd, sortErr) = ParseSort(sortRaw);
            if (sortErr is not null) return (null, sortErr);
            iq.SortKey = sk;
            iq.SortDir = sd;
        }

        iq.TreeSortParam = NullIfEmpty(q["treeSort"].ToString());

        var modeRaw = q["filterMode"].ToString();
        if (!string.IsNullOrWhiteSpace(modeRaw))
        {
            switch (modeRaw.Trim().ToLowerInvariant())
            {
                case "context": iq.FilterMode = TreeFilterMode.Context; break;
                case "flat": iq.FilterMode = TreeFilterMode.Flat; break;
                default: return (null, $"unknown filterMode: {modeRaw}");
            }
        }

        return (iq, null);
    }

    // Free-form tags: repeatable and/or comma-separated. Case-insensitive
    // membership (the AND check in the runner uses OrdinalIgnoreCase).
    private static HashSet<string>? ParseTags(Microsoft.Extensions.Primitives.StringValues raw)
    {
        if (raw.Count == 0) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            foreach (var token in s!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(token);
        }
        return set.Count == 0 ? null : set;
    }

    private static (HashSet<ItemType>?, string?) ParseTypes(Microsoft.Extensions.Primitives.StringValues raw)
    {
        if (raw.Count == 0) return (null, null);
        var set = new HashSet<ItemType>();
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            foreach (var token in s!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!EnumStrings.TryParseItemType(token, out var t)) return (null, $"unknown type: {token}");
                set.Add(t);
            }
        }
        return (set.Count == 0 ? null : set, null);
    }

    private static (HashSet<ItemState>?, string?) ParseStates(Microsoft.Extensions.Primitives.StringValues raw)
    {
        if (raw.Count == 0) return (null, null);
        var set = new HashSet<ItemState>();
        foreach (var s in raw)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            foreach (var token in s!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, "blocked", StringComparison.OrdinalIgnoreCase))
                    return (null, "'blocked' is no longer a valid state; use ?blocked=only|hide for the orthogonal flag");
                if (!EnumStrings.TryParseItemState(token, out var st)) return (null, $"unknown state: {token}");
                set.Add(st);
            }
        }
        return (set.Count == 0 ? null : set, null);
    }

    private static bool ParseBool(Microsoft.Extensions.Primitives.StringValues raw)
    {
        if (raw.Count == 0) return false;
        var v = raw.ToString().Trim().ToLowerInvariant();
        return v == "1" || v == "true" || v == "yes" || v == "";
    }

    private static (SortKey, SortDir, string?) ParseSort(string raw)
    {
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        var keyRaw = parts[0].ToLowerInvariant();
        var dir = SortDir.Asc;
        if (parts.Length == 2)
        {
            var dirRaw = parts[1].ToLowerInvariant();
            if (dirRaw == "desc") dir = SortDir.Desc;
            else if (dirRaw == "asc") dir = SortDir.Asc;
            else return (default, default, $"unknown sort direction: {parts[1]}");
        }
        SortKey key;
        switch (keyRaw)
        {
            case "tree": key = SortKey.Tree; break;
            case "rank": key = SortKey.Rank; break;
            case "type": key = SortKey.Type; break;
            case "state": key = SortKey.State; break;
            case "assignee": key = SortKey.Assignee; break;
            case "created": key = SortKey.Created; break;
            case "due": key = SortKey.Due; break;
            case "priority": key = SortKey.Priority; break;
            default: return (default, default, $"unknown sort key: {parts[0]}");
        }
        return (key, dir, null);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
