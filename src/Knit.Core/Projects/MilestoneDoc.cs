using System.Globalization;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Ids;

namespace Knit.Core.Projects;

/// <summary>
/// Milestone lifecycle. A passed target with open work is derived from
/// <c>target &lt; today</c>, not a state.
/// </summary>
public enum MilestoneState { Planned, Reached, Dropped }

public static class MilestoneStates
{
    public static string ToWire(this MilestoneState s) => s switch
    {
        MilestoneState.Planned => "planned",
        MilestoneState.Reached => "reached",
        MilestoneState.Dropped => "dropped",
        _ => "planned",
    };

    public static bool TryParse(string? s, out MilestoneState state)
    {
        switch ((s ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "planned": state = MilestoneState.Planned; return true;
            case "reached": state = MilestoneState.Reached; return true;
            case "dropped": state = MilestoneState.Dropped; return true;
            default: state = MilestoneState.Planned; return false;
        }
    }
}

/// <summary>
/// A parsed milestone sub-doc (<c>.knit/projects/&lt;project&gt;/milestones/&lt;slug&gt;.md</c>).
/// Doc type is implicit in location — no <c>kind:</c> field. Identity is
/// <c>&lt;project&gt;/&lt;slug&gt;</c>; bare-slug addressing resolves within
/// an item's own project.
/// </summary>
public sealed record MilestoneDoc(
    string ProjectSlug,
    string Slug,
    string Title,
    DateOnly? Target,
    MilestoneState State,
    DateTimeOffset? Created,
    // Fractional rank (a-z) for manual ordering in the roadmap spine.
    // Null when the milestone predates ranking; such milestones sort after
    // ranked ones (by slug) and gain a rank on the first reorder.
    string? Rank,
    string Path)
{
    public string QualifiedSlug => $"{ProjectSlug}/{Slug}";
}

/// <summary>
/// Reads milestone sub-docs. Lenient for display (missing title falls back
/// to the slug, malformed fields degrade to defaults) but every degradation
/// is reported in <c>Problems</c> so <c>knit verify</c> can surface it —
/// malformed milestone files are findings, never skipped.
/// </summary>
public static class MilestoneFile
{
    public static (MilestoneDoc Doc, IReadOnlyList<string> Problems) Read(
        string path, string projectSlug, IFrontmatterSerializer yaml)
    {
        var slug = ProjectsLayout.SlugFromMilestonePath(path);
        var problems = new List<string>();

        string content;
        try { content = AtomicFile.ReadAllText(path); }
        catch (Exception ex)
        {
            problems.Add($"unreadable: {ex.Message}");
            return (new MilestoneDoc(projectSlug, slug, slug, null, MilestoneState.Planned, null, null, path), problems);
        }

        var (fm, _) = SplitFrontmatter(content);
        if (fm.Length == 0)
        {
            problems.Add("no YAML frontmatter block (missing or unterminated ---)");
            return (new MilestoneDoc(projectSlug, slug, slug, null, MilestoneState.Planned, null, null, path), problems);
        }

        var fields = yaml.Parse(fm);
        var title = AsString(fields, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            problems.Add("missing 'title' in frontmatter");
            title = slug;
        }

        DateOnly? target = null;
        var targetRaw = AsString(fields, "target");
        if (!string.IsNullOrEmpty(targetRaw))
        {
            if (DueDates.TryParse(targetRaw, out var t)) target = t;
            else problems.Add($"invalid target '{targetRaw}' — expected YYYY-MM-DD");
        }

        var state = MilestoneState.Planned;
        var stateRaw = AsString(fields, "state");
        if (!string.IsNullOrEmpty(stateRaw) && !MilestoneStates.TryParse(stateRaw, out state))
            problems.Add($"unknown state '{stateRaw}' — expected planned|reached|dropped");

        DateTimeOffset? created = null;
        var createdRaw = AsString(fields, "created");
        if (!string.IsNullOrEmpty(createdRaw) &&
            DateTimeOffset.TryParse(createdRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var c))
        {
            created = c;
        }

        var rank = AsString(fields, "rank");
        if (!string.IsNullOrEmpty(rank) && !Rank.IsValid(rank))
        {
            problems.Add($"invalid rank '{rank}' — expected a-z fractional rank");
            rank = null;
        }

        return (new MilestoneDoc(projectSlug, slug, title!, target, state, created, rank, path), problems);
    }

    public static string? ReadBody(string path)
    {
        if (!File.Exists(path)) return null;
        var (_, body) = SplitFrontmatter(AtomicFile.ReadAllText(path));
        return body;
    }

    /// <summary>
    /// Raw (frontmatter map, body) pair for in-place edits — preserves
    /// unknown keys (e.g. <c>created</c>) that the typed <see cref="Read"/>
    /// view drops. Null when the file is missing. Delegates to the shared
    /// <see cref="DocFolders.DocBody"/>.
    /// </summary>
    public static (Dictionary<string, object?> Frontmatter, string Body)? ReadRaw(
        string path, IFrontmatterSerializer yaml) => DocFolders.DocBody.ReadRaw(path, yaml);

    /// <summary>
    /// Write a milestone file from a frontmatter map + body. A milestone always
    /// has frontmatter (title at minimum), so this is the non-empty branch of
    /// the shared <see cref="DocFolders.DocBody"/> writer.
    /// </summary>
    public static void WriteRaw(
        string path, IReadOnlyDictionary<string, object?> frontmatter, string body, IFrontmatterSerializer yaml) =>
        DocFolders.DocBody.WriteRaw(path, frontmatter, body, yaml);

    private static string? AsString(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static (string fm, string body) SplitFrontmatter(string content) =>
        DocFolders.DocBody.Split(content);
}
