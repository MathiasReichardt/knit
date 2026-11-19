using System.Globalization;
using System.Text;

namespace Knit.Data.StatusPage;

/// <summary>
/// GFM helpers for the status page: escaping user text and computing heading
/// anchors the way the forges do.
/// </summary>
public static class GfmText
{
    /// <summary>
    /// User text as plain inline text. <c>|</c> is escaped everywhere because
    /// GFM splits table cells before it parses inlines; the rest keeps a title
    /// from turning into markup. Line breaks fold to spaces: every use is one
    /// line, and a break inside a table cell ends the row.
    /// </summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 8);
        var first = true;
        foreach (var raw in text)
        {
            var c = raw is '\r' or '\n' ? ' ' : raw;
            if (c is '\\' or '|' or '[' or ']' or '*' or '_' or '`' or '<' || (first && c == '#'))
                sb.Append('\\');
            sb.Append(c);
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>A relative link destination: forward slashes, and the characters that end or break one encoded.</summary>
    public static string Url(string relativePath)
    {
        var sb = new StringBuilder(relativePath.Length);
        foreach (var c in relativePath.Replace('\\', '/'))
            sb.Append(c switch
            {
                ' ' => "%20",
                '(' => "%28",
                ')' => "%29",
                '<' => "%3C",
                '>' => "%3E",
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    /// <summary>The anchor base for a heading's text, before duplicate suffixes.</summary>
    public static string AnchorBase(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        foreach (var rune in heading.ToLowerInvariant().EnumerateRunes())
        {
            if (rune.Value == ' ') { sb.Append('-'); continue; }
            if (rune.Value == '-') { sb.Append('-'); continue; }
            var keep = Rune.GetUnicodeCategory(rune) switch
            {
                UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                    or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                    or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                    or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber
                    or UnicodeCategory.ConnectorPunctuation => true,
                _ => false,
            };
            if (keep) sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Hands out anchors in document order. A repeat gets <c>-1</c>, <c>-2</c>,
    /// counted over every heading in the file — github-slugger's rule, which
    /// also skips a suffixed form an earlier heading already took.
    /// </summary>
    public sealed class Slugger
    {
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

        public string Next(string heading)
        {
            var original = AnchorBase(heading);
            var slug = original;
            while (_seen.ContainsKey(slug))
            {
                _seen[original] = _seen.GetValueOrDefault(original) + 1;
                slug = $"{original}-{_seen[original]}";
            }
            _seen[slug] = 0;
            return slug;
        }
    }
}
