using System.Text.RegularExpressions;
using Knit.Core.Ids;

namespace Knit.Core.Refs;

/// <summary>An h2/h3 heading with its anchor. <paramref name="Line"/> and
/// <paramref name="EndLine"/> are 0-based, inclusive: the section runs to the
/// next heading of the same or a higher level, subsections included.
/// <paramref name="Suffixed"/> marks an anchor deduped with <c>-2</c>, <c>-3</c>.</summary>
public sealed record DocHeading(int Level, string Text, string Anchor, bool Suffixed, int Line, int EndLine);

/// <summary>
/// The heading anchors of a markdown body — the SPA's rule (<c>extractOutline</c> +
/// <c>HeadingSlugger</c> in <c>lib/outline.ts</c>): <see cref="Slug"/> of the raw
/// heading text, a doc-wide <c>-2</c> / <c>-3</c> suffix on a repeat, h2/h3 only,
/// fenced code and a leading frontmatter block skipped. Both sides are pinned by
/// the same test table.
/// </summary>
public static partial class DocSections
{
    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRe();

    [GeneratedRegex(@"^\s*(`{3,}|~{3,})")]
    private static partial Regex FenceRe();

    [GeneratedRegex(@"^mermaid-\d+$")]
    private static partial Regex PointerAnchorRe();

    /// <summary>True for an anchor that reads as a diagram pointer's <c>mermaid-&lt;n&gt;</c>.</summary>
    public static bool IsPointerAnchor(string anchor) => PointerAnchorRe().IsMatch(anchor);

    public static IReadOnlyList<DocHeading> Outline(string body)
    {
        var lines = Lines(body);
        var start = FrontmatterEnd(lines);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var all = new List<(int Level, string Text, int Line)>();
        char? fence = null;
        for (var i = start; i < lines.Length; i++)
        {
            var f = FenceRe().Match(lines[i]);
            if (f.Success)
            {
                var ch = f.Groups[1].Value[0];
                if (fence is null) fence = ch;
                else if (fence == ch) fence = null;
                continue;
            }
            if (fence is not null) continue;
            var m = HeadingRe().Match(lines[i]);
            if (!m.Success) continue;
            var text = m.Groups[2].Value.Trim();
            if (text.Length == 0) continue;
            all.Add((m.Groups[1].Length, text, i));
        }

        var result = new List<DocHeading>();
        for (var k = 0; k < all.Count; k++)
        {
            var (level, text, line) = all[k];
            if (level is not (2 or 3)) continue;
            var end = lines.Length - 1;
            for (var j = k + 1; j < all.Count; j++)
            {
                if (all[j].Level > level) continue;
                end = all[j].Line - 1;
                break;
            }
            while (end > line && lines[end].Trim().Length == 0) end--;
            var slug = Slug.From(text);
            var n = seen.GetValueOrDefault(slug);
            seen[slug] = n + 1;
            result.Add(new DocHeading(level, text, n == 0 ? slug : $"{slug}-{n + 1}", n > 0, line, end));
        }
        return result;
    }

    /// <summary>The section's markdown, its heading line included, or null when no heading has that anchor.</summary>
    public static string? Section(string body, string anchor)
    {
        var h = Outline(body).FirstOrDefault(x => x.Anchor == anchor);
        if (h is null) return null;
        var lines = Lines(body);
        return string.Join('\n', lines[h.Line..(h.EndLine + 1)]);
    }

    private static string[] Lines(string body) => (body ?? "").Replace("\r\n", "\n").Split('\n');

    private static int FrontmatterEnd(string[] lines)
    {
        if (lines.Length == 0 || lines[0] != "---") return 0;
        var close = Array.IndexOf(lines, "---", 1);
        return close > 0 ? close + 1 : 0;
    }
}
