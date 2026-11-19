using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Walks every KB artifact and rewrites the <b>frontmatter</b> ref fields
/// when a KB entry is renamed or deleted — the KB-side counterpart to
/// <c>KbRefRewriter</c> (which maintains the <c>KB ref</c> rows in items'
/// <c>## Links</c> tables). KB entries reference each other only through
/// frontmatter and inline body prose; the structured frontmatter fields
/// are the part that can be rewritten mechanically:
/// <list type="bullet">
///   <item><c>supersedes</c> (list) / <c>superseded_by</c> (scalar) hold
///   canonical <c>bucket:slug</c> refs — matched on the full ref string.</item>
///   <item><c>feature</c> (scalar or list, depending on bucket) holds a
///   <b>bare feature slug</b> (e.g. <c>assembly-line</c>), not a
///   <c>feature:slug</c> ref — matched on the slug, and only relevant when
///   the renamed/deleted ref is in the <c>feature</c> bucket.</item>
/// </list>
/// Inline <c>bucket:slug</c> code-spans and markdown links in bodies are
/// deliberately out of scope (render-resolve / verify-catch handle those).
/// </summary>
public sealed class KbFrontmatterRefRewriter
{
    private readonly KbStore _store;

    public KbFrontmatterRefRewriter(KbStore store) => _store = store;

    /// <summary>
    /// Rewrite every frontmatter ref equal to an old ref to its new ref.
    /// The list carries the parent rename plus, for a folder-form entry,
    /// each sub-doc's old→new pair. Returns the refs of KB entries changed.
    /// </summary>
    public IReadOnlyList<string> RewriteFrontmatterRefs(IReadOnlyList<(Ref Old, Ref New)> renames)
    {
        if (renames.Count == 0) return [];
        return Apply(fm =>
        {
            var changed = false;
            foreach (var (old, @new) in renames)
            {
                changed |= RewriteListRef(fm, "supersedes", old.ToString(), @new.ToString());
                changed |= RewriteScalarRef(fm, "superseded_by", old.ToString(), @new.ToString());
                if (old.Bucket == Bucket.Feature)
                    changed |= RewriteFeatureSlug(fm, old.Slug, @new.Slug);
            }
            return changed;
        });
    }

    /// <summary>
    /// Strip every frontmatter ref pointing at a deleted entry (and, for a
    /// folder-form entry, its sub-docs). Drops list elements, clears the
    /// scalar. Keeps <c>supersedes</c> ⇄ <c>superseded_by</c> consistent —
    /// neither can keep pointing at a gone entry. Returns refs changed.
    /// </summary>
    public IReadOnlyList<string> RemoveFrontmatterRefs(IReadOnlyCollection<Ref> removed)
    {
        if (removed.Count == 0) return [];
        var refStrings = removed.Select(r => r.ToString()).ToHashSet(StringComparer.Ordinal);
        var featureSlugs = removed.Where(r => r.Bucket == Bucket.Feature)
            .Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
        return Apply(fm =>
        {
            var changed = false;
            changed |= RemoveListRefs(fm, "supersedes", refStrings);
            changed |= ClearScalarRef(fm, "superseded_by", refStrings);
            if (featureSlugs.Count > 0)
                changed |= RemoveFeatureSlugs(fm, featureSlugs);
            return changed;
        });
    }

    /// <summary>Read-only: refs of KB entries whose frontmatter references
    /// any of the targets. Drives the delete preview's KB-entry count.</summary>
    public IReadOnlyList<string> FindEntriesReferencing(IReadOnlyCollection<Ref> targets)
    {
        if (targets.Count == 0) return [];
        var refStrings = targets.Select(r => r.ToString()).ToHashSet(StringComparer.Ordinal);
        var featureSlugs = targets.Where(r => r.Bucket == Bucket.Feature)
            .Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
        var hits = new List<string>();
        foreach (var (r, path) in AllEntries())
        {
            var read = _store.ReadPath(path);
            if (read is null) continue;
            if (References(read.Value.Frontmatter, refStrings, featureSlugs))
                hits.Add(r.ToString());
        }
        return hits;
    }

    // --- shared walk ----------------------------------------------------

    /// <summary>Snapshot every entry, apply <paramref name="mutate"/> to a
    /// frontmatter copy, and write back only the ones that changed.</summary>
    private IReadOnlyList<string> Apply(Func<Dictionary<string, object?>, bool> mutate)
    {
        // Materialise before writing: ListBucket enumerates the directory
        // lazily, and we write back into files we've already yielded.
        var entries = AllEntries().ToList();
        var changed = new List<string>();
        foreach (var (r, path) in entries)
        {
            var read = _store.ReadPath(path);
            if (read is null) continue;
            var fm = new Dictionary<string, object?>(read.Value.Frontmatter, StringComparer.Ordinal);
            if (!mutate(fm)) continue;
            _store.Write(r, fm, read.Value.Body);
            changed.Add(r.ToString());
        }
        return changed;
    }

    private IEnumerable<(Ref Ref, string Path)> AllEntries()
    {
        foreach (var b in BucketStrings.All)
        {
            if (b == Bucket.Item) continue;
            foreach (var entry in _store.ListBucket(b))
                yield return entry;
        }
    }

    private static bool References(
        IReadOnlyDictionary<string, object?> fm,
        IReadOnlySet<string> refStrings,
        IReadOnlySet<string> featureSlugs)
    {
        if (fm.TryGetValue("supersedes", out var sup) && sup is List<object?> l
            && l.Any(x => refStrings.Contains(x?.ToString() ?? ""))) return true;
        if (fm.TryGetValue("superseded_by", out var sb) && refStrings.Contains(sb?.ToString() ?? "")) return true;
        if (featureSlugs.Count > 0 && fm.TryGetValue("feature", out var feat))
        {
            if (feat is List<object?> fl && fl.Any(x => featureSlugs.Contains(x?.ToString() ?? ""))) return true;
            if (feat is string fs && featureSlugs.Contains(fs)) return true;
        }
        return false;
    }

    // --- field ops ------------------------------------------------------

    private static bool RewriteListRef(Dictionary<string, object?> fm, string key, string oldRef, string newRef)
    {
        if (!fm.TryGetValue(key, out var v) || v is not List<object?> list) return false;
        var changed = false;
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i]?.ToString(), oldRef, StringComparison.Ordinal))
            {
                list[i] = newRef;
                changed = true;
            }
        }
        return changed;
    }

    private static bool RewriteScalarRef(Dictionary<string, object?> fm, string key, string oldRef, string newRef)
    {
        if (!fm.TryGetValue(key, out var v) || !string.Equals(v?.ToString(), oldRef, StringComparison.Ordinal))
            return false;
        fm[key] = newRef;
        return true;
    }

    private static bool RewriteFeatureSlug(Dictionary<string, object?> fm, string oldSlug, string newSlug)
    {
        if (!fm.TryGetValue("feature", out var v)) return false;
        if (v is List<object?> list)
        {
            var changed = false;
            for (var i = 0; i < list.Count; i++)
                if (string.Equals(list[i]?.ToString(), oldSlug, StringComparison.Ordinal))
                {
                    list[i] = newSlug;
                    changed = true;
                }
            return changed;
        }
        if (v is string s && string.Equals(s, oldSlug, StringComparison.Ordinal))
        {
            fm["feature"] = newSlug;
            return true;
        }
        return false;
    }

    private static bool RemoveListRefs(Dictionary<string, object?> fm, string key, IReadOnlySet<string> refs)
    {
        if (!fm.TryGetValue(key, out var v) || v is not List<object?> list) return false;
        var kept = list.Where(x => !refs.Contains(x?.ToString() ?? "")).ToList();
        if (kept.Count == list.Count) return false;
        fm[key] = kept;
        return true;
    }

    private static bool ClearScalarRef(Dictionary<string, object?> fm, string key, IReadOnlySet<string> refs)
    {
        if (!fm.TryGetValue(key, out var v) || !refs.Contains(v?.ToString() ?? "")) return false;
        fm[key] = null;
        return true;
    }

    private static bool RemoveFeatureSlugs(Dictionary<string, object?> fm, IReadOnlySet<string> slugs)
    {
        if (!fm.TryGetValue("feature", out var v)) return false;
        if (v is List<object?> list)
        {
            var kept = list.Where(x => !slugs.Contains(x?.ToString() ?? "")).ToList();
            if (kept.Count == list.Count) return false;
            fm["feature"] = kept;
            return true;
        }
        if (v is string s && slugs.Contains(s))
        {
            fm["feature"] = null;
            return true;
        }
        return false;
    }
}
