using System.Globalization;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Paths;
using Knit.Core.Result;

namespace Knit.Core.Projects;

/// <summary>
/// The fields a <see cref="MilestoneEdit.Apply"/> may change. A null field
/// is left untouched; <see cref="ClearTarget"/> removes the target date
/// (distinct from "leave it") since a null <see cref="Target"/> can't tell
/// "clear" from "don't touch". <see cref="Body"/> replaces the narrative body.
/// </summary>
public sealed record MilestoneEdits(
    string? Title = null,
    MilestoneState? State = null,
    DateOnly? Target = null,
    bool ClearTarget = false,
    string? Body = null);

/// <summary>
/// In-place milestone mutations that need no knowledge of items: frontmatter
/// edits (<c>title</c> / <c>state</c> / <c>target</c>), body edits, and the
/// pure filesystem move / delete of the flat <c>milestones/&lt;slug&gt;.md</c>
/// file. Backlink re-pointing (rename) and dangling/scrub handling (delete)
/// live in <c>Knit.Data.MilestoneMutator</c>, which orchestrates these plus
/// the item store — mirroring how <c>ProjectRemover</c> sits over
/// <c>ProjectScaffold</c>.
/// </summary>
public static class MilestoneEdit
{
    /// <summary>
    /// Apply frontmatter and/or body edits in place. Preserves <c>created</c>
    /// and any unknown keys. Two-way title↔H1 sync: an explicit
    /// <see cref="MilestoneEdits.Title"/> wins and is mirrored into the body's
    /// leading H1; otherwise a body edit's leading H1 becomes the title.
    /// </summary>
    public static OperationResult<MilestoneDoc> Apply(
        string repoRoot, string project, string slug, MilestoneEdits edits, IFrontmatterSerializer yaml)
    {
        var path = ProjectsLayout.MilestonePath(repoRoot, project, slug);
        var raw = MilestoneFile.ReadRaw(path, yaml);
        if (raw is null)
            return OperationResult<MilestoneDoc>.Error(KnitError.NotFound($"no milestone '{project}/{slug}' at {path}"));

        var fm = new Dictionary<string, object?>(raw.Value.Frontmatter, StringComparer.Ordinal);
        var body = edits.Body ?? raw.Value.Body;

        if (edits.Title is { } title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return OperationResult<MilestoneDoc>.Error(KnitError.BadArgument("title cannot be empty"));
            fm["title"] = title;
            body = LeadingH1.SetOrInsert(body, title);
        }
        else if (edits.Body is not null && LeadingH1.Extract(body) is { } h1)
        {
            fm["title"] = h1;
        }

        if (edits.State is { } state) fm["state"] = state.ToWire();

        if (edits.ClearTarget) fm.Remove("target");
        else if (edits.Target is { } t) fm["target"] = t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        MilestoneFile.WriteRaw(path, fm, body, yaml);
        var (doc, _) = MilestoneFile.Read(path, project, yaml);
        return OperationResult<MilestoneDoc>.Ok(doc);
    }

    /// <summary>
    /// Rewrite milestone ranks to match <paramref name="order"/> (the desired
    /// slug order, top to bottom). Assigns a fresh sequential fractional rank
    /// to each and writes only the files whose rank actually changes, so a
    /// reorder touches the minimum. <paramref name="order"/> must be a
    /// permutation of the project's milestones — a missing / extra / duplicate
    /// slug is rejected. Backfills ranks for milestones that never had one.
    /// </summary>
    public static OperationResult<IReadOnlyList<string>> Reorder(
        string repoRoot, string project, IReadOnlyList<string> order, IFrontmatterSerializer yaml)
    {
        var existing = ProjectsLayout.MilestoneFiles(repoRoot, project)
            .Select(ProjectsLayout.SlugFromMilestonePath)
            .ToList();
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);
        var orderSet = new HashSet<string>(order, StringComparer.Ordinal);
        if (order.Count != orderSet.Count)
            return OperationResult<IReadOnlyList<string>>.Error(KnitError.BadArgument("order contains duplicate slugs"));
        if (!existingSet.SetEquals(orderSet))
            return OperationResult<IReadOnlyList<string>>.Error(KnitError.BadArgument(
                $"order must be a permutation of the {existing.Count} milestone(s) in '{project}'"));

        // Evenly spaced rather than chained Between(prev, null): chaining
        // yields n, t, w, y, z, zn, … which saturates upward and leaves no
        // room before the first row.
        var ranks = Rank.Spread(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            var slug = order[i];
            var next = ranks[i];
            var path = ProjectsLayout.MilestonePath(repoRoot, project, slug);
            var raw = MilestoneFile.ReadRaw(path, yaml);
            if (raw is null) continue;
            var current = raw.Value.Frontmatter.TryGetValue("rank", out var rv) ? rv?.ToString() : null;
            if (string.Equals(current, next, StringComparison.Ordinal)) continue;
            var fm = new Dictionary<string, object?>(raw.Value.Frontmatter, StringComparer.Ordinal) { ["rank"] = next };
            MilestoneFile.WriteRaw(path, fm, raw.Value.Body, yaml);
        }

        return OperationResult<IReadOnlyList<string>>.Ok(order);
    }

    /// <summary>
    /// Move <c>milestones/&lt;oldSlug&gt;.md</c> → <c>&lt;newSlug&gt;.md</c>
    /// (slugified) inside one project. Pure filesystem — does <b>not</b>
    /// re-point backlinks; the Data layer does that.
    /// </summary>
    public static OperationResult<(string OldPath, string NewPath)> Rename(
        string repoRoot, string project, string oldSlug, string newSlug) =>
        Move(repoRoot, project, oldSlug, project, newSlug);

    /// <summary>
    /// Move a milestone sub-doc to another project, optionally under a new
    /// slug. Pure filesystem — does <b>not</b> re-point backlinks; the Data
    /// layer does that. Refuses to overwrite an existing target, to land in a
    /// project that doesn't exist, or to touch anything outside
    /// <c>.knit/projects/</c> (or through a symlink).
    /// </summary>
    public static OperationResult<(string OldPath, string NewPath)> Move(
        string repoRoot, string fromProject, string oldSlug, string toProject, string newSlug)
    {
        if (string.IsNullOrWhiteSpace(newSlug))
            return OperationResult<(string, string)>.Error(KnitError.BadArgument("new slug cannot be empty"));
        var norm = Slug.From(newSlug);
        if (string.IsNullOrEmpty(norm))
            return OperationResult<(string, string)>.Error(KnitError.BadArgument($"invalid slug '{newSlug}'"));

        var oldPath = ProjectsLayout.MilestonePath(repoRoot, fromProject, oldSlug);
        if (!File.Exists(oldPath))
            return OperationResult<(string, string)>.Error(KnitError.NotFound($"no milestone '{fromProject}/{oldSlug}' at {oldPath}"));
        if (!Directory.Exists(ProjectsLayout.ProjectDir(repoRoot, toProject)))
            return OperationResult<(string, string)>.Error(KnitError.NotFound($"no project '{toProject}' under .knit/projects/"));

        var newPath = ProjectsLayout.MilestonePath(repoRoot, toProject, norm);
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return OperationResult<(string, string)>.Ok((oldPath, newPath));
        if (File.Exists(newPath))
            return OperationResult<(string, string)>.Error(KnitError.Conflict($"milestone '{toProject}/{norm}' already exists at {newPath}"));
        if (!Guarded(repoRoot, oldPath) || !Guarded(repoRoot, newPath))
            return OperationResult<(string, string)>.Error(KnitError.BadArgument("milestone path escapes .knit/projects/"));

        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
        return OperationResult<(string, string)>.Ok((oldPath, newPath));
    }

    /// <summary>
    /// Delete <c>milestones/&lt;slug&gt;.md</c>. Pure filesystem — leaves
    /// backlinks alone (the Data layer decides dangle-vs-scrub). Guarded like
    /// <see cref="Rename"/>.
    /// </summary>
    public static OperationResult<string> Delete(string repoRoot, string project, string slug)
    {
        var path = ProjectsLayout.MilestonePath(repoRoot, project, slug);
        if (!File.Exists(path))
            return OperationResult<string>.Error(KnitError.NotFound($"no milestone '{project}/{slug}' at {path}"));
        if (!Guarded(repoRoot, path))
            return OperationResult<string>.Error(KnitError.BadArgument("milestone path escapes .knit/projects/"));

        File.Delete(path);
        return OperationResult<string>.Ok(path);
    }

    private static bool Guarded(string repoRoot, string path)
    {
        var projectsDir = Path.GetFullPath(ProjectsLayout.ProjectsDir(repoRoot));
        var full = Path.GetFullPath(path);
        return PathContainment.IsContained(projectsDir, full) && !PathContainment.HasSymlinkBelow(projectsDir, full);
    }
}
