using System.Globalization;
using System.Text;
using Knit.Core.Designs;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Kb;
using Knit.Core.Refs;

namespace Knit.Data.Search;

/// <summary>
/// Stateless full-text searcher: reads the authoritative store/files on every
/// query and scans them in memory. No SQLite, no derived cache, no warm-up —
/// the corpus is a single repo, small enough to scan behind the UI's debounce.
///
/// Tokens are maximal letter/digit runs, diacritics folded, matched
/// case-insensitively as plain <b>substrings</b> within a word (so <c>cat</c>
/// matches both <c>category</c> and <c>scatter</c>) — one Ctrl+F-like rule
/// shared with the client highlighter. A multi-token query is an AND
/// across all of an entity's searchable fields. Hits are ranked by match
/// quality (exact word &gt; word-start prefix &gt; title substring &gt; other
/// substring) so a prefix still outranks a mid-word hit.
/// </summary>
public sealed class LiveScanSearcher : ISearcher
{
    private readonly string _repoRoot;
    private readonly IFrontmatterSerializer _yaml;
    private readonly ItemCache? _cache;

    public LiveScanSearcher(string repoRoot, IFrontmatterSerializer? yaml = null, ItemCache? cache = null)
    {
        _repoRoot = repoRoot;
        _yaml = yaml ?? new SimpleFrontmatterSerializer();
        _cache = cache;
    }

    public IReadOnlyList<SearchHit> Query(string query, SearchKind kind, int limit)
    {
        var qTokens = Tokenize(query);
        if (qTokens.Count == 0 || limit <= 0) return [];

        var scored = new List<Scored>();
        if (kind is SearchKind.All or SearchKind.Item) ScanItems(qTokens, scored);
        if (kind is SearchKind.All or SearchKind.Kb) ScanKb(qTokens, scored);
        if (kind is SearchKind.All or SearchKind.Design) ScanDesigns(qTokens, scored);
        if (kind is SearchKind.All or SearchKind.Project) ScanProjects(qTokens, scored);

        // Match quality first (exact > prefix > title-substring > other), then
        // title-region hits, then higher term-frequency, then alpha. Fine for a
        // navigation search box; `auth` surfaces `authentication` above `oauth`
        // while still including `oauth`.
        scored.Sort(static (a, b) =>
        {
            if (a.Quality != b.Quality) return b.Quality.CompareTo(a.Quality);
            if (a.TitleHit != b.TitleHit) return a.TitleHit ? -1 : 1;
            if (a.Frequency != b.Frequency) return b.Frequency.CompareTo(a.Frequency);
            return string.Compare(a.Hit.Title, b.Hit.Title, StringComparison.OrdinalIgnoreCase);
        });

        if (scored.Count <= limit)
            return scored.ConvertAll(s => s.Hit);
        return scored.GetRange(0, limit).ConvertAll(s => s.Hit);
    }

    public SearchHit? LookupRef(Ref r)
    {
        var read = new KbStore(_repoRoot, _yaml).Read(r);
        if (read is null) return null;
        var (fm, body) = read.Value;
        var title = TitleFrom(fm, body, fallback: r.Slug);
        return new SearchHit(SearchKind.Kb, r.ToString(), title, "", MatchField.Title, null);
    }

    // ---------- per-kind scans ----------

    private void ScanItems(List<string> q, List<Scored> sink)
    {
        var store = new ItemStore(_repoRoot, _yaml, _cache);
        foreach (var item in store.AllItems())
        {
            var fields = new List<Region>(2 + item.Comments.Count)
            {
                new(MatchField.Title, item.Title),
                new(MatchField.Body, item.Body),
            };
            foreach (var c in item.Comments) fields.Add(new(MatchField.Comment, c.Body));
            TryMatch(q, SearchKind.Item, item.Id, item.Title, fields, sink);
        }
    }

    private void ScanKb(List<string> q, List<Scored> sink)
    {
        var store = new KbStore(_repoRoot, _yaml);
        foreach (var path in KbLayout.EnumerateArtifactFiles(_repoRoot))
        {
            var r = KbRefFromPath(path);
            if (r is null) continue;
            var read = store.ReadPath(path);
            if (read is null) continue;
            var (fm, body) = read.Value;
            var title = TitleFrom(fm, body, fallback: r.Slug);
            var fields = new List<Region>
            {
                new(MatchField.Title, title),
                new(MatchField.Body, body),
                new(MatchField.Tags, TagsText(fm)),
            };
            TryMatch(q, SearchKind.Kb, r.ToString(), title, fields, sink);
        }
    }

    private void ScanDesigns(List<string> q, List<Scored> sink)
    {
        foreach (var path in DesignsLayout.ActiveDesignDocs(_repoRoot))
            ScanDesignFile(q, path, sink);
        foreach (var path in DesignsLayout.CompletedDesignDocs(_repoRoot))
            ScanDesignFile(q, path, sink);
    }

    // Scan every markdown doc under a project folder — readme, roadmap,
    // milestones and free-form docs alike. The id is <project>/<rel-no-.md>,
    // matching the FileWatcher `projects` SSE id so a hit addresses the same
    // resource the SPA refreshes on.
    private void ScanProjects(List<string> q, List<Scored> sink)
    {
        foreach (var project in Knit.Core.Projects.ProjectsLayout.ProjectSlugs(_repoRoot))
        {
            var dir = Knit.Core.Projects.ProjectsLayout.ProjectDir(_repoRoot, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
                var slugRel = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? rel[..^3] : rel;
                var lastSeg = slugRel.Split('/')[^1];
                var (fm, body) = ReadFile(path);
                var title = TitleFrom(fm, body, fallback: lastSeg);
                var fields = new List<Region>
                {
                    new(MatchField.Title, title),
                    new(MatchField.Body, body),
                    new(MatchField.Tags, TagsText(fm)),
                };
                TryMatch(q, SearchKind.Project, $"{project}/{slugRel}", title, fields, sink);
            }
        }
    }

    private void ScanDesignFile(List<string> q, string path, List<Scored> sink)
    {
        var slug = DesignsLayout.SlugFromPath(path);
        var (fm, body) = ReadFile(path);
        var title = TitleFrom(fm, body, fallback: slug);
        var fields = new List<Region>
        {
            new(MatchField.Title, title),
            new(MatchField.Body, body),
        };
        TryMatch(q, SearchKind.Design, slug, title, fields, sink);
    }

    // ---------- matching ----------

    private readonly record struct Region(MatchField Field, string Text);

    private sealed record Scored(SearchHit Hit, int Quality, bool TitleHit, int Frequency);

    /// <summary>
    /// AND-match every query token across the union of an entity's fields; if
    /// all match, build the hit (region + snippet) and add it to the sink. The
    /// hit's <see cref="Scored.Quality"/> is its weakest token's best quality
    /// (a hit ranks by its worst-matching term).
    /// </summary>
    private static void TryMatch(
        List<string> q, SearchKind kind, string id, string title,
        List<Region> fields, List<Scored> sink)
    {
        var fieldToks = new List<Tok>[fields.Count];
        for (var i = 0; i < fields.Count; i++) fieldToks[i] = TokenizeWithOffsets(fields[i].Text);

        var frequency = 0;
        var hitQuality = int.MaxValue;
        foreach (var qt in q)
        {
            var bestForToken = 0;
            for (var i = 0; i < fields.Count; i++)
            {
                var isTitle = fields[i].Field == MatchField.Title;
                foreach (var t in fieldToks[i])
                {
                    var qual = Quality(t.Folded, qt, isTitle);
                    if (qual > 0) frequency++;
                    if (qual > bestForToken) bestForToken = qual;
                }
            }
            if (bestForToken == 0) return; // AND: a missing token fails the row
            if (bestForToken < hitQuality) hitQuality = bestForToken;
        }

        // Pick the highest-priority field (Title > Body > Comment > Tags) that
        // contains a match — that is the region we report and snippet from.
        var bestField = -1;
        foreach (var pref in PriorityOrder)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Field != pref) continue;
                if (AnyMatch(fieldToks[i], q)) { bestField = i; break; }
            }
            if (bestField >= 0) break;
        }
        if (bestField < 0) return; // unreachable when frequency > 0, but be safe

        var region = fields[bestField];
        var toks = fieldToks[bestField];
        var anchor = FirstMatchIndex(toks, q);
        var snippet = BuildSnippet(region.Text, toks, anchor);
        int? bodyOffset = region.Field == MatchField.Body && anchor >= 0 ? toks[anchor].Start : null;
        var titleHit = region.Field == MatchField.Title;

        sink.Add(new Scored(
            new SearchHit(kind, id, title, snippet, region.Field, bodyOffset),
            hitQuality, titleHit, frequency));
    }

    /// <summary>
    /// Match quality of one folded corpus token against one folded query token:
    /// 4 exact word · 3 word-start prefix · 2 mid-word substring in a title ·
    /// 1 mid-word substring elsewhere · 0 no match.
    /// </summary>
    private static int Quality(string tok, string qt, bool isTitle)
    {
        if (!tok.Contains(qt, StringComparison.Ordinal)) return 0;
        if (tok.Length == qt.Length) return 4;
        if (tok.StartsWith(qt, StringComparison.Ordinal)) return 3;
        return isTitle ? 2 : 1;
    }

    private static readonly MatchField[] PriorityOrder =
        [MatchField.Title, MatchField.Body, MatchField.Comment, MatchField.Tags];

    private static bool AnyMatch(List<Tok> toks, List<string> q)
    {
        foreach (var t in toks)
            foreach (var qt in q)
                if (t.Folded.Contains(qt, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int FirstMatchIndex(List<Tok> toks, List<string> q)
    {
        for (var k = 0; k < toks.Count; k++)
            foreach (var qt in q)
                if (toks[k].Folded.Contains(qt, StringComparison.Ordinal)) return k;
        return -1;
    }

    // ---------- snippet ----------

    private const int SnippetTokens = 16;
    private const int SnippetLead = 4;

    /// <summary>
    /// A ~16-token window of <b>plain text</b> around the first matching token,
    /// bounded by <c>…</c> when clipped. Highlighting is a UI concern — the
    /// client re-finds the matches in this text and paints them — so the
    /// snippet carries no markup and needs no HTML escaping.
    /// </summary>
    private static string BuildSnippet(string text, List<Tok> toks, int anchor)
    {
        if (toks.Count == 0) return Collapse(text.Length > 80 ? text[..80] : text);
        if (anchor < 0) anchor = 0;

        var from = Math.Max(0, anchor - SnippetLead);
        var to = Math.Min(toks.Count - 1, from + SnippetTokens - 1);
        var startChar = toks[from].Start;
        var endChar = toks[to].Start + toks[to].Length;

        var sb = new StringBuilder();
        if (from > 0) sb.Append('…');
        sb.Append(text[startChar..endChar]);
        if (to < toks.Count - 1) sb.Append('…');
        return Collapse(sb.ToString());
    }

    /// <summary>Collapse whitespace runs to a single space so multi-line
    /// snippets render on one line.</summary>
    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var inWs = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWs) sb.Append(' ');
                inWs = true;
            }
            else { sb.Append(ch); inWs = false; }
        }
        return sb.ToString().Trim();
    }

    // ---------- tokenization ----------

    private readonly record struct Tok(int Start, int Length, string Folded);

    private static List<string> Tokenize(string s)
    {
        var toks = new List<string>();
        foreach (var t in TokenizeWithOffsets(s)) toks.Add(t.Folded);
        return toks;
    }

    private static List<Tok> TokenizeWithOffsets(string s)
    {
        var toks = new List<Tok>();
        if (string.IsNullOrEmpty(s)) return toks;
        int i = 0, n = s.Length;
        while (i < n)
        {
            while (i < n && !char.IsLetterOrDigit(s[i])) i++;
            var start = i;
            while (i < n && char.IsLetterOrDigit(s[i])) i++;
            if (i > start)
            {
                var folded = Fold(s.Substring(start, i - start));
                if (folded.Length > 0) toks.Add(new Tok(start, i - start, folded));
            }
        }
        return toks;
    }

    /// <summary>
    /// Lowercase + strip diacritics to mirror FTS5 <c>remove_diacritics 1</c>
    /// (<c>café</c> == <c>cafe</c>).
    ///
    /// The CLI publishes with <c>InvariantGlobalization=true</c> (smaller AOT
    /// binary, no ICU), under which <see cref="string.Normalize"/> is a no-op —
    /// so FormD decomposition alone does not fold precomposed accents. We
    /// still run FormD first (it handles already-decomposed input and the
    /// non-invariant host) and drop combining marks, then fold the common
    /// precomposed Latin-1 letters explicitly so folding works either way.
    /// Coverage is Latin scripts; non-Latin diacritics are left as-is.
    /// </summary>
    private static string Fold(string s)
    {
        var norm = SafeNormalize(s);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            AppendFolded(sb, char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string SafeNormalize(string s)
    {
        try { return s.Normalize(NormalizationForm.FormD); }
        catch { return s; } // invariant globalization can reject normalization
    }

    private static void AppendFolded(StringBuilder sb, char c)
    {
        switch (c)
        {
            case >= 'à' and <= 'å': sb.Append('a'); break; // à á â ã ä å
            case 'æ': sb.Append("ae"); break;                   // æ
            case 'ç': sb.Append('c'); break;                    // ç
            case >= 'è' and <= 'ë': sb.Append('e'); break; // è é ê ë
            case >= 'ì' and <= 'ï': sb.Append('i'); break; // ì í î ï
            case 'ð': sb.Append('d'); break;                    // ð
            case 'ñ': sb.Append('n'); break;                    // ñ
            case >= 'ò' and <= 'ö': sb.Append('o'); break; // ò ó ô õ ö
            case 'ø': sb.Append('o'); break;                    // ø
            case >= 'ù' and <= 'ü': sb.Append('u'); break; // ù ú û ü
            case 'ý' or 'ÿ': sb.Append('y'); break;        // ý ÿ
            case 'œ': sb.Append("oe"); break;                   // œ
            default: sb.Append(c); break;
        }
    }

    // ---------- file / frontmatter helpers (ported from the old index) ----------

    private (IReadOnlyDictionary<string, object?> Fm, string Body) ReadFile(string path)
    {
        string content;
        try { content = AtomicFile.ReadAllText(path).Replace("\r\n", "\n"); }
        catch { return (Empty, ""); }
        if (!content.StartsWith("---", StringComparison.Ordinal)) return (Empty, content);
        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return (Empty, content);
        var fmText = content[3..end].TrimStart('\n');
        var body = content[(end + 4)..].TrimStart('\n');
        return (_yaml.Parse(fmText), body);
    }

    private static readonly Dictionary<string, object?> Empty = new();

    private static string TitleFrom(IReadOnlyDictionary<string, object?> fm, string body, string fallback)
    {
        if (fm.TryGetValue("title", out var tv) && tv is string s && !string.IsNullOrWhiteSpace(s))
            return s.Trim();
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return fallback;
    }

    private static string TagsText(IReadOnlyDictionary<string, object?> fm)
    {
        if (!fm.TryGetValue("tags", out var v) || v is null) return "";
        if (v is List<object?> list)
            return string.Join(' ', list.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        return v.ToString() ?? "";
    }

    /// <summary>
    /// Map an absolute path under <c>.knit/kb/</c> back to a (bucket, slug)
    /// ref. Returns null for index files that aren't authored entries.
    /// </summary>
    private static Ref? KbRefFromPath(string path)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, KbLayout.MasterIndexFile, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(name, KbLayout.PerFolderIndexFile, StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(path);
        var segs = new List<string>();
        while (dir != null)
        {
            var folder = Path.GetFileName(dir);
            if (BucketFolders.TryFromFolder(folder, out var bucket))
            {
                var slugBase = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
                string slug;
                if (string.Equals(slugBase, "readme", StringComparison.OrdinalIgnoreCase) && segs.Count > 0)
                    slug = string.Join('/', ((IEnumerable<string>)segs).Reverse());
                else
                {
                    segs.Reverse();
                    segs.Add(slugBase);
                    slug = string.Join('/', segs);
                }
                return new Ref(bucket, slug);
            }
            segs.Add(folder);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
