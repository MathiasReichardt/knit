using System.Text;

namespace Knit.Core.Frontmatter;

/// <summary>
/// Splits an item body (frontmatter + comments already stripped) into
/// h2-keyed sections. Used by <c>knit show --json</c> so agents can
/// access "Description", "Acceptance Criteria", etc. without re-parsing
/// markdown. h3 and below stay inline as raw markdown — the schema is
/// flat and stable.
/// </summary>
public static class BodySections
{
    /// <summary>
    /// The heading the default templates seed and every extractor reads.
    /// One spelling in one place — the templates and the code that parses
    /// them drifted (epic seeded "Success Criteria") because the string
    /// lived independently on both sides.
    /// </summary>
    public const string AcceptanceHeading = "Acceptance Criteria";

    /// <summary>
    /// Not seeded by any template — added on demand once there is code to
    /// point at. Read by <c>knit context</c> and the item API.
    /// </summary>
    public const string PointersHeading = "Code pointers";

    /// <summary>
    /// Returns ordered (key, value) pairs.
    /// - Each <c>## Heading</c> becomes a section; the key is the
    ///   heading text snake_cased, the value is the raw markdown
    ///   between this heading and the next, trimmed.
    /// - Prose preceding the first <c>##</c> heading (if any) is
    ///   returned under the key <c>"body"</c>.
    /// - The <c>## Comments</c> section is excluded — comments are
    ///   surfaced separately as structured data.
    /// </summary>
    public static List<KeyValuePair<string, string>> Split(string body)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(body)) return result;

        var lines = StripLeadingH1(body.Replace("\r\n", "\n").Split('\n'));
        string? currentKey = null;
        var currentBuf = new StringBuilder();
        var prelude = new StringBuilder();
        var sawHeading = false;

        void Flush()
        {
            if (currentKey is null) return;
            var text = currentBuf.ToString().Trim('\n').TrimEnd();
            result.Add(new KeyValuePair<string, string>(currentKey, text));
            currentKey = null;
            currentBuf.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                if (heading.Equals("Comments", StringComparison.OrdinalIgnoreCase))
                {
                    // Comments are handled structurally elsewhere.
                    Flush();
                    currentKey = null;
                    break;
                }
                Flush();
                if (!sawHeading)
                {
                    var pre = prelude.ToString().Trim('\n').TrimEnd();
                    if (!string.IsNullOrEmpty(pre))
                        result.Add(new KeyValuePair<string, string>("body", pre));
                    sawHeading = true;
                }
                currentKey = ToSnakeCase(heading);
                continue;
            }

            if (currentKey is null && !sawHeading)
                prelude.Append(line).Append('\n');
            else if (currentKey is not null)
                currentBuf.Append(line).Append('\n');
        }
        Flush();

        if (!sawHeading)
        {
            var pre = prelude.ToString().Trim('\n').TrimEnd();
            if (!string.IsNullOrEmpty(pre))
                result.Add(new KeyValuePair<string, string>("body", pre));
        }
        return result;
    }

    /// <summary>
    /// Drop a single leading <c>#</c> heading (and any blank lines around
    /// it). Templates start with <c># {{title}}</c> by convention; that
    /// h1 duplicates the frontmatter <c>title</c>, so it shouldn't show
    /// up as content.
    /// </summary>
    /// <summary>One bullet of an extracted list. <see cref="Checked"/> is null
    /// for a plain bullet, true/false for a <c>[x]</c> / <c>[ ]</c> task box.</summary>
    public sealed record ListEntry(string Text, bool? Checked);

    /// <summary>
    /// Strict-heading extraction: returns the bullet-list items under an
    /// <c>## &lt;heading&gt;</c> section (case-insensitive, exact-words match).
    /// Used by <c>knit context</c> for "Acceptance criteria" and
    /// "Code pointers". Prose that isn't part of a <c>- </c> bullet is
    /// ignored. Returns an empty list when the heading isn't present.
    /// <para>A bullet's continuation lines (the lines that follow it, or
    /// indented ones after a blank line) are joined into its text with single
    /// spaces.</para>
    /// <para>A leading <c>[ ]</c> / <c>[x]</c> task box becomes
    /// <see cref="ListEntry.Checked"/>; a bullet left empty by the strip is
    /// dropped, so the templates' seeded <c>- [ ]</c> doesn't come back as an
    /// empty criterion.</para>
    /// </summary>
    public static IReadOnlyList<ListEntry> ExtractList(string body, string heading)
    {
        var result = new List<ListEntry>();
        if (string.IsNullOrEmpty(body)) return result;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var inSection = false;
        StringBuilder? text = null;
        bool? isChecked = null;
        var sawBlank = false;

        void Close()
        {
            if (text is not null && text.Length > 0) result.Add(new ListEntry(text.ToString(), isChecked));
            text = null;
        }

        foreach (var raw in lines)
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                var name = raw[3..].Trim();
                inSection = name.Equals(heading, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) { sawBlank = true; continue; }

            if (trimmed == "-" || trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Close();
                var rest = trimmed[1..].Trim();
                isChecked = null;
                if (rest.Length >= 3 && rest[0] == '[' && rest[2] == ']' && (rest[1] is ' ' or 'x' or 'X')
                    &&(rest.Length == 3 || rest[3] == ' '))
                {
                    isChecked = rest[1] != ' ';
                    rest = rest[3..].Trim();
                }
                text = new StringBuilder(rest);
            }
            else if (text is not null && (!sawBlank || char.IsWhiteSpace(raw[0])))
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(trimmed);
            }
            else
            {
                Close();
            }
            sawBlank = false;
        }
        Close();
        return result;
    }

    /// <summary>
    /// Replace the content of <c>## &lt;heading&gt;</c> with <paramref name="content"/>,
    /// or append a new section if the heading is absent. Heading match is
    /// case-insensitive (exact words). Refuses <c>Links</c> and
    /// <c>Comments</c> — those are owned by structural parsers.
    /// When appending, inserts before <c>## Comments</c> if present so
    /// the comments tail stays last.
    /// </summary>
    public static string SetSection(string body, string heading, string content)
    {
        if (string.IsNullOrWhiteSpace(heading)) throw new ArgumentException("heading required", nameof(heading));
        var h = heading.Trim();
        if (h.Equals("Links", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Comments", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"section '{h}' is structural and cannot be set via this method", nameof(heading));

        var normalizedContent = (content ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');
        var lines = (body ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();

        var headingIdx = -1;
        var nextHeadingIdx = lines.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) continue;
            var name = lines[i][3..].Trim();
            if (headingIdx < 0 && name.Equals(h, StringComparison.OrdinalIgnoreCase))
            {
                headingIdx = i;
                continue;
            }
            if (headingIdx >= 0) { nextHeadingIdx = i; break; }
        }

        var sectionLines = new List<string> { $"## {h}", "" };
        if (normalizedContent.Length > 0)
        {
            sectionLines.AddRange(normalizedContent.Split('\n'));
            sectionLines.Add("");
        }

        if (headingIdx >= 0)
        {
            // The old section's trailing blanks go with it — sectionLines brings its own separator.
            lines.RemoveRange(headingIdx, nextHeadingIdx - headingIdx);
            lines.InsertRange(headingIdx, sectionLines);
        }
        else
        {
            // Append. If `## Comments` exists, insert before it; else at end.
            var commentsIdx = lines.FindIndex(l => l.StartsWith("## Comments", StringComparison.Ordinal));
            var insertAt = commentsIdx >= 0 ? commentsIdx : lines.Count;
            // Ensure a blank line separator before the inserted section.
            if (insertAt > 0 && !string.IsNullOrWhiteSpace(lines[insertAt - 1]))
                sectionLines.Insert(0, "");
            lines.InsertRange(insertAt, sectionLines);
        }

        return string.Join('\n', lines).TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// The <c>## </c> headings present in <paramref name="body"/>, in order and
    /// in their authored spelling (unlike <see cref="Split"/>, which snake_cases
    /// the key). Used to name the real sections when a caller asks for one that
    /// isn't there.
    /// </summary>
    public static IReadOnlyList<string> Headings(string body)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(body)) return result;
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
                result.Add(line[3..].Trim());
        }
        return result;
    }

    /// <summary>
    /// Remove <c>## &lt;heading&gt;</c> and everything under it, up to the next
    /// h2. The counterpart to <see cref="SetSection"/> with an empty value,
    /// which keeps the heading — a template section that doesn't apply should
    /// cease to exist, not stand empty. Refuses <c>Links</c> and
    /// <c>Comments</c> like <see cref="SetSection"/>. Returns false, leaving
    /// <paramref name="result"/> at the original body, when the heading is
    /// absent — the caller reports that rather than succeeding on a typo.
    /// </summary>
    public static bool TryDeleteSection(string body, string heading, out string result)
    {
        if (string.IsNullOrWhiteSpace(heading)) throw new ArgumentException("heading required", nameof(heading));
        var h = heading.Trim();
        if (h.Equals("Links", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Comments", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"section '{h}' is structural and cannot be deleted via this method", nameof(heading));

        result = body ?? string.Empty;
        var lines = result.Replace("\r\n", "\n").Split('\n').ToList();

        var headingIdx = -1;
        var nextHeadingIdx = lines.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) continue;
            var name = lines[i][3..].Trim();
            if (headingIdx < 0 && name.Equals(h, StringComparison.OrdinalIgnoreCase))
            {
                headingIdx = i;
                continue;
            }
            if (headingIdx >= 0) { nextHeadingIdx = i; break; }
        }
        if (headingIdx < 0) return false;

        lines.RemoveRange(headingIdx, nextHeadingIdx - headingIdx);
        // Drop the blank line the removed section left behind, so repeated
        // deletes don't stack empty lines between the survivors.
        while (headingIdx > 0 && headingIdx <= lines.Count
               && string.IsNullOrWhiteSpace(lines[headingIdx - 1])
               && (headingIdx == lines.Count || string.IsNullOrWhiteSpace(lines[headingIdx])))
        {
            lines.RemoveAt(headingIdx - 1);
            headingIdx--;
        }

        var joined = string.Join('\n', lines).TrimEnd('\n');
        result = joined.Length == 0 ? "" : joined + "\n";
        return true;
    }

    private static string[] StripLeadingH1(string[] lines)
    {
        var i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length) return lines;
        if (!lines[i].StartsWith("# ", StringComparison.Ordinal)) return lines;
        // Skip the h1 line plus any blank lines after it.
        i++;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        return lines[i..];
    }

    private static string ToSnakeCase(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        var lastWasSep = true;
        foreach (var raw in heading)
        {
            var c = raw;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSep = false;
            }
            else if (!lastWasSep && sb.Length > 0)
            {
                sb.Append('_');
                lastWasSep = true;
            }
        }
        // Trim trailing underscore from a final separator run.
        while (sb.Length > 0 && sb[^1] == '_') sb.Length--;
        return sb.ToString();
    }
}
