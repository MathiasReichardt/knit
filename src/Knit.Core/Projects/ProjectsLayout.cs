using Knit.Core.Discovery;

namespace Knit.Core.Projects;

/// <summary>
/// Layout + discovery helpers for <c>.knit/projects/</c> — the doc-folder
/// side of a project item. One folder per
/// project slug: <c>readme.md</c>, an optional <c>roadmap.md</c> narrative,
/// milestone sub-docs under <c>milestones/</c>, and free-form files
/// (browsable + searchable, inert to the link machinery). The project
/// <em>item</em> points at its folder via the frontmatter
/// <c>project: &lt;slug&gt;</c> field (the <c>design:</c> pattern).
/// </summary>
public static class ProjectsLayout
{
    public const string ProjectsDirName = "projects";
    public const string MilestonesDirName = "milestones";
    public const string ReadmeFileName = "readme.md";
    public const string RoadmapFileName = "roadmap.md";

    public static string ProjectsDir(string repoRoot) =>
        Path.Combine(RepoDiscovery.KnitDir(repoRoot), ProjectsDirName);

    public static string ProjectDir(string repoRoot, string slug) =>
        Path.Combine(ProjectsDir(repoRoot), slug);

    public static string ReadmePath(string repoRoot, string slug) =>
        Path.Combine(ProjectDir(repoRoot, slug), ReadmeFileName);

    public static string RoadmapPath(string repoRoot, string slug) =>
        Path.Combine(ProjectDir(repoRoot, slug), RoadmapFileName);

    public static string MilestonesDir(string repoRoot, string slug) =>
        Path.Combine(ProjectDir(repoRoot, slug), MilestonesDirName);

    public static string MilestonePath(string repoRoot, string projectSlug, string milestoneSlug) =>
        Path.Combine(MilestonesDir(repoRoot, projectSlug), $"{milestoneSlug}.md");

    /// <summary>Project folder slugs, sorted. A folder is a project folder by
    /// existing — a missing <c>readme.md</c> is a verify finding, not a skip.</summary>
    public static IReadOnlyList<string> ProjectSlugs(string repoRoot)
    {
        var dir = ProjectsDir(repoRoot);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir)
            .Select(p => Path.GetFileName(p)!)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>All <c>.md</c> files under a project's <c>milestones/</c>
    /// (top level only — sub-folders there are a verify finding).</summary>
    public static IReadOnlyList<string> MilestoneFiles(string repoRoot, string projectSlug)
    {
        var dir = MilestonesDir(repoRoot, projectSlug);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static string SlugFromMilestonePath(string path) =>
        Path.GetFileNameWithoutExtension(path);
}
