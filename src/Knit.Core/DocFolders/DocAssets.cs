using System.Text.RegularExpressions;

namespace Knit.Core.DocFolders;

/// <summary>
/// The files a doc links next to itself (an embedded SVG, an image, a PDF).
/// They belong to the doc, so a move that changes its folder carries them.
/// </summary>
public static class DocAssets
{
    private static readonly Regex LinkRe = new(@"!?\[[^\]]*\]\(([^)\s]+)(?:\s+[^)]*)?\)", RegexOptions.Compiled);

    /// <summary>
    /// Call after the doc moved to <paramref name="newDoc"/>. Moves each sibling
    /// file the body links into the new folder, unless one of that name is
    /// already there. Returns the moves, for the prose rewriter: with them the
    /// doc's own links stay as written.
    /// </summary>
    public static IReadOnlyList<(string OldPath, string NewPath)> Carry(string oldDoc, string newDoc)
    {
        var oldDir = Path.GetDirectoryName(Path.GetFullPath(oldDoc))!;
        var newDir = Path.GetDirectoryName(Path.GetFullPath(newDoc))!;
        if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(newDoc)) return [];

        var moves = new List<(string, string)>();
        foreach (var name in SiblingTargets(File.ReadAllText(newDoc)))
        {
            var from = Path.Combine(oldDir, name);
            var to = Path.Combine(newDir, name);
            if (!File.Exists(from) || File.Exists(to)) continue;
            File.Move(from, to);
            moves.Add((from, to));
        }
        return moves;
    }

    /// <summary>The distinct non-markdown file names the body links in its own folder.</summary>
    public static IEnumerable<string> SiblingTargets(string body) =>
        LinkRe.Matches(body)
            .Select(m => Uri.UnescapeDataString(m.Groups[1].Value.Split('#')[0].Split('?')[0]))
            .Select(t => t.StartsWith("./", StringComparison.Ordinal) ? t[2..] : t)
            .Where(t => t.Length > 0 && !t.Contains('/') && !t.Contains('\\') && !t.Contains(':')
                        && !t.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
