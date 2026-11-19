using System.Globalization;
using Knit.Core.Frontmatter;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Frontmatter shapes for KB artifacts. One record per bucket. All records
/// carry a common <c>title</c>/<c>summary</c>/<c>tags</c>/<c>feature</c>
/// payload plus bucket-specific extras. Type is implicit in folder location
/// — no <c>kind:</c> field is emitted (per design Decision).
/// </summary>
public sealed record AdrFrontmatter(
    string Id,
    string Title,
    DateOnly? Date,
    string Status,
    IReadOnlyList<string> Supersedes,
    string? SupersededBy,
    IReadOnlyList<string> Feature,
    // The design cycle / item the decision emerged from, as `design:<slug>`
    // and/or `item:<id>` refs. Distinct from `feature:` (the feature
    // living-doc the decision shapes). Lets a reader trace an ADR's origin.
    IReadOnlyList<string> Origin,
    IReadOnlyList<string> Tags,
    string? Summary);

public sealed record DocFrontmatter(
    string Title,
    string? Feature,
    IReadOnlyList<string> Tags,
    string? Summary);

public sealed record FeatureFrontmatter(
    string Title,
    string? Feature,
    IReadOnlyList<string> Tags,
    string? Summary);

public sealed record LogFrontmatter(
    string Title,
    DateOnly? Date,
    IReadOnlyList<string> Feature,
    IReadOnlyList<string> Tags,
    string? Summary);

public sealed record IdeaFrontmatter(
    string Title,
    string? Feature,
    IReadOnlyList<string> Tags,
    string? Summary);

public sealed record SourceFrontmatter(
    string Id,
    string Title,
    string SourceKind, // "link" or "file"
    string? Url,
    string? Path,
    IReadOnlyList<string> Tags,
    string? Summary,
    // Name of the config mount `Path` is relative to. Absent means the
    // path resolves against the repo root, exactly as before mounts existed.
    string? Mount = null);

/// <summary>
/// Round-trip helpers between the bucket-specific records and the flat
/// frontmatter map used by <see cref="IFrontmatterSerializer"/>.
/// </summary>
public static class KbFrontmatterMap
{
    public static Dictionary<string, object?> ToMap(AdrFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["id"] = f.Id;
        m["title"] = f.Title;
        if (f.Date.HasValue) m["date"] = f.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        m["status"] = f.Status;
        m["supersedes"] = f.Supersedes.Select(r => (object?)r).ToList();
        m["superseded_by"] = (object?)f.SupersededBy;
        m["feature"] = f.Feature.Select(s => (object?)s).ToList();
        m["origin"] = f.Origin.Select(s => (object?)s).ToList();
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static AdrFrontmatter ReadAdr(IReadOnlyDictionary<string, object?> m) => new(
        Id: AsString(m, "id") ?? "",
        Title: AsString(m, "title") ?? "",
        Date: AsDate(m, "date"),
        Status: AsString(m, "status") ?? "proposed",
        Supersedes: AsStringList(m, "supersedes"),
        SupersededBy: AsString(m, "superseded_by"),
        Feature: AsStringList(m, "feature"),
        Origin: AsStringList(m, "origin"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"));

    public static Dictionary<string, object?> ToMap(DocFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["title"] = f.Title;
        m["feature"] = (object?)f.Feature;
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static DocFrontmatter ReadDoc(IReadOnlyDictionary<string, object?> m) => new(
        Title: AsString(m, "title") ?? "",
        Feature: AsString(m, "feature"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"));

    public static Dictionary<string, object?> ToMap(FeatureFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["title"] = f.Title;
        m["feature"] = (object?)f.Feature;
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static FeatureFrontmatter ReadFeature(IReadOnlyDictionary<string, object?> m) => new(
        Title: AsString(m, "title") ?? "",
        Feature: AsString(m, "feature"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"));

    public static Dictionary<string, object?> ToMap(LogFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["title"] = f.Title;
        if (f.Date.HasValue) m["date"] = f.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        m["feature"] = f.Feature.Select(s => (object?)s).ToList();
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static LogFrontmatter ReadLog(IReadOnlyDictionary<string, object?> m) => new(
        Title: AsString(m, "title") ?? "",
        Date: AsDate(m, "date"),
        Feature: AsStringList(m, "feature"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"));

    public static Dictionary<string, object?> ToMap(IdeaFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["title"] = f.Title;
        m["feature"] = (object?)f.Feature;
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static IdeaFrontmatter ReadIdea(IReadOnlyDictionary<string, object?> m) => new(
        Title: AsString(m, "title") ?? "",
        Feature: AsString(m, "feature"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"));

    public static Dictionary<string, object?> ToMap(SourceFrontmatter f)
    {
        var m = new Dictionary<string, object?>(StringComparer.Ordinal);
        m["id"] = f.Id;
        m["title"] = f.Title;
        m["source_kind"] = f.SourceKind;
        m["url"] = (object?)f.Url;
        m["path"] = (object?)f.Path;
        if (!string.IsNullOrEmpty(f.Mount)) m["mount"] = f.Mount;
        m["tags"] = f.Tags.Select(s => (object?)s).ToList();
        if (!string.IsNullOrEmpty(f.Summary)) m["summary"] = f.Summary;
        return m;
    }

    public static SourceFrontmatter ReadSource(IReadOnlyDictionary<string, object?> m) => new(
        Id: AsString(m, "id") ?? "",
        Title: AsString(m, "title") ?? "",
        SourceKind: AsString(m, "source_kind") ?? "link",
        Url: AsString(m, "url"),
        Path: AsString(m, "path"),
        Tags: AsStringList(m, "tags"),
        Summary: AsString(m, "summary"),
        Mount: AsString(m, "mount"));

    private static string? AsString(IReadOnlyDictionary<string, object?> m, string k) =>
        m.TryGetValue(k, out var v) ? v?.ToString() : null;

    private static IReadOnlyList<string> AsStringList(IReadOnlyDictionary<string, object?> m, string k)
    {
        if (!m.TryGetValue(k, out var v)) return [];
        if (v is List<object?> list) return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        if (v is string s && !string.IsNullOrEmpty(s)) return [s];
        return [];
    }

    private static DateOnly? AsDate(IReadOnlyDictionary<string, object?> m, string k)
    {
        var s = AsString(m, k);
        if (string.IsNullOrEmpty(s)) return null;
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
