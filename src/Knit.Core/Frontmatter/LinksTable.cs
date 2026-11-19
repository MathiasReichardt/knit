using System.Text;
using Knit.Core.Domain;

namespace Knit.Core.Frontmatter;

/// <summary>
/// Parses and renders the <c>## Links</c> body table that carries every
/// typed relation (item↔item, item↔KB, KB↔KB).
/// Replaces frontmatter <c>links:</c> / <c>kb_refs:</c> / <c>blockers:</c>
/// arrays. Lenient on parse, canonical on write.
/// </summary>
public static class LinksTable
{
    public const string Heading = "## Links";

    /// <summary>
    /// Extract every <c>## Links</c> row from a body. Missing section ⇒
    /// empty list. Malformed rows are skipped (caller may log via
    /// <paramref name="warnings"/>). The header / separator rows are
    /// identified by their first cell being <c>Rel</c> or matching the
    /// markdown separator pattern.
    /// </summary>
    public static IReadOnlyList<LinkRow> Parse(string body, IList<string>? warnings = null)
    {
        var rows = new List<LinkRow>();
        if (string.IsNullOrEmpty(body)) return rows;

        var lines = body.Replace("\r\n", "\n").Split('\n');
        var inSection = false;
        var sawHeaderRow = false;
        var sawParent = false;
        foreach (var raw in lines)
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                var name = raw[3..].Trim();
                if (inSection) break; // next section terminates
                inSection = name.Equals("Links", StringComparison.OrdinalIgnoreCase);
                sawHeaderRow = false;
                continue;
            }
            if (!inSection) continue;
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (!line.StartsWith('|')) continue;

            var cells = SplitRow(line);
            if (cells.Count < 2)
            {
                warnings?.Add($"## Links: malformed row: {line}");
                continue;
            }

            // Header / separator detection.
            if (!sawHeaderRow)
            {
                if (cells[0].Equals("Rel", StringComparison.OrdinalIgnoreCase))
                {
                    sawHeaderRow = true;
                    continue;
                }
                if (cells.All(c => c.Length == 0 || c.All(ch => ch == '-' || ch == ':')))
                {
                    // separator before header — tolerate but flip header flag
                    sawHeaderRow = true;
                    continue;
                }
            }
            else if (cells.All(c => c.Length == 0 || c.All(ch => ch == '-' || ch == ':')))
            {
                continue; // separator row
            }

            if (!LinkRels.TryParse(cells[0], out var rel))
            {
                warnings?.Add($"## Links: unknown rel '{cells[0]}' in row: {line}");
                continue;
            }
            var target = cells.Count > 1 ? cells[1] : string.Empty;
            var notes = cells.Count > 2 ? cells[2] : null;
            if (string.IsNullOrWhiteSpace(notes)) notes = null;

            if (!TryParseTargetCell(target, out var text, out var path))
            {
                warnings?.Add($"## Links: target not a markdown link: {target}");
                continue;
            }
            // An external rel's target is a branch / mount / URL, not a path in
            // this repo. The bare form is its canonical shape, so don't let the
            // lenient fallback copy it into TargetPath, where every path-based
            // scan would then read it as a missing file.
            if (ExternalRefs.IsExternal(rel) && !target.Trim().StartsWith('[')) path = string.Empty;
            if (rel == LinkRel.Parent)
            {
                if (sawParent)
                {
                    warnings?.Add("## Links: multiple Parent rows; first wins");
                    continue;
                }
                sawParent = true;
            }
            rows.Add(new LinkRow(rel, text, path, notes));
        }
        return rows;
    }

    /// <summary>
    /// Render a list of rows as a padded markdown table prefixed by the
    /// <c>## Links</c> heading. Returns the empty string when there are
    /// no rows (the "no section ⇒ no links" invariant). Multiple Parent
    /// rows are collapsed to the first.
    /// </summary>
    public static string Render(IReadOnlyList<LinkRow> rows)
    {
        if (rows is null || rows.Count == 0) return string.Empty;

        var seenParent = false;
        var keep = new List<LinkRow>(rows.Count);
        foreach (var r in rows)
        {
            if (r.Rel == LinkRel.Parent)
            {
                if (seenParent) continue;
                seenParent = true;
            }
            keep.Add(r);
        }
        if (keep.Count == 0) return string.Empty;

        const string h1 = "Rel";
        const string h2 = "Target";
        const string h3 = "Notes";

        var relStrs = keep.Select(r => LinkRels.ToWire(r.Rel)).ToArray();
        var tgtStrs = keep.Select(r => r.TargetPath.Length == 0
            ? EscapeMd(r.TargetText)
            : $"[{EscapeMd(r.TargetText)}]({r.TargetPath})").ToArray();
        // A note is free text (a block reason): keep it on one row and its pipes
        // out of the cell grid (GFM's `\|`).
        var noteStrs = keep.Select(r => (r.Notes ?? string.Empty).ReplaceLineEndings(" ").Replace("|", @"\|")).ToArray();

        var w1 = Math.Max(h1.Length, relStrs.Max(s => s.Length));
        var w2 = Math.Max(h2.Length, tgtStrs.Max(s => s.Length));
        var w3 = Math.Max(h3.Length, noteStrs.Max(s => s.Length));

        var sb = new StringBuilder();
        sb.Append("## Links\n\n");
        sb.Append('|').Append(' ').Append(h1.PadRight(w1)).Append(' ');
        sb.Append('|').Append(' ').Append(h2.PadRight(w2)).Append(' ');
        sb.Append('|').Append(' ').Append(h3.PadRight(w3)).Append(' ').Append("|\n");
        sb.Append('|').Append(new string('-', w1 + 2));
        sb.Append('|').Append(new string('-', w2 + 2));
        sb.Append('|').Append(new string('-', w3 + 2)).Append("|\n");
        for (var i = 0; i < keep.Count; i++)
        {
            sb.Append('|').Append(' ').Append(relStrs[i].PadRight(w1)).Append(' ');
            sb.Append('|').Append(' ').Append(tgtStrs[i].PadRight(w2)).Append(' ');
            sb.Append('|').Append(' ').Append(noteStrs[i].PadRight(w3)).Append(' ').Append("|\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The body with its <c>## Links</c> section removed — the heading plus the
    /// contiguous pipe-prefixed rows. Anything after that point is preserved.
    /// </summary>
    public static string Strip(string body)
    {
        var s = body.Replace("\r\n", "\n");
        var idx = FindHeading(s, "Links");
        if (idx < 0) return body;

        var afterHeading = s.IndexOf('\n', idx);
        if (afterHeading < 0) return string.Empty;
        var endOfSection = SectionEnd(s, afterHeading + 1);
        var before = s[..idx].TrimEnd('\n');
        var after = endOfSection >= s.Length ? string.Empty : s[endOfSection..].TrimStart('\n');
        var rebuilt = before;
        if (after.Length > 0)
        {
            if (rebuilt.Length > 0) rebuilt += "\n\n";
            rebuilt += after;
        }
        if (rebuilt.Length > 0 && !rebuilt.EndsWith('\n')) rebuilt += "\n";
        return rebuilt;
    }

    /// <summary>
    /// Write <paramref name="rows"/> into the body's <c>## Links</c> section,
    /// replacing any existing one and landing directly after a leading
    /// <c># h1</c>. For KB artifacts, whose body is stored whole — an item's
    /// table is rebuilt from <c>LinkRows</c> on every write instead.
    /// </summary>
    public static string Replace(string body, IReadOnlyList<LinkRow> rows)
    {
        var rest = Strip(body).Replace("\r\n", "\n").Trim('\n');
        var table = Render(rows);
        if (table.Length == 0) return rest.Length == 0 ? string.Empty : rest + "\n";

        var lines = rest.Split('\n');
        var h1 = lines.Length > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal) ? 1 : 0;
        var head = string.Join('\n', lines[..h1]);
        var tail = string.Join('\n', lines[h1..]).Trim('\n');

        var sb = new StringBuilder();
        if (head.Length > 0) sb.Append(head).Append("\n\n");
        sb.Append(table);
        if (tail.Length > 0) sb.Append('\n').Append(tail).Append('\n');
        return sb.ToString();
    }

    private static int SectionEnd(string text, int from)
    {
        var idx = from;
        while (idx < text.Length)
        {
            var nl = text.IndexOf('\n', idx);
            var end = nl < 0 ? text.Length : nl;
            var line = text[idx..end];
            if (line.StartsWith('|') || string.IsNullOrWhiteSpace(line))
            {
                if (nl < 0) return text.Length;
                idx = nl + 1;
                continue;
            }
            return idx;
        }
        return text.Length;
    }

    private static int FindHeading(string text, string name)
    {
        var idx = 0;
        while (idx < text.Length)
        {
            var nl = text.IndexOf('\n', idx);
            var end = nl < 0 ? text.Length : nl;
            var line = text[idx..end];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var h = line[3..].Trim();
                if (h.Equals(name, StringComparison.OrdinalIgnoreCase)) return idx;
            }
            if (nl < 0) break;
            idx = nl + 1;
        }
        return -1;
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inBracket = false;
        foreach (var c in trimmed)
        {
            if (c == '[') inBracket = true;
            else if (c == ']') inBracket = false;
            if (c == '|' && sb.Length > 0 && sb[^1] == '\\') sb[^1] = '|';
            else if (c == '|' && !inBracket)
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    /// <summary>
    /// Parse a target cell. Accepts <c>[text](path)</c> or, leniently, a
    /// bare path/text. Returns false if both pieces are empty.
    /// </summary>
    public static bool TryParseTargetCell(string cell, out string text, out string path)
    {
        text = string.Empty;
        path = string.Empty;
        var s = cell.Trim();
        if (s.Length == 0) return false;
        if (s.StartsWith('[') && s.Contains("](", StringComparison.Ordinal) && s.EndsWith(')'))
        {
            var closeBracket = s.IndexOf("](", StringComparison.Ordinal);
            text = s.Substring(1, closeBracket - 1);
            path = s.Substring(closeBracket + 2, s.Length - closeBracket - 3);
            return path.Length > 0 || text.Length > 0;
        }
        // Bare value — treat the whole thing as both text and path so callers
        // get something usable. Knit's writer always emits canonical form.
        text = s;
        path = s;
        return true;
    }

    private static string EscapeMd(string s)
    {
        // Soft escape — we only worry about square brackets that would
        // confuse the link parser.
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("]", @"\]");
    }
}

public sealed record LinkRow(LinkRel Rel, string TargetText, string TargetPath, string? Notes);

public enum LinkRel
{
    Parent,
    Blocks,
    BlockedBy,
    Related,
    Subtasks,
    KbRef,
    // Dated cut across the tree — targets a milestone sub-doc under
    // .knit/projects/<project>/milestones/. Non-structural; any item type
    // may carry one (one-per-item is a convention verify warns on).
    Milestone,
    // The external rels. Their target is not a file in this repo, so
    // `TargetPath` carries a clickable URL or nothing at all — see `ExternalRefs`.
    Branch,
    Repo,
    Pr,
    Commit,
}

public static class LinkRels
{
    public static string ToWire(LinkRel r) => r switch
    {
        LinkRel.Parent => "Parent",
        LinkRel.Blocks => "Blocks",
        LinkRel.BlockedBy => "Blocked by",
        LinkRel.Related => "Related",
        LinkRel.Subtasks => "Subtasks",
        LinkRel.KbRef => "KB ref",
        LinkRel.Milestone => "Milestone",
        LinkRel.Branch => "Branch",
        LinkRel.Repo => "Repo",
        LinkRel.Pr => "PR",
        LinkRel.Commit => "Commit",
        _ => "Related",
    };

    public static bool TryParse(string? s, out LinkRel rel)
    {
        var v = (s ?? string.Empty).Trim().ToLowerInvariant();
        switch (v)
        {
            case "parent": rel = LinkRel.Parent; return true;
            case "blocks": rel = LinkRel.Blocks; return true;
            case "blocked by":
            case "blockedby":
            case "blocked-by": rel = LinkRel.BlockedBy; return true;
            case "related": rel = LinkRel.Related; return true;
            case "subtasks":
            case "subtask": rel = LinkRel.Subtasks; return true;
            case "kb ref":
            case "kbref":
            case "kb-ref":
            case "kb_ref": rel = LinkRel.KbRef; return true;
            case "milestone": rel = LinkRel.Milestone; return true;
            case "branch": rel = LinkRel.Branch; return true;
            case "repo":
            case "repository": rel = LinkRel.Repo; return true;
            case "pr":
            case "pull request": rel = LinkRel.Pr; return true;
            case "commit": rel = LinkRel.Commit; return true;
            default: rel = LinkRel.Related; return false;
        }
    }
}
