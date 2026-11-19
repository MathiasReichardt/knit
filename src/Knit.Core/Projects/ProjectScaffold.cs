using System.Globalization;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Ids;
using Knit.Core.Result;

namespace Knit.Core.Projects;

/// <summary>
/// Scaffolds the project doc folder + milestone sub-docs. Mirrors
/// <see cref="Knit.Core.Designs.DesignStore.New"/>: refuses to overwrite
/// unless forced, creates parent directories as needed.
/// </summary>
public static class ProjectScaffold
{
    public static OperationResult<string> NewProject(
        string repoRoot, string slug, string title, bool force, string? goal = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return OperationResult<string>.Error(KnitError.BadArgument("slug must not be empty"));

        var path = ProjectsLayout.ReadmePath(repoRoot, slug);
        if (File.Exists(path) && !force)
            return OperationResult<string>.Error(KnitError.Conflict(
                $"project doc folder already exists at {path}; pass --force to overwrite"));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var goalLine = string.IsNullOrWhiteSpace(goal)
            ? "What this project is for, and what \"done\" looks like."
            : goal.Trim();
        AtomicFile.WriteAllText(path,
            $"# {title}\n\n" +
            $"## Goal\n{goalLine}\n\n" +
            "## Notes\nContext that belongs to the project but not to the roadmap or any one item.\n");
        return OperationResult<string>.Ok(path);
    }

    public static OperationResult<string> NewRoadmap(string repoRoot, string slug, string title, bool force)
    {
        if (!Directory.Exists(ProjectsLayout.ProjectDir(repoRoot, slug)))
            return OperationResult<string>.Error(KnitError.NotFound(
                $"no project doc folder at {ProjectsLayout.ProjectDir(repoRoot, slug)}"));

        var path = ProjectsLayout.RoadmapPath(repoRoot, slug);
        if (File.Exists(path) && !force)
            return OperationResult<string>.Error(KnitError.Conflict(
                $"roadmap already exists at {path}; pass --force to overwrite"));

        AtomicFile.WriteAllText(path,
            $"# {title} — roadmap\n\n" +
            "How the milestones fit together — sequencing rationale, cross-cutting themes, risks; per-milestone detail lives in `milestones/`.\n");
        return OperationResult<string>.Ok(path);
    }

    public static OperationResult<string> NewMilestone(
        string repoRoot, string projectSlug, string slug, string title, DateOnly? target, bool force,
        IFrontmatterSerializer yaml)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return OperationResult<string>.Error(KnitError.BadArgument("slug must not be empty"));
        if (!Directory.Exists(ProjectsLayout.ProjectDir(repoRoot, projectSlug)))
            return OperationResult<string>.Error(KnitError.NotFound(
                $"no project doc folder at {ProjectsLayout.ProjectDir(repoRoot, projectSlug)}; create it with `knit project new {projectSlug}`"));
        if (!File.Exists(ProjectsLayout.RoadmapPath(repoRoot, projectSlug)))
            return OperationResult<string>.Error(KnitError.Conflict(
                $"project '{projectSlug}' has no roadmap; add one with `knit project roadmap {projectSlug}` before creating milestones"));

        var path = ProjectsLayout.MilestonePath(repoRoot, projectSlug, slug);
        if (File.Exists(path) && !force)
            return OperationResult<string>.Error(KnitError.Conflict(
                $"milestone already exists at {path}; pass --force to overwrite"));

        var rank = Rank.Between(MaxMilestoneRank(repoRoot, projectSlug, yaml), null);

        var sb = new System.Text.StringBuilder();
        sb.Append("---\n");
        sb.Append($"title: {title}\n");
        if (target is { } t) sb.Append($"target: {t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\n");
        sb.Append("state: planned\n");
        sb.Append($"rank: {rank}\n");
        sb.Append($"created: {DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\n");
        sb.Append("---\n\n");
        sb.Append($"# {title}\n\nWhat this milestone delivers, and what it deliberately leaves out.\n");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, sb.ToString());
        return OperationResult<string>.Ok(path);
    }

    // Highest existing milestone rank in the project (null when none is ranked),
    // so a new milestone appends to the end of the manual order.
    private static string? MaxMilestoneRank(string repoRoot, string projectSlug, IFrontmatterSerializer yaml) =>
        ProjectsLayout.MilestoneFiles(repoRoot, projectSlug)
            .Select(f => MilestoneFile.Read(f, projectSlug, yaml).Doc.Rank)
            .Where(Rank.IsValid)
            .OrderBy(r => r, StringComparer.Ordinal)
            .LastOrDefault();
}
