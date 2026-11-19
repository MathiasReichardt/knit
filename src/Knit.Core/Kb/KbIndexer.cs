using System.Globalization;
using System.Text;
using Knit.Core.IO;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Generates the per-bucket <c>index.md</c> files and the master
/// <c>kb-index.md</c>. Reads <strong>frontmatter only</strong> — never bodies —
/// so per-mutation cost stays linear in file count.
/// </summary>
public sealed class KbIndexer
{
    private readonly KbStore _store;
    private readonly string _repoRoot;

    public KbIndexer(KbStore store)
    {
        _store = store;
        _repoRoot = store.RepoRoot;
    }

    /// <summary>
    /// Regenerate every <c>index.md</c> + the master <c>kb-index.md</c>.
    /// Returns the list of files written.
    /// </summary>
    public IReadOnlyList<string> RebuildAll()
    {
        var written = new List<string>();
        foreach (var bucket in BucketStrings.All)
        {
            if (bucket == Bucket.Item) continue;
            var bucketDir = KbLayout.BucketDir(_repoRoot, bucket);
            if (!Directory.Exists(bucketDir)) continue;
            var path = WriteBucketIndex(bucket);
            if (path is not null) written.Add(path);
        }

        var master = WriteMasterIndex();
        if (master is not null) written.Add(master);
        return written;
    }

    /// <summary>
    /// Rebuild a single bucket's <c>index.md</c> plus the master index.
    /// </summary>
    public IReadOnlyList<string> RebuildBucketAndMaster(Bucket bucket)
    {
        var written = new List<string>();
        var bucketPath = WriteBucketIndex(bucket);
        if (bucketPath is not null) written.Add(bucketPath);
        var master = WriteMasterIndex();
        if (master is not null) written.Add(master);
        return written;
    }

    private string? WriteBucketIndex(Bucket bucket)
    {
        var dir = KbLayout.BucketDir(_repoRoot, bucket);
        if (!Directory.Exists(dir)) return null;
        var entries = _store.ListBucket(bucket)
            .Select(x =>
            {
                var read = _store.ReadPath(x.Path);
                var fm = read?.Frontmatter ?? new Dictionary<string, object?>();
                return new IndexEntry(x.Ref, x.Path, fm);
            })
            .OrderBy(e => e.Ref.Slug, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"# {bucket.Folder()}\n\n");
        if (bucket.AllowsGrowToFolder())
            RenderNested(sb, BuildTree(entries), 0, e => FormatBucketLine(bucket, e, dir));
        else
            foreach (var e in entries)
            {
                sb.Append(FormatBucketLine(bucket, e, dir));
                sb.Append('\n');
            }
        var path = KbLayout.PerFolderIndexPath(_repoRoot, bucket);
        AtomicFile.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string FormatBucketLine(Bucket bucket, IndexEntry e, string bucketDir)
    {
        var title = AsString(e.Frontmatter, "title");
        var summary = AsString(e.Frontmatter, "summary");
        var fileForm = e.Ref.Slug + ".md";
        var fileExists = File.Exists(Path.Combine(bucketDir, fileForm));
        var link = !fileExists && bucket.AllowsGrowToFolder()
            ? $"{e.Ref.Slug}/readme.md"
            : fileForm;
        if (bucket == Bucket.Adr)
        {
            var id = AsString(e.Frontmatter, "id") ?? "????";
            // Nygard ADR titles are stored with the `NNNN.` prefix in
            // frontmatter; strip it for the index row so we don't render
            // `[0001 — 0001. Pick storage]`. The id already plays the ref
            // role, so ADR lines carry no `bucket:slug` code-span.
            var displayTitle = StripAdrNumberPrefix(title ?? e.Ref.Slug, id);
            var line = $"- [{id} — {displayTitle}]({link})";
            return string.IsNullOrEmpty(summary) ? line : $"{line} — {summary}";
        }
        // Format B: title-first link text, ref as a copy-pasteable code-span.
        // Nested entries show only their leaf segment as the link text.
        var prefix = $"- [{DisplayText(title, e.Ref.Slug)}]({link}) `{e.Ref}`";
        return string.IsNullOrEmpty(summary) ? prefix : $"{prefix} — {summary}";
    }

    private string? WriteMasterIndex()
    {
        var kbDir = KbLayout.KbDir(_repoRoot);
        if (!Directory.Exists(kbDir)) return null;
        var sb = new StringBuilder();
        sb.Append("# Knit Knowledge Base\n\n");

        AppendSection(sb, Bucket.Feature, "Features", limit: null);
        AppendSection(sb, Bucket.Doc, "Cross-cutting docs", limit: null);
        AppendAdrLatest(sb, limit: 10);
        AppendLogLatest(sb, limit: 5);
        AppendSection(sb, Bucket.Source, "External sources", limit: null);
        AppendSection(sb, Bucket.Idea, "Ideas", limit: null);

        var milestones = Path.Combine(kbDir, "milestones.md");
        if (File.Exists(milestones))
        {
            sb.Append("\n## Milestones\n");
            sb.Append("- [milestones.md](milestones.md)\n");
        }

        var path = KbLayout.MasterIndexPath(_repoRoot);
        AtomicFile.WriteAllText(path, sb.ToString());
        return path;
    }

    private void AppendSection(StringBuilder sb, Bucket bucket, string heading, int? limit)
    {
        var entries = _store.ListBucket(bucket)
            .Select(x =>
            {
                var read = _store.ReadPath(x.Path);
                return new IndexEntry(x.Ref, x.Path, read?.Frontmatter ?? new Dictionary<string, object?>());
            })
            .OrderBy(e => e.Ref.Slug, StringComparer.Ordinal)
            .ToList();
        if (entries.Count == 0) return;
        sb.Append($"\n## {heading}\n");
        if (bucket.AllowsGrowToFolder() && !limit.HasValue)
        {
            RenderNested(sb, BuildTree(entries), 0, FormatMasterLine);
            return;
        }
        var taken = limit.HasValue ? entries.Take(limit.Value) : entries;
        foreach (var e in taken)
        {
            sb.Append(FormatMasterLine(e));
            sb.Append('\n');
        }
    }

    // Format B for the master index: title-first link text + ref code-span.
    // Nested entries render their leaf segment; ADR/log use their own forms.
    private string FormatMasterLine(IndexEntry e)
    {
        var title = AsString(e.Frontmatter, "title");
        var summary = AsString(e.Frontmatter, "summary");
        var rel = RelativeFromKb(e.Path);
        var line = $"- [{DisplayText(title, e.Ref.Slug)}]({rel}) `{e.Ref}`";
        return string.IsNullOrEmpty(summary) ? line : $"{line} — {summary}";
    }

    private void AppendAdrLatest(StringBuilder sb, int limit)
    {
        var entries = _store.ListBucket(Bucket.Adr)
            .Select(x => new { x.Ref, x.Path, FM = _store.ReadPath(x.Path)?.Frontmatter ?? new Dictionary<string, object?>() })
            .Select(x => new
            {
                x.Ref,
                x.Path,
                x.FM,
                Number = ParseAdrNumber(x.Ref.Slug),
            })
            .OrderByDescending(x => x.Number)
            .Take(limit)
            .ToList();
        if (entries.Count == 0) return;
        sb.Append($"\n## Latest decisions (most recent {limit})\n");
        foreach (var e in entries)
        {
            var title = AsString(e.FM, "title") ?? e.Ref.Slug;
            var date = AsString(e.FM, "date") ?? "";
            var summary = AsString(e.FM, "summary");
            var rel = RelativeFromKb(e.Path);
            var id = AsString(e.FM, "id") ?? "";
            // ADR keeps the `[NNNN — title]` form; the id is the ref, so no
            // code-span (matches the per-bucket ADR index line).
            var line = $"- [{id} — {StripAdrNumberPrefix(title, id)}]({rel}) — {date}";
            sb.Append(string.IsNullOrEmpty(summary) ? line : $"{line} — {summary}");
            sb.Append('\n');
        }
    }

    private void AppendLogLatest(StringBuilder sb, int limit)
    {
        var entries = _store.ListBucket(Bucket.Log)
            .Select(x => new { x.Ref, x.Path, FM = _store.ReadPath(x.Path)?.Frontmatter ?? new Dictionary<string, object?>() })
            .OrderByDescending(x => AsString(x.FM, "date") ?? x.Ref.Slug, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        if (entries.Count == 0) return;
        sb.Append($"\n## Latest log (most recent {limit})\n");
        foreach (var e in entries)
        {
            var title = AsString(e.FM, "title") ?? e.Ref.Slug;
            var date = AsString(e.FM, "date") ?? "";
            var summary = AsString(e.FM, "summary");
            var rel = RelativeFromKb(e.Path);
            // Keep the leading date column; append the ref code-span after the
            // title (format B with the date kept for log scanability).
            var line = $"- {date} — [{title}]({rel}) `{e.Ref}`";
            sb.Append(string.IsNullOrEmpty(summary) ? line : $"{line} — {summary}");
            sb.Append('\n');
        }
    }

    /// <summary>
    /// Build a forest from slug-ordered entries, nesting each sub-doc under
    /// the nearest ancestor that exists as its own entry (folder-form
    /// <c>readme.md</c>). N-level by construction; today only one level of
    /// nesting occurs. Orphaned sub-docs (no existing ancestor) stay at the
    /// root. Children preserve the incoming slug order.
    /// </summary>
    private static List<IndexNode> BuildTree(IReadOnlyList<IndexEntry> entries)
    {
        var nodes = new Dictionary<string, IndexNode>(StringComparer.Ordinal);
        foreach (var e in entries) nodes[e.Ref.Slug] = new IndexNode(e);

        var slugs = new HashSet<string>(nodes.Keys, StringComparer.Ordinal);
        var roots = new List<IndexNode>();
        foreach (var e in entries)
        {
            var parent = NearestParentSlug(e.Ref.Slug, slugs);
            if (parent is null) roots.Add(nodes[e.Ref.Slug]);
            else nodes[parent].Children.Add(nodes[e.Ref.Slug]);
        }
        return roots;
    }

    private static void RenderNested(
        StringBuilder sb, List<IndexNode> nodes, int depth, Func<IndexEntry, string> format)
    {
        foreach (var n in nodes)
        {
            sb.Append(' ', depth * 2);
            sb.Append(format(n.Entry));
            sb.Append('\n');
            if (n.Children.Count > 0) RenderNested(sb, n.Children, depth + 1, format);
        }
    }

    private static string? NearestParentSlug(string slug, HashSet<string> all)
    {
        var idx = slug.LastIndexOf('/');
        while (idx > 0)
        {
            var prefix = slug[..idx];
            if (all.Contains(prefix)) return prefix;
            idx = prefix.LastIndexOf('/');
        }
        return null;
    }

    private static string LeafSlug(string slug)
    {
        var idx = slug.LastIndexOf('/');
        return idx >= 0 ? slug[(idx + 1)..] : slug;
    }

    /// <summary>
    /// Link text for a format-B line: a real custom title when present,
    /// otherwise the slug's leaf segment. A title that merely echoes the
    /// full slug (the create-time default for feature/doc, including the
    /// nested <c>parent/leaf</c> path) is treated as "no real title" so
    /// nested entries render their leaf (<c>wiring</c>, not
    /// <c>user-mgmt/wiring</c>).
    /// </summary>
    private static string DisplayText(string? title, string slug) =>
        !string.IsNullOrEmpty(title) && !string.Equals(title, slug, StringComparison.Ordinal)
            ? title
            : LeafSlug(slug);

    private string RelativeFromKb(string fullPath)
    {
        var kbDir = KbLayout.KbDir(_repoRoot);
        return Path.GetRelativePath(kbDir, fullPath).Replace('\\', '/');
    }

    private static int ParseAdrNumber(string slug)
    {
        var dash = slug.IndexOf('-');
        var prefix = dash > 0 ? slug[..dash] : slug;
        return int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static string StripAdrNumberPrefix(string title, string id)
    {
        var p = $"{id}. ";
        return title.StartsWith(p, StringComparison.Ordinal) ? title[p.Length..] : title;
    }

    private static string? AsString(IReadOnlyDictionary<string, object?> m, string k) =>
        m.TryGetValue(k, out var v) ? v?.ToString() : null;

    private sealed record IndexEntry(Ref Ref, string Path, IReadOnlyDictionary<string, object?> Frontmatter);

    private sealed record IndexNode(IndexEntry Entry)
    {
        public List<IndexNode> Children { get; } = [];
    }
}
