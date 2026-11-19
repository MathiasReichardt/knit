namespace Knit.Core.Frontmatter;

/// <summary>
/// The single leading <c>#&#160;H1</c> of a markdown body — the line that
/// mirrors the frontmatter <c>title</c>. Every helper only ever
/// touches that one leading line; non-leading headings and prose are left
/// untouched, so no authored content is at risk.
/// </summary>
public static class LeadingH1
{
    /// <summary>
    /// Text of the leading <c>#&#160;</c> heading (after any blank lines), or
    /// <c>null</c> when the body's first non-blank line isn't a top-level
    /// heading.
    /// </summary>
    public static string? Extract(string body)
    {
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // A leading H1 must sit at column 0 — same convention as the
            // strip logic in BodySections / the SPA renderer.
            return raw.StartsWith("# ", StringComparison.Ordinal) ? raw[2..].Trim() : null;
        }
        return null;
    }

    /// <summary>
    /// Return <paramref name="body"/> with its leading H1 set to
    /// <paramref name="title"/> — replacing the existing leading H1 in place if
    /// present, otherwise inserting <c># title</c> as the first line followed by
    /// a blank line. Only the single leading H1 line is affected.
    /// </summary>
    public static string SetOrInsert(string body, string title)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n').ToList();
        var i = 0;
        while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;

        var newH1 = $"# {title}";
        if (i < lines.Count && lines[i].StartsWith("# ", StringComparison.Ordinal))
        {
            lines[i] = newH1;
            return string.Join('\n', lines);
        }

        // No leading H1: drop any leading blank lines, then prepend the H1
        // plus one blank line separating it from the body that follows.
        lines.RemoveRange(0, i);
        lines.Insert(0, "");
        lines.Insert(0, newH1);
        return string.Join('\n', lines);
    }
}
