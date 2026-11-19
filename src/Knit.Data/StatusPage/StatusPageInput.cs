using System.Text.RegularExpressions;
using Knit.Core.Designs;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Kb;
using Knit.Core.Projects;
using Knit.Core.Refs;

namespace Knit.Data.StatusPage;

/// <summary>A project doc folder. Paths are absolute and null when the file is missing.</summary>
public sealed record StatusPageFolder(
    string Slug, string Title, string? ReadmePath, string? RoadmapPath, IReadOnlyList<MilestoneDoc> Milestones);

/// <summary>An active design doc; <see cref="Status"/> is the first line of its <c>## Status</c>.</summary>
public sealed record StatusPageDesign(string Slug, string Title, string Path, string? Status);

/// <summary>A decision record; <see cref="Label"/> is its number (<c>0002</c>).</summary>
public sealed record StatusPageAdr(string Label, string Title, DateOnly? Date, string Status, string Path);

/// <summary>
/// Everything the status page is derived from, read once. The renderer is a
/// pure function of this, so a test can pin its output for a fixed day.
/// </summary>
/// <param name="ReportDir">The folder the page is written to; every link is relative to it.</param>
/// <param name="ItemPaths">Item id → absolute file path.</param>
public sealed partial record StatusPageInput(
    string RepoName,
    DateOnly Today,
    string ReportDir,
    IReadOnlyList<Item> Items,
    IReadOnlyDictionary<string, string> ItemPaths,
    Func<string, string> ShortId,
    IReadOnlyList<StatusPageFolder> Folders,
    IReadOnlyList<StatusPageDesign> Designs,
    IReadOnlyList<StatusPageAdr> Adrs)
{
    public static StatusPageInput Load(
        string root, ItemStore store, IFrontmatterSerializer yaml,
        string repoName, DateOnly today, string reportPath, Func<string, string> shortId)
    {
        var items = store.AllItems();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var i in items)
            if (store.PathOf(i.Id) is { } p) paths.TryAdd(i.Id, p);

        return new StatusPageInput(
            repoName, today, Path.GetDirectoryName(Path.GetFullPath(reportPath))!,
            items, paths, shortId, LoadFolders(root, yaml), LoadDesigns(root, yaml), LoadAdrs(root, yaml));
    }

    private static List<StatusPageFolder> LoadFolders(string root, IFrontmatterSerializer yaml)
    {
        var result = new List<StatusPageFolder>();
        foreach (var slug in ProjectsLayout.ProjectSlugs(root))
        {
            var readme = ProjectsLayout.ReadmePath(root, slug);
            var roadmap = ProjectsLayout.RoadmapPath(root, slug);
            var title = File.Exists(readme) && DesignMeta.FirstHeading(MilestoneFile.ReadBody(readme) ?? "") is { } h ? h : slug;
            var milestones = ProjectsLayout.MilestoneFiles(root, slug)
                .Select(f => MilestoneFile.Read(f, slug, yaml).Doc)
                .ToList();
            result.Add(new StatusPageFolder(
                slug, title, File.Exists(readme) ? readme : null, File.Exists(roadmap) ? roadmap : null, milestones));
        }
        return result;
    }

    private static List<StatusPageDesign> LoadDesigns(string root, IFrontmatterSerializer yaml)
    {
        var store = new DesignStore(root, yaml);
        var result = new List<StatusPageDesign>();
        foreach (var path in DesignsLayout.ActiveDesignDocs(root))
        {
            if (store.ReadPath(path) is not { } doc) continue;
            var slug = DesignsLayout.SlugFromPath(path);
            result.Add(new StatusPageDesign(slug, DesignMeta.TitleOf(slug, doc.Frontmatter, doc.Body), path, DesignMeta.StatusOf(doc.Body)));
        }
        return result;
    }

    private static List<StatusPageAdr> LoadAdrs(string root, IFrontmatterSerializer yaml)
    {
        var store = new KbStore(root, yaml);
        var result = new List<StatusPageAdr>();
        foreach (var (r, path) in store.ListBucket(Bucket.Adr))
        {
            if (store.ReadPath(path) is not { } doc) continue;
            var adr = KbFrontmatterMap.ReadAdr(doc.Frontmatter);
            var label = adr.Id.Length > 0 ? adr.Id : LeadingNumber().Match(r.Slug).Value;
            if (label.Length == 0) label = r.Slug;
            // The ADR template titles itself "0002. Choice"; the page names the number separately.
            var title = NumberPrefix().Replace(adr.Title, "");
            result.Add(new StatusPageAdr(label, title.Length == 0 ? r.Slug : title, adr.Date, adr.Status, path));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        return result;
    }

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^\d+\.\s*")]
    private static partial Regex NumberPrefix();
}
