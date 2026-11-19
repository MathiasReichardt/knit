using Knit.Core.Discovery;

namespace Knit.Core.Designs;

/// <summary>
/// Layout + discovery helpers for <c>.knit/designs/</c>. Active design
/// docs live at the root; the <c>completed/&lt;slug&gt;/</c> subtree
/// archives finished cycles. Designs are markdown only — the plan side
/// of a cycle is a Knit item subtree (items reference the design via
/// the <c>design:</c> field).
/// </summary>
public static class DesignsLayout
{
    public const string DesignsDirName = "designs";
    public const string CompletedDirName = "completed";

    public static string DesignsDir(string repoRoot) =>
        Path.Combine(RepoDiscovery.KnitDir(repoRoot), DesignsDirName);

    public static string CompletedDir(string repoRoot) =>
        Path.Combine(DesignsDir(repoRoot), CompletedDirName);

    public static IReadOnlyList<string> ActiveDesignDocs(string repoRoot)
    {
        var dir = DesignsDir(repoRoot);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*-design.md", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> CompletedDesignDocs(string repoRoot, string? slug = null)
    {
        var dir = CompletedDir(repoRoot);
        if (!Directory.Exists(dir)) return [];
        var searchDir = string.IsNullOrEmpty(slug) ? dir : Path.Combine(dir, slug);
        if (!Directory.Exists(searchDir)) return [];
        return Directory.EnumerateFiles(searchDir, "*-design.md", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static string DesignDocPath(string repoRoot, string slug) =>
        Path.Combine(DesignsDir(repoRoot), $"{slug}-design.md");

    public static string SlugFromPath(string path)
    {
        var name = Path.GetFileName(path);
        const string suffix = "-design.md";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(name);
    }
}
