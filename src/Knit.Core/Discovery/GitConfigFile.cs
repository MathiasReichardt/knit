using System.Text;

namespace Knit.Core.Discovery;

/// <param name="Section">Lower-cased, as git compares it.</param>
/// <param name="Subsection">As written — git compares a quoted subsection case-sensitively. Null for none.</param>
/// <param name="Key">Lower-cased.</param>
/// <param name="Value">Unquoted and unescaped; <c>"true"</c> for a key without <c>=</c>.</param>
public sealed record GitConfigEntry(string Section, string? Subsection, string Key, string Value);

/// <summary>
/// A git <c>config</c> file read line by line — no git shell-out. Covers
/// <c>[section]</c>, <c>[section "sub"]</c>, the legacy <c>[section.sub]</c>,
/// <c>#</c> / <c>;</c> comments and quoted values. <c>include</c> is not
/// followed: the reads here (<c>core.bare</c>, <c>remote.origin.url</c>) live
/// in the repo's own file.
/// </summary>
public static class GitConfigFile
{
    /// <summary>Every entry in file order; empty when the file is missing or unreadable.</summary>
    public static IReadOnlyList<GitConfigEntry> Read(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        return Parse(lines);
    }

    public static IReadOnlyList<GitConfigEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<GitConfigEntry>();
        string? section = null;
        string? subsection = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[')
            {
                (section, subsection) = Header(line);
                continue;
            }
            if (section is null) continue;
            var eq = line.IndexOf('=');
            var key = (eq < 0 ? StripComment(line) : line[..eq]).Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            entries.Add(new GitConfigEntry(section, subsection, key, eq < 0 ? "true" : Value(line[(eq + 1)..])));
        }
        return entries;
    }

    /// <summary>The first value of <c>section."subsection".key</c>, or null.</summary>
    public static string? Get(IReadOnlyList<GitConfigEntry> entries, string section, string? subsection, string key) =>
        entries.FirstOrDefault(e =>
            e.Section == section && e.Subsection == subsection && e.Key == key)?.Value;

    private static (string? Section, string? Subsection) Header(string line)
    {
        var close = line.LastIndexOf(']');
        if (close < 0) return (null, null);
        var inner = line[1..close].Trim();
        var quote = inner.IndexOf('"');
        if (quote >= 0)
        {
            var end = inner.LastIndexOf('"');
            if (end <= quote) return (null, null);
            var sub = inner[(quote + 1)..end].Replace("\\\"", "\"").Replace("\\\\", "\\");
            return (inner[..quote].Trim().ToLowerInvariant(), sub);
        }
        // Legacy `[section.sub]`: the subsection is lower-cased like the section.
        var dot = inner.IndexOf('.');
        return dot < 0
            ? (inner.ToLowerInvariant(), null)
            : (inner[..dot].ToLowerInvariant(), inner[(dot + 1)..].ToLowerInvariant());
    }

    private static string StripComment(string s)
    {
        var at = s.IndexOfAny(['#', ';']);
        return at < 0 ? s : s[..at];
    }

    private static string Value(string s)
    {
        var sb = new StringBuilder();
        var quoted = false;
        var trailingSpace = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { quoted = !quoted; trailingSpace = 0; continue; }
            if (!quoted && c is '#' or ';') break;
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[++i];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', 'b' => '\b', _ => n });
                trailingSpace = 0;
                continue;
            }
            sb.Append(c);
            trailingSpace = !quoted && char.IsWhiteSpace(c) ? trailingSpace + 1 : 0;
        }
        if (trailingSpace > 0) sb.Length -= trailingSpace;
        return sb.ToString().TrimStart();
    }
}
