using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Kb;

namespace Knit.Data;

/// <summary>
/// Walks every item and rewrites <c>KB ref</c> target paths in the body
/// <c>## Links</c> table when a row resolves to the old KB file. Each
/// item gets a freshly computed source-relative path to the new KB file.
/// </summary>
public sealed class KbRefRewriter : ItemRefRewriter, ItemRefRemover
{
    private readonly ItemStore _store;

    public KbRefRewriter(ItemStore store) => _store = store;

    public IReadOnlyList<string> RewriteKbRefs(string oldAbsolutePath, string newAbsolutePath, string? newTargetText = null)
    {
        var oldNorm = Path.GetFullPath(oldAbsolutePath).Replace('\\', '/');
        var changed = new List<string>();

        foreach (var sourceFile in _store.AllItemFiles())
        {
            var sourceDir = Path.GetDirectoryName(sourceFile)!;
            Item? item;
            try { item = new Knit.Core.Frontmatter.ItemFile(new SimpleFrontmatterSerializer()).Read(AtomicFile.ReadAllText(sourceFile)); }
            catch { continue; }
            if (item is null || item.LinkRows.Count == 0) continue;

            var didChange = false;
            var rewritten = new List<LinkRow>(item.LinkRows.Count);
            foreach (var row in item.LinkRows)
            {
                if (row.Rel == LinkRel.KbRef)
                {
                    var abs = Path.GetFullPath(Path.Combine(sourceDir, row.TargetPath)).Replace('\\', '/');
                    if (string.Equals(abs, oldNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        var newRel = LinkPath.Relative(sourceDir, newAbsolutePath);
                        rewritten.Add(row with
                        {
                            TargetPath = newRel,
                            TargetText = string.IsNullOrEmpty(newTargetText) ? row.TargetText : newTargetText,
                        });
                        didChange = true;
                        continue;
                    }
                }
                rewritten.Add(row);
            }
            if (!didChange) continue;
            _store.Write(item with { LinkRows = rewritten });
            changed.Add(item.Id);
        }

        return changed;
    }

    public IReadOnlyList<string> RemoveKbRefs(IReadOnlyCollection<string> oldTargetPaths)
        => MatchTargets(oldTargetPaths, write: true);

    public IReadOnlyList<string> FindItemsReferencing(IReadOnlyCollection<string> oldTargetPaths)
        => MatchTargets(oldTargetPaths, write: false);

    private IReadOnlyList<string> MatchTargets(IReadOnlyCollection<string> oldTargetPaths, bool write)
    {
        var targets = oldTargetPaths
            .Select(p => Path.GetFullPath(p).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0) return [];

        var changed = new List<string>();
        foreach (var sourceFile in _store.AllItemFiles())
        {
            var sourceDir = Path.GetDirectoryName(sourceFile)!;
            Item? item;
            try { item = new Knit.Core.Frontmatter.ItemFile(new SimpleFrontmatterSerializer()).Read(AtomicFile.ReadAllText(sourceFile)); }
            catch { continue; }
            if (item is null || item.LinkRows.Count == 0) continue;

            var didChange = false;
            var kept = new List<LinkRow>(item.LinkRows.Count);
            foreach (var row in item.LinkRows)
            {
                if (row.Rel == LinkRel.KbRef)
                {
                    var abs = Path.GetFullPath(Path.Combine(sourceDir, row.TargetPath)).Replace('\\', '/');
                    if (targets.Contains(abs)) { didChange = true; continue; }
                }
                kept.Add(row);
            }
            if (!didChange) continue;
            if (write) _store.Write(item with { LinkRows = kept });
            changed.Add(item.Id);
        }

        return changed;
    }
}
