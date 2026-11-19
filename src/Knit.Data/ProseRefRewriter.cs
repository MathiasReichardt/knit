using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Kb;
using Knit.Core.Refs;

namespace Knit.Data;

/// <summary>
/// Repairs body prose after a file moved or an entry was renamed, reading
/// exactly the lines <see cref="RepoVerifier.ScanProse"/> checks: markdown
/// links resolving into a moved file or folder get a freshly computed
/// source-relative path, and a <c>`bucket:slug`</c> code-span or mermaid link
/// naming a renamed ref (or one of its <c>/sub</c> docs) or a moved <c>.knit/…</c>
/// path gets the new one, its <c>#fragment</c> kept. The <c>## Links</c>
/// table is left to <see cref="KbRefRewriter"/>.
/// </summary>
public sealed class ProseRefRewriter : ProseRewriter
{
    private readonly ItemStore _store;
    private readonly string _root;

    public ProseRefRewriter(ItemStore store, string root)
    {
        _store = store;
        _root = root;
    }

    public IReadOnlyList<string> RewriteProse(
        IReadOnlyList<(string OldPath, string NewPath)> moves,
        IReadOnlyList<(Ref Old, Ref New)> renames)
    {
        var normMoves = moves
            .Select(m => (Old: Norm(m.OldPath), New: Path.GetFullPath(m.NewPath)))
            .ToList();
        var changed = new List<string>();

        foreach (var file in RepoVerifier.ProseFiles(_store, _root))
        {
            string content;
            try { content = AtomicFile.ReadAllText(file); } catch { continue; }
            var sourceDir = Path.GetDirectoryName(file)!;
            // A moved file's own links were written relative to where it was.
            var authoredDir = Path.GetDirectoryName(MovedFrom(Norm(file), normMoves) ?? file)!;
            var lines = content.Split('\n');
            var didChange = (renames.Count > 0 || normMoves.Count > 0)
                && RepoVerifier.RewriteRefs(lines, d => Repoint(d, renames, normMoves));
            if (normMoves.Count > 0)
            {
                foreach (var i in RepoVerifier.ProseLineIndexes(lines))
                {
                    var updated = RepointLinks(lines[i], sourceDir, authoredDir, normMoves);
                    if (updated == lines[i]) continue;
                    lines[i] = updated;
                    didChange = true;
                }
            }
            if (!didChange) continue;
            AtomicFile.WriteAllText(file, string.Join('\n', lines));
            changed.Add(file);
        }

        if (changed.Count > 0) _store.Forget();
        return changed;
    }

    // The fragment rides along: a section or a pointer names a place in the
    // doc, which a rename or a move doesn't change.
    private DocRef? Repoint(
        DocRef d, IReadOnlyList<(Ref Old, Ref New)> renames, IReadOnlyList<(string Old, string New)> moves)
    {
        if (d.Ref is { } r)
        {
            foreach (var (oldRef, newRef) in renames)
            {
                if (r.Bucket != oldRef.Bucket) continue;
                if (string.Equals(r.Slug, oldRef.Slug, StringComparison.Ordinal))
                    return d.WithRef(newRef);
                if (r.Slug.StartsWith(oldRef.Slug + "/", StringComparison.Ordinal))
                    return d.WithRef(new Ref(newRef.Bucket, newRef.Slug + r.Slug[oldRef.Slug.Length..]));
            }
            return null;
        }
        var abs = Norm(Path.Combine(_root, d.KnitPath!));
        foreach (var (oldPath, newPath) in moves)
        {
            string? moved = null;
            if (string.Equals(abs, oldPath, StringComparison.OrdinalIgnoreCase)) moved = newPath;
            else if (abs.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                moved = Path.Combine(newPath, abs[(oldPath.Length + 1)..]);
            if (moved is not null)
                return d with { KnitPath = Path.GetRelativePath(_root, moved).Replace('\\', '/') };
        }
        return null;
    }

    private static string? MovedFrom(string file, IReadOnlyList<(string Old, string New)> moves)
    {
        foreach (var (oldPath, newPath) in moves)
        {
            var newNorm = newPath.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(file, newNorm, StringComparison.OrdinalIgnoreCase)) return oldPath;
            if (file.StartsWith(newNorm + "/", StringComparison.OrdinalIgnoreCase))
                return oldPath + file[newNorm.Length..];
        }
        return null;
    }

    private static string RepointLinks(
        string line, string sourceDir, string authoredDir, IReadOnlyList<(string Old, string New)> moves) =>
        RepoVerifier.LinkRe.Replace(line, m =>
        {
            var g = m.Groups[2];
            var target = g.Value;
            if (target.Length == 0 || target.StartsWith('#') || target.StartsWith('/')) return m.Value;
            if (RepoVerifier.ProtocolRe.IsMatch(target)) return m.Value;
            // A fragment / query survives the move — only the path half moves.
            var cut = target.IndexOfAny(['#', '?']);
            var clean = cut < 0 ? target : target[..cut];
            var suffix = cut < 0 ? "" : target[cut..];
            if (clean.Length == 0) return m.Value;

            string abs;
            try { abs = Norm(Path.Combine(authoredDir, clean)); }
            catch { return m.Value; }

            string? moved = null;
            foreach (var (oldPath, newPath) in moves)
            {
                if (string.Equals(abs, oldPath, StringComparison.OrdinalIgnoreCase)) moved = newPath;
                else if (abs.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                    moved = Path.Combine(newPath, abs[(oldPath.Length + 1)..]);
                if (moved is not null) break;
            }
            if (moved is null && authoredDir == sourceDir) return m.Value;
            var start = g.Index - m.Index;
            return m.Value[..start] + LinkPath.Relative(sourceDir, moved ?? abs) + suffix + m.Value[(start + g.Length)..];
        });

    private static string Norm(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
}
