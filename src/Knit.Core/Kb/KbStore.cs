using System.Text;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Read / write / list KB artifacts under <c>.knit/kb/</c>. Analogous to
/// <c>ItemStore</c>; no JSONL cache (KB is low-volume, no FTS yet).
/// </summary>
public sealed class KbStore
{
    private readonly string _repoRoot;
    private readonly IFrontmatterSerializer _yaml;

    public KbStore(string repoRoot, IFrontmatterSerializer? yaml = null)
    {
        _repoRoot = repoRoot;
        _yaml = yaml ?? new SimpleFrontmatterSerializer();
    }

    public string RepoRoot => _repoRoot;

    public string Write(Ref r, IReadOnlyDictionary<string, object?> frontmatter, string body)
    {
        var path = ResolveWritePath(r);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fm = _yaml.Write(frontmatter).TrimEnd();
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append(fm);
        sb.Append("\n---\n\n");
        sb.Append(body.TrimEnd());
        sb.Append("\n");
        AtomicFile.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>
    /// Returns (frontmatter, body) for a ref, or null if missing.
    /// </summary>
    public (IReadOnlyDictionary<string, object?> Frontmatter, string Body)? Read(Ref r)
    {
        var resolved = RefResolver.Resolve(r, _repoRoot).GetValueOrThrow();
        if (!resolved.Exists) return null;
        return ReadPath(resolved.Path);
    }

    public (IReadOnlyDictionary<string, object?> Frontmatter, string Body)? ReadPath(string path)
    {
        if (!File.Exists(path)) return null;
        var content = AtomicFile.ReadAllText(path).Replace("\r\n", "\n");
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return (new Dictionary<string, object?>(), content);
        var endIdx = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return (new Dictionary<string, object?>(), content);
        var fmText = content[3..endIdx].TrimStart('\n');
        var body = content[(endIdx + 4)..].TrimStart('\n');
        var fm = _yaml.Parse(fmText);
        return (fm, body);
    }

    /// <summary>
    /// Enumerate all artifacts in a bucket. Returns (ref, path) tuples for
    /// each file. Skips the bucket's <c>index.md</c>.
    /// </summary>
    public IEnumerable<(Ref Ref, string Path)> ListBucket(Bucket bucket)
    {
        var dir = KbLayout.BucketDir(_repoRoot, bucket);
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, KbLayout.PerFolderIndexFile, StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            string slug;
            if (rel.EndsWith("/readme.md", StringComparison.OrdinalIgnoreCase))
                slug = rel[..^"/readme.md".Length];
            else if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                slug = rel[..^3];
            else continue;
            yield return (new Ref(bucket, slug), file);
        }
    }

    /// <summary>
    /// Compute the writable path for a ref. Consults <see cref="RefResolver"/>
    /// so a mutation targets the real on-disk file — notably an ADR addressed
    /// by its bare <c>NNNN</c> prefix resolves to the existing
    /// <c>NNNN-slug.md</c> instead of forking a duplicate <c>NNNN.md</c> beside
    /// it. When
    /// no file exists yet the ref is a fresh artifact, so the canonical
    /// single-file path is used (grow-to-folder buckets start flat).
    /// </summary>
    public string ResolveWritePath(Ref r)
    {
        var resolved = RefResolver.Resolve(r, _repoRoot).GetValueOrThrow();
        if (resolved.Exists) return resolved.Path;
        return KbLayout.FilePath(_repoRoot, r);
    }
}
