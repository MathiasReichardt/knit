using System.Globalization;
using System.Text;
using Knit.Core.Domain;
using Knit.Core.Refs;

namespace Knit.Core.Frontmatter;

/// <summary>
/// Reads and writes a single item file: frontmatter + body + ## Links
/// table + ## Comments section. Lenient — missing pieces degrade to
/// defaults rather than throwing. Frontmatter no longer carries
/// <c>links:</c> / <c>kb_refs:</c> / <c>blockers:</c> arrays; those
/// relations live in the body <c>## Links</c> table.
/// </summary>
public sealed class ItemFile
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "title", "type", "state", "assignee", "parent",
        "rank", "effort", "due", "priority", "health", "created",
        "summary", "feature", "agent_skills", "tags", "design", "project", "slate", "blocked",
        // Listed so the legacy-drop step below treats them as "known"
        // and the ExtraFrontmatter generosity rule doesn't re-emit them.
        "links", "blockers", "kb_refs",
    };

    /// <summary>
    /// Legacy frontmatter shape. Read silently drops these so the
    /// next write produces the new <c>## Links</c> table without carrying
    /// the old arrays forward. Unknown frontmatter keys are otherwise
    /// preserved verbatim — this set is the explicit, one-time exception.
    /// </summary>
    private static readonly HashSet<string> LegacyDropKeys = new(StringComparer.Ordinal)
    {
        "links", "blockers", "kb_refs",
    };

    private readonly IFrontmatterSerializer _yaml;

    public ItemFile(IFrontmatterSerializer yaml) => _yaml = yaml;

    public Item Read(string content)
    {
        var (fm, body, commentsRaw) = SplitSections(content);
        var fields = _yaml.Parse(fm);

        var id = AsString(fields, "id") ?? string.Empty;
        var title = AsString(fields, "title") ?? string.Empty;
        EnumStrings.TryParseItemType(AsString(fields, "type"), out var type);
        var state = EnumStrings.ParseItemStateStrict(AsString(fields, "state"));
        var assignee = AsString(fields, "assignee");
        var rank = AsString(fields, "rank");
        var effort = AsString(fields, "effort");
        var due = Knit.Core.Domain.DueDates.ParseLenient(AsString(fields, "due"));
        var priority = Knit.Core.Domain.Priorities.ParseLenient(AsString(fields, "priority"));
        var health = Knit.Core.Domain.Healths.ParseLenient(AsString(fields, "health"));
        var created = AsDate(fields, "created");

        // Body table is authoritative for typed links.
        var (bodyClean, linkRows) = ExtractLinksSection(body);
        var parentId = DeriveParentFromRows(linkRows)
            ?? Ref.StripItemPrefix(AsString(fields, "parent"));

        var extra = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
        {
            if (KnownKeys.Contains(k)) continue;
            extra[k] = v;
        }

        var comments = ParseComments(commentsRaw);

        return new Item
        {
            Id = id,
            Title = title,
            Type = type,
            State = state,
            Assignee = assignee,
            Parent = parentId,
            LinkRows = linkRows,
            Rank = rank,
            Effort = effort,
            Due = due,
            Priority = priority,
            Health = health,
            Created = created,
            Body = bodyClean,
            Comments = comments,
            Summary = AsString(fields, "summary"),
            Feature = AsStringList(fields, "feature"),
            AgentSkills = AsStringList(fields, "agent_skills"),
            Tags = AsStringList(fields, "tags"),
            Design = AsString(fields, "design"),
            Project = AsString(fields, "project"),
            Slate = AsString(fields, "slate"),
            BlockedReason = AsBlocked(fields, "blocked"),
            ExtraFrontmatter = extra,
        };
    }

    private static IReadOnlyList<string> AsStringList(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var v)) return [];
        if (v is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        if (v is string s && !string.IsNullOrEmpty(s)) return [s];
        return [];
    }

    private static string? AsBlocked(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var v)) return null;
        return v switch
        {
            null => null,
            bool b => b ? null : null, // boolean blocked values are legacy; require a reason now
            string s when string.IsNullOrEmpty(s) => null,
            _ => v.ToString() is { Length: > 0 } str ? str : null,
        };
    }

    public string Write(Item item)
    {
        var fields = new Dictionary<string, object?>();
        fields["id"] = item.Id;
        fields["title"] = item.Title;
        fields["type"] = item.Type.ToWire();
        fields["state"] = item.State.ToWire();
        if (!string.IsNullOrEmpty(item.Assignee)) fields["assignee"] = item.Assignee;

        // Derived parent path (cheap structural read). Authoritative parent
        // lives in the body table; we still write a stable relative path
        // here when we know enough to build one.
        var parentPath = DeriveParentPath(item);
        if (!string.IsNullOrEmpty(parentPath)) fields["parent"] = parentPath;

        if (!string.IsNullOrEmpty(item.Rank)) fields["rank"] = item.Rank;
        if (!string.IsNullOrEmpty(item.Effort)) fields["effort"] = item.Effort;
        if (item.Due is { } due) fields["due"] = Knit.Core.Domain.DueDates.ToWire(due);
        // Only written when it differs from the Normal default — keeps
        // frontmatter clean for the common case.
        if (item.Priority != Knit.Core.Domain.Priority.Normal) fields["priority"] = item.Priority.ToWire();
        if (item.Health is { } health) fields["health"] = health.ToWire();
        fields["created"] = item.Created.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(item.Summary)) fields["summary"] = item.Summary;
        if (item.Feature.Count > 0)
            fields["feature"] = item.Feature.Select(s => (object?)s).ToList();
        if (item.AgentSkills.Count > 0)
            fields["agent_skills"] = item.AgentSkills.Select(s => (object?)s).ToList();
        if (item.Tags.Count > 0)
            fields["tags"] = item.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(item.Design)) fields["design"] = item.Design;
        if (!string.IsNullOrEmpty(item.Project)) fields["project"] = item.Project;
        if (!string.IsNullOrEmpty(item.Slate)) fields["slate"] = item.Slate;
        if (!string.IsNullOrEmpty(item.BlockedReason))
            fields["blocked"] = item.BlockedReason;

        // Append unknown keys at the end, preserving insertion order.
        foreach (var (k, v) in item.ExtraFrontmatter)
        {
            if (LegacyDropKeys.Contains(k)) continue;
            if (!fields.ContainsKey(k)) fields[k] = v;
        }

        var fm = _yaml.Write(fields).TrimEnd();

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append(fm);
        sb.Append("\n---\n\n");

        // Render the body. A leading `# h1` (if present) wins the very
        // first slot — readers and previewers expect the heading to come
        // before any structural section. The ## Links table then follows,
        // then the rest of the body, then ## Comments at the end.
        var bodyText = item.Body.TrimEnd();
        var (h1Block, restBody) = SplitLeadingH1(bodyText);
        restBody = restBody.Trim('\n');
        var linkTable = LinksTable.Render(item.LinkRows);

        if (h1Block.Length > 0)
        {
            sb.Append(h1Block);
            sb.Append("\n\n");
        }
        if (linkTable.Length > 0)
        {
            sb.Append(linkTable);
            if (restBody.Length > 0)
            {
                sb.Append('\n');
                sb.Append(restBody);
                sb.Append('\n');
            }
        }
        else if (restBody.Length > 0)
        {
            sb.Append(restBody);
            sb.Append('\n');
        }

        if (item.Comments.Count > 0)
        {
            sb.Append("\n## Comments\n\n");
            foreach (var c in item.Comments)
            {
                sb.Append($"#### `{c.Id}` · {c.Author} · {c.Created.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n");
                sb.Append(c.Body.TrimEnd());
                sb.Append("\n\n");
            }
        }
        return sb.ToString();
    }

    private static (string h1Block, string rest) SplitLeadingH1(string body)
    {
        if (string.IsNullOrEmpty(body)) return ("", body);
        var s = body.Replace("\r\n", "\n");
        var lines = s.Split('\n');
        var i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length) return ("", body);
        if (!lines[i].StartsWith("# ", StringComparison.Ordinal)) return ("", body);

        var h1End = i + 1;
        while (h1End < lines.Length && string.IsNullOrWhiteSpace(lines[h1End])) h1End++;
        var h1Block = string.Join('\n', lines[..h1End]).TrimEnd('\n').TrimStart('\n');
        var rest = h1End < lines.Length ? string.Join('\n', lines[h1End..]) : "";
        return (h1Block, rest);
    }

    private static string? DeriveParentPath(Item item)
    {
        var parentRow = item.LinkRows.FirstOrDefault(r => r.Rel == LinkRel.Parent);
        if (parentRow is not null) return parentRow.TargetPath;
        return null;
    }

    private static string? DeriveParentFromRows(IReadOnlyList<LinkRow> rows)
    {
        var parentRow = rows.FirstOrDefault(r => r.Rel == LinkRel.Parent);
        if (parentRow is null) return null;
        return LinkPath.ItemIdFromTarget(parentRow.TargetPath);
    }

    private static (string bodyClean, IReadOnlyList<LinkRow> rows) ExtractLinksSection(string body)
    {
        var rows = LinksTable.Parse(body);
        // Strip the section so it isn't duplicated when the body is
        // re-rendered from the rows.
        return rows.Count == 0 ? (body, rows) : (LinksTable.Strip(body), rows);
    }

    private static int FindNextH2(string text, int from)
    {
        var idx = from;
        while (idx < text.Length)
        {
            var nl = text.IndexOf('\n', idx);
            var end = nl < 0 ? text.Length : nl;
            var line = text[idx..end];
            if (line.StartsWith("## ", StringComparison.Ordinal)) return idx;
            if (nl < 0) break;
            idx = nl + 1;
        }
        return -1;
    }

    private static (string frontmatter, string body, string comments) SplitSections(string content)
    {
        var s = content.Replace("\r\n", "\n");
        if (!s.StartsWith("---", StringComparison.Ordinal))
            return (string.Empty, s, string.Empty);

        var endIdx = s.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return (string.Empty, s, string.Empty);

        var fm = s.Substring(3, endIdx - 3).TrimStart('\n');
        var afterFm = s[(endIdx + 4)..].TrimStart('\n');

        var commentsIdx = FindCommentsHeader(afterFm);
        if (commentsIdx < 0) return (fm, afterFm.TrimEnd() + "\n", string.Empty);

        var body = afterFm[..commentsIdx].TrimEnd() + "\n";
        var comments = afterFm[commentsIdx..];
        return (fm, body, comments);
    }

    private static int FindCommentsHeader(string text)
    {
        var idx = 0;
        while (idx < text.Length)
        {
            var line = idx;
            var nl = text.IndexOf('\n', idx);
            var end = nl < 0 ? text.Length : nl;
            var lineStr = text[line..end];
            if (lineStr.StartsWith("## Comments", StringComparison.OrdinalIgnoreCase))
                return line;
            if (nl < 0) break;
            idx = nl + 1;
        }
        return -1;
    }

    private static IReadOnlyList<Comment> ParseComments(string section)
    {
        if (string.IsNullOrWhiteSpace(section)) return [];
        var lines = section.Replace("\r\n", "\n").Split('\n');

        var result = new List<Comment>();
        Comment? current = null;
        var bodyLines = new List<string>();
        var sawHeader = false;

        void Flush()
        {
            if (current is null) return;
            var body = string.Join('\n', bodyLines).Trim();
            result.Add(current with { Body = body });
            current = null;
            bodyLines.Clear();
        }

        foreach (var line in lines)
        {
            if (!sawHeader)
            {
                if (line.StartsWith("## Comments", StringComparison.OrdinalIgnoreCase))
                    sawHeader = true;
                continue;
            }

            if (line.StartsWith("#### ", StringComparison.Ordinal))
            {
                Flush();
                if (TryParseCommentHeader(line, out var c))
                {
                    current = c;
                }
            }
            else if (current is not null)
            {
                bodyLines.Add(line);
            }
        }
        Flush();
        return result;
    }

    private static bool TryParseCommentHeader(string header, out Comment comment)
    {
        comment = new Comment("", "", DateTimeOffset.UnixEpoch, "");
        if (!header.StartsWith("#### ", StringComparison.Ordinal)) return false;
        var rest = header[5..];

        var tickStart = rest.IndexOf('`');
        if (tickStart < 0) return false;
        var tickEnd = rest.IndexOf('`', tickStart + 1);
        if (tickEnd < 0) return false;
        var id = rest.Substring(tickStart + 1, tickEnd - tickStart - 1);
        var afterId = rest[(tickEnd + 1)..];

        var parts = afterId.Split('·', StringSplitOptions.TrimEntries);
        var author = parts.Length > 1 ? parts[1] : "";
        var dateStr = parts.Length > 2 ? parts[2].Replace(" UTC", "").Trim() : "";
        var created = DateTimeOffset.UnixEpoch;
        if (DateTimeOffset.TryParseExact(dateStr, "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            created = parsed;
        }

        comment = new Comment(id, author, created, "");
        return true;
    }

    private static string? AsString(IReadOnlyDictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static DateTimeOffset AsDate(IReadOnlyDictionary<string, object?> dict, string key)
    {
        var s = AsString(dict, key);
        if (string.IsNullOrEmpty(s)) return DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }
        return DateTimeOffset.UtcNow;
    }
}
