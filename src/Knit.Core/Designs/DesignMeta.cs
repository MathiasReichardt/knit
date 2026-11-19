namespace Knit.Core.Designs;

/// <summary>
/// Summary fields read out of a design doc without loading it into an
/// editor: what it is called and where it stands. Designs have no index
/// and no schema beyond the template, so both are derived from the file.
/// </summary>
public static class DesignMeta
{
    /// <summary>
    /// The heading the design template seeds and <see cref="StatusOf"/>
    /// reads. One spelling in one place — same reason as
    /// <c>BodySections.AcceptanceHeading</c>.
    /// </summary>
    public const string StatusHeading = "Status";

    /// <summary>Frontmatter <c>title</c>, else the first <c>#</c> heading, else the slug.</summary>
    public static string TitleOf(string slug, IReadOnlyDictionary<string, object?> fm, string body)
    {
        if (fm.TryGetValue("title", out var v) && v is not null)
        {
            var s = v.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s!;
        }
        return FirstHeading(body) ?? slug;
    }

    /// <summary>First <c>#&#160;heading</c> line in the body, if any.</summary>
    public static string? FirstHeading(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.StartsWith("# ", StringComparison.Ordinal))
                return t[2..].Trim();
        }
        return null;
    }

    /// <summary>
    /// First line of the <c>## Status</c> section — the template's
    /// draft / agreed / superseded slot. There is no <c>status:</c>
    /// frontmatter field on purpose: the section is where the skill tells
    /// authors to write it, and a second home would drift. Returns null
    /// when the section is missing or empty.
    /// </summary>
    public static string? StatusOf(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var inSection = false;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection) return null;
                inSection = raw[3..].Trim().Equals(StatusHeading, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var text = raw.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("- ", StringComparison.Ordinal)) text = text[2..].Trim();
            return text.Length == 0 ? null : text;
        }
        return null;
    }
}
