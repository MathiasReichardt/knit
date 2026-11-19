using Knit.Core.Kb;
using Knit.Core.Refs;
using Knit.WebApi.Contracts;

namespace Knit.WebApi.Mapping;

public static class KbMapping
{
    public static KbFrontmatterDto ReadFrontmatter(IReadOnlyDictionary<string, object?> fm) => new(
        Id: AsString(fm, "id"),
        Title: AsString(fm, "title"),
        Date: AsString(fm, "date"),
        Status: AsString(fm, "status"),
        Supersedes: AsStringList(fm, "supersedes"),
        SupersededBy: AsString(fm, "superseded_by"),
        Feature: AsFeatureList(fm),
        Origin: AsStringList(fm, "origin"),
        Tags: AsStringList(fm, "tags"),
        Summary: AsString(fm, "summary"),
        SourceKind: AsString(fm, "source_kind"),
        Url: AsString(fm, "url"),
        Path: AsString(fm, "path"),
        Mount: AsString(fm, "mount"));

    /// <summary>
    /// Merge a typed frontmatter DTO into the existing on-disk dictionary,
    /// preserving keys we don't model. Empty/null fields drop their keys.
    /// </summary>
    public static Dictionary<string, object?> Apply(
        IReadOnlyDictionary<string, object?> existing,
        KbFrontmatterDto patch,
        Bucket bucket)
    {
        var m = new Dictionary<string, object?>(existing, StringComparer.Ordinal);

        SetOrDrop(m, "id", patch.Id);
        SetOrDrop(m, "title", patch.Title);
        SetOrDrop(m, "date", patch.Date);
        SetOrDrop(m, "status", patch.Status);
        SetListOrDrop(m, "supersedes", patch.Supersedes);
        SetOrDrop(m, "superseded_by", patch.SupersededBy);

        // Feature shape varies per bucket: list-typed (adr, log) vs single
        // string (doc, feature, idea). Preserve the existing shape so
        // KbIndexer's filter logic keeps working.
        if (BucketUsesFeatureList(bucket))
            SetListOrDrop(m, "feature", patch.Feature);
        else
            SetOrDrop(m, "feature", patch.Feature.Count > 0 ? patch.Feature[0] : null);

        SetListOrDrop(m, "origin", patch.Origin);
        SetListOrDrop(m, "tags", Tags.Normalize(patch.Tags));
        SetOrDrop(m, "summary", patch.Summary);
        SetOrDrop(m, "source_kind", patch.SourceKind);
        SetOrDrop(m, "url", patch.Url);
        SetOrDrop(m, "path", patch.Path);
        SetOrDrop(m, "mount", patch.Mount);

        return m;
    }

    public static KbDto ToDto(Ref r, string filePath, IReadOnlyDictionary<string, object?> fm, string body)
    {
        var fmDto = ReadFrontmatter(fm);
        var title = fmDto.Title ?? r.Slug;
        return new KbDto(
            Bucket: r.Bucket.ToWire(),
            Slug: r.Slug,
            Ref: r.ToString(),
            Title: title,
            FilePath: filePath,
            Frontmatter: fmDto,
            Body: body);
    }

    public static KbSummaryDto ToSummary(Ref r, string filePath, IReadOnlyDictionary<string, object?> fm) =>
        new(
            Bucket: r.Bucket.ToWire(),
            Slug: r.Slug,
            Ref: r.ToString(),
            Title: AsString(fm, "title") ?? r.Slug,
            Summary: AsString(fm, "summary"),
            FilePath: filePath,
            Modified: FileTime.ModifiedIso(filePath));

    private static bool BucketUsesFeatureList(Bucket b) => b is Bucket.Adr or Bucket.Log;

    private static void SetOrDrop(IDictionary<string, object?> m, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) m.Remove(key);
        else m[key] = value;
    }

    private static void SetListOrDrop(IDictionary<string, object?> m, string key, IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0) m.Remove(key);
        else m[key] = values.Select(s => (object?)s).ToList();
    }

    private static string? AsString(IReadOnlyDictionary<string, object?> m, string k) =>
        m.TryGetValue(k, out var v) ? v?.ToString() : null;

    private static IReadOnlyList<string> AsStringList(IReadOnlyDictionary<string, object?> m, string k)
    {
        if (!m.TryGetValue(k, out var v)) return [];
        if (v is List<object?> list) return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        if (v is string s && !string.IsNullOrEmpty(s)) return [s];
        return [];
    }

    private static IReadOnlyList<string> AsFeatureList(IReadOnlyDictionary<string, object?> m)
    {
        if (!m.TryGetValue("feature", out var v) || v is null) return [];
        if (v is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        var s = v.ToString();
        return string.IsNullOrEmpty(s) ? [] : [s];
    }
}
