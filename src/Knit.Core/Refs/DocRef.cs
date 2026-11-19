using Knit.Core.Discovery;
using Knit.Core.Paths;

namespace Knit.Core.Refs;

/// <summary>
/// A doc address with an optional fragment: <c>&lt;ref&gt;[#&lt;fragment&gt;]</c>, or, for a
/// doc without a ref (a milestone, a non-readme project doc), its
/// <c>.knit/…/x.md</c> path relative to the knit root. The fragment is a heading
/// anchor (<see cref="DocSections"/>) or a diagram pointer
/// (<c>mermaid-&lt;n&gt;…</c>, or <c>&lt;file&gt;.svg[/&lt;kind&gt;/&lt;id&gt;]</c> for an
/// SVG next to the doc; never a section — an anchor holds no <c>.</c>).
/// </summary>
public sealed record DocRef(Ref? Ref, string? KnitPath, string? Fragment)
{
    private const string KnitPrefix = RepoDiscovery.KnitDirName + "/";

    public string Doc => Ref?.ToString() ?? KnitPath!;

    /// <summary>The fragment when it names a section, null for none or a pointer.</summary>
    public string? Section => Fragment is null || IsPointer ? null : Fragment;

    public bool IsPointer => SvgFile is not null || (Fragment is not null
        && Fragment.StartsWith("mermaid-", StringComparison.Ordinal)
        && Fragment.Length > 8 && char.IsAsciiDigit(Fragment[8]));

    /// <summary>The SVG file name an SVG pointer names, or null.</summary>
    public string? SvgFile =>
        Fragment?.Split('/')[0] is { } f && f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && f.Length > 4 ? f : null;

    public override string ToString() => Fragment is null ? Doc : $"{Doc}#{Fragment}";

    public DocRef WithRef(Ref r) => this with { Ref = r, KnitPath = null };

    public static bool TryParse(string? text, out DocRef result)
    {
        result = new DocRef(null, null, null);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        var hash = s.IndexOf('#');
        var doc = hash < 0 ? s : s[..hash];
        var fragment = hash < 0 ? null : s[(hash + 1)..];
        if (fragment is { Length: 0 }) return false;

        if (doc.StartsWith(KnitPrefix, StringComparison.Ordinal))
        {
            if (!doc.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || doc.Contains('\\')
                || doc.Split('/').Any(seg => seg is "" or "." or ".."))
                return false;
            result = new DocRef(null, doc, fragment);
            return true;
        }

        if (!Refs.Ref.TryParse(doc, out var r)) return false;
        result = new DocRef(r, null, fragment);
        return true;
    }

    /// <summary>The doc's file and whether it exists. A path outside <c>.knit/</c> never exists.</summary>
    public (string Path, bool Exists) Resolve(string root)
    {
        if (Ref is not null)
        {
            var resolved = RefResolver.Resolve(Ref, root).GetValueOrThrow();
            return (resolved.Path, resolved.Exists);
        }
        var knitDir = System.IO.Path.GetFullPath(RepoDiscovery.KnitDir(root));
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, KnitPath!));
        return (full, PathContainment.IsContained(knitDir, full) && File.Exists(full));
    }
}

/// <summary>Why a <see cref="DocRef"/>'s section doesn't hold, or null when it does.</summary>
public static class SectionCheck
{
    public const string MissingSection = "missing_section";
    public const string SuffixedAnchor = "suffixed_anchor";

    public static string? Check(string body, string section)
    {
        var h = DocSections.Outline(body).FirstOrDefault(x => x.Anchor == section);
        if (h is null) return MissingSection;
        return h.Suffixed ? SuffixedAnchor : null;
    }
}
