using System.Text;
using Knit.Core.Discovery;
using Knit.Core.IO;

namespace Knit.Core.Slates;

public enum SlateStatus
{
    Planned,
    Active,
    Archived,
}

public static class SlateStatuses
{
    public static string ToWire(this SlateStatus s) => s switch
    {
        SlateStatus.Planned => "planned",
        SlateStatus.Active => "active",
        SlateStatus.Archived => "archived",
        _ => "planned",
    };

    public static bool TryParse(string? s, out SlateStatus status)
    {
        switch ((s ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "planned": status = SlateStatus.Planned; return true;
            case "active": status = SlateStatus.Active; return true;
            case "archived": status = SlateStatus.Archived; return true;
            default: status = SlateStatus.Planned; return false;
        }
    }

    /// <summary>Unknown / missing status cells degrade to <c>planned</c>.</summary>
    public static SlateStatus ParseLenient(string? s) =>
        TryParse(s, out var status) ? status : SlateStatus.Planned;
}

public sealed record SlateRow(string Name, SlateStatus Status, string? Notes);

/// <summary>
/// Parses and renders <c>.knit/slates.md</c> — the authoritative slate
/// registry: a human-editable markdown table whose row order is the
/// display order. Same precedent as the <c>## Links</c> body table:
/// lenient on parse, canonical on write. The file is created lazily by
/// the first <c>knit slate add</c>; a missing file degrades to derived
/// behavior (slate values on items keep working, ordering/archive is lost).
/// </summary>
public static class SlateRegistry
{
    public const string FileName = "slates.md";

    public static string PathFor(string root) =>
        Path.Combine(RepoDiscovery.KnitDir(root), FileName);

    public static IReadOnlyList<SlateRow> Load(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path)) return [];
        return Parse(AtomicFile.ReadAllText(path));
    }

    public static void Save(string root, IReadOnlyList<SlateRow> rows) =>
        AtomicFile.WriteAllText(PathFor(root), Render(rows));

    /// <summary>
    /// Extract the slate rows from the file content. Missing table ⇒ empty
    /// list. The header row is identified by its first cell being
    /// <c>Slate</c>; separator rows are skipped; a duplicate name keeps the
    /// first row (callers may log via <paramref name="warnings"/>).
    /// </summary>
    public static IReadOnlyList<SlateRow> Parse(string content, IList<string>? warnings = null)
    {
        var rows = new List<SlateRow>();
        if (string.IsNullOrEmpty(content)) return rows;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sawHeaderRow = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || !line.StartsWith('|')) continue;

            var cells = SplitRow(line);
            if (cells.Count == 0) continue;

            if (cells[0].Equals("Slate", StringComparison.OrdinalIgnoreCase))
            {
                sawHeaderRow = true;
                continue;
            }
            if (cells.All(c => c.Length == 0 || c.All(ch => ch == '-' || ch == ':')))
                continue; // separator
            if (!sawHeaderRow)
            {
                warnings?.Add($"slates.md: row before header skipped: {line}");
                continue;
            }

            var name = cells[0];
            if (name.Length == 0)
            {
                warnings?.Add($"slates.md: row with empty slate name skipped: {line}");
                continue;
            }
            if (!seen.Add(name))
            {
                warnings?.Add($"slates.md: duplicate slate '{name}'; first row wins");
                continue;
            }
            var status = SlateStatuses.ParseLenient(cells.Count > 1 ? cells[1] : null);
            var notes = cells.Count > 2 && cells[2].Length > 0 ? cells[2] : null;
            rows.Add(new SlateRow(name, status, notes));
        }
        return rows;
    }

    /// <summary>
    /// Render the registry as a padded markdown table under a
    /// <c># Slates</c> heading. An empty registry still renders the
    /// heading + header row so the file stays self-describing.
    /// </summary>
    public static string Render(IReadOnlyList<SlateRow> rows)
    {
        const string h1 = "Slate";
        const string h2 = "Status";
        const string h3 = "Notes";

        var nameStrs = rows.Select(r => r.Name).ToArray();
        var statusStrs = rows.Select(r => r.Status.ToWire()).ToArray();
        var noteStrs = rows.Select(r => r.Notes ?? string.Empty).ToArray();

        var w1 = Math.Max(h1.Length, nameStrs.Length == 0 ? 0 : nameStrs.Max(s => s.Length));
        var w2 = Math.Max(h2.Length, statusStrs.Length == 0 ? 0 : statusStrs.Max(s => s.Length));
        var w3 = Math.Max(h3.Length, noteStrs.Length == 0 ? 0 : noteStrs.Max(s => s.Length));

        var sb = new StringBuilder();
        sb.Append("# Slates\n\n");
        sb.Append('|').Append(' ').Append(h1.PadRight(w1)).Append(' ');
        sb.Append('|').Append(' ').Append(h2.PadRight(w2)).Append(' ');
        sb.Append('|').Append(' ').Append(h3.PadRight(w3)).Append(' ').Append("|\n");
        sb.Append('|').Append(new string('-', w1 + 2));
        sb.Append('|').Append(new string('-', w2 + 2));
        sb.Append('|').Append(new string('-', w3 + 2)).Append("|\n");
        for (var i = 0; i < rows.Count; i++)
        {
            sb.Append('|').Append(' ').Append(nameStrs[i].PadRight(w1)).Append(' ');
            sb.Append('|').Append(' ').Append(statusStrs[i].PadRight(w2)).Append(' ');
            sb.Append('|').Append(' ').Append(noteStrs[i].PadRight(w3)).Append(' ').Append("|\n");
        }
        return sb.ToString();
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }
}
