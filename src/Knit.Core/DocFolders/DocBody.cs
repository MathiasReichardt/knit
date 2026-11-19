using Knit.Core.Frontmatter;
using Knit.Core.IO;

namespace Knit.Core.DocFolders;

/// <summary>
/// Frontmatter-preserving raw read/write for a doc-folder markdown file.
/// A project
/// readme / roadmap / free-form doc usually carries no frontmatter, but a body
/// edit must never clobber one that <em>is</em> present — so a read returns the
/// (frontmatter map, body) split and a write re-emits the frontmatter verbatim.
/// With an empty frontmatter map the file is written as plain markdown (no
/// leading <c>---</c> block) so a readme stays a readme.
/// </summary>
public static class DocBody
{
    /// <summary>(frontmatter block text, body) split of a file's content.
    /// Frontmatter is <c>""</c> when there is no terminated leading
    /// <c>---</c> block.</summary>
    public static (string Frontmatter, string Body) Split(string content)
    {
        var s = content.Replace("\r\n", "\n");
        if (!s.StartsWith("---", StringComparison.Ordinal)) return ("", s);
        var endIdx = s.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return ("", s);
        var fm = s.Substring(3, endIdx - 3).TrimStart('\n');
        var body = s[(endIdx + 4)..].TrimStart('\n');
        return (fm, body);
    }

    /// <summary>Raw (frontmatter map, body) for in-place edits — preserves
    /// unknown keys the typed views drop. Null when the file is missing.</summary>
    public static (Dictionary<string, object?> Frontmatter, string Body)? ReadRaw(
        string path, IFrontmatterSerializer yaml)
    {
        if (!File.Exists(path)) return null;
        var (fm, body) = Split(AtomicFile.ReadAllText(path));
        var fields = fm.Length == 0 ? new Dictionary<string, object?>(StringComparer.Ordinal) : yaml.Parse(fm);
        return (fields, body);
    }

    /// <summary>Write a doc from a frontmatter map + body. An empty map emits
    /// plain markdown; a non-empty map is serialized as a leading YAML block.
    /// The body is emitted verbatim after the closing <c>---</c>.</summary>
    public static void WriteRaw(
        string path, IReadOnlyDictionary<string, object?> frontmatter, string body, IFrontmatterSerializer yaml)
    {
        var b = body.Replace("\r\n", "\n").Trim('\n');
        string content;
        if (frontmatter.Count == 0)
        {
            content = b.Length == 0 ? "" : b + "\n";
        }
        else
        {
            var fm = yaml.Write(frontmatter).TrimEnd('\n');
            content = b.Length == 0 ? $"---\n{fm}\n---\n" : $"---\n{fm}\n---\n\n{b}\n";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, content);
    }
}
