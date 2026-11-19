using Knit.Core.DocFolders;
using Knit.Core.IO;
using Knit.Core.Kb;
using Knit.Core.Paths;
using Knit.Core.Result;

namespace Knit.Core.Projects;

/// <summary>
/// Free-form doc CRUD under a project doc folder
/// (<c>.knit/projects/&lt;slug&gt;/</c>) — the "entries + folder entries"
/// surface a project shares with KB. Reuses the shared doc-folder
/// core for path/form (<see cref="DocFolder"/>), delete
/// (<see cref="DocFolderDelete"/>) and move (<see cref="DocFolderMove"/>), and
/// <see cref="DocBody"/> for frontmatter-preserving body IO.
///
/// The recognized names <c>readme.md</c> / <c>roadmap.md</c> and the
/// <c>milestones/</c> sub-tree are <em>reserved</em> — they have their own
/// scaffold/lifecycle paths (<see cref="ProjectScaffold"/>) and are rejected
/// here so a free-form op can't collide with or clobber them. Every resolved
/// path is containment-guarded to the project folder.
/// </summary>
public static class ProjectDoc
{
    public sealed record CreateResult(string Path, bool IsFolderForm);
    public sealed record DeleteResult(string TargetPath, bool IsFolderForm, bool Deleted, IReadOnlyList<string> BlockingFiles);
    public sealed record MoveResult(string OldPath, string NewPath, bool WasFolderForm, IReadOnlyList<string> FilesRelinked);

    /// <summary>Reserved (has a dedicated command): the recognized readme /
    /// roadmap entry files, and anything under <c>milestones/</c>.</summary>
    public static bool IsReserved(string relPath)
    {
        var rel = relPath.Replace('\\', '/').Trim('/');
        return rel.Equals(ProjectsLayout.ReadmeFileName, StringComparison.OrdinalIgnoreCase)
            || rel.Equals(ProjectsLayout.RoadmapFileName, StringComparison.OrdinalIgnoreCase)
            || rel.Equals(ProjectsLayout.MilestonesDirName, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(ProjectsLayout.MilestonesDirName + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Safe absolute path for a project-relative doc path — normalizes
    /// separators, rejects empty / absolute / <c>..</c> / dot segments, and
    /// guards containment (lexical + no symlink) to the project folder. No
    /// existence check.</summary>
    public static OperationResult<string> Resolve(string root, string projectSlug, string relPath)
    {
        var (rel, err) = NormalizeRel(relPath);
        if (err is not null) return OperationResult<string>.Error(KnitError.BadArgument(err));
        var projectDir = Path.GetFullPath(ProjectsLayout.ProjectDir(root, projectSlug));
        var full = Path.GetFullPath(Path.Combine(projectDir, rel));
        if (!PathContainment.IsContained(projectDir, full) || PathContainment.HasSymlinkBelow(projectDir, full))
            return OperationResult<string>.Error(KnitError.BadArgument("doc path escapes the project folder"));
        return OperationResult<string>.Ok(full);
    }

    /// <summary>Create a free-form doc, flat (<c>&lt;slug&gt;.md</c>) or folder
    /// form (<c>&lt;slug&gt;/readme.md</c>). Errors if the project folder is
    /// missing, the slug is reserved, or either on-disk form already
    /// exists.</summary>
    public static OperationResult<CreateResult> CreateFreeForm(
        string root, string projectSlug, string docSlug, string? title, bool asFolder)
    {
        var projectDir = ProjectsLayout.ProjectDir(root, projectSlug);
        if (!Directory.Exists(projectDir))
            return OperationResult<CreateResult>.Error(KnitError.NotFound(
                $"no project doc folder at {projectDir}; create it with `knit project new {projectSlug}`"));

        var (rel, err) = NormalizeRel(docSlug);
        if (err is not null) return OperationResult<CreateResult>.Error(KnitError.BadArgument(err));
        var slug = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? rel[..^3] : rel;
        if (slug.Length == 0)
            return OperationResult<CreateResult>.Error(KnitError.BadArgument("doc slug must not be empty"));
        if (IsReserved(slug) || IsReserved(slug + ".md"))
            return OperationResult<CreateResult>.Error(KnitError.BadArgument(
                $"'{slug}' is reserved — readme/roadmap and milestones/ have their own commands"));

        var target = asFolder ? DocFolder.FolderReadmePath(projectDir, slug) : DocFolder.FlatPath(projectDir, slug);
        var full = Path.GetFullPath(target);
        if (!PathContainment.IsContained(Path.GetFullPath(projectDir), full))
            return OperationResult<CreateResult>.Error(KnitError.BadArgument("doc path escapes the project folder"));
        if (DocFolder.Resolve(projectDir, slug) is not null || File.Exists(full))
            return OperationResult<CreateResult>.Error(KnitError.Conflict(
                $"doc '{slug}' already exists in project '{projectSlug}'"));

        var docTitle = string.IsNullOrWhiteSpace(title) ? LastSegment(slug) : title!;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, $"# {docTitle}\n");
        return OperationResult<CreateResult>.Ok(new CreateResult(full, asFolder));
    }

    /// <summary>Delete a free-form doc (flat file or folder-form entry) via the
    /// shared containment-guarded partition. A folder-form entry with sibling
    /// files needs <paramref name="deleteFolder"/>; otherwise the blockers are
    /// returned and nothing is removed. Free-form docs are inert to the link
    /// machinery, so there are no inbound refs to strip.</summary>
    public static OperationResult<DeleteResult> Delete(
        string root, string projectSlug, string relPath, bool deleteFolder)
    {
        var resolved = Resolve(root, projectSlug, relPath);
        if (resolved.IsError) return OperationResult<DeleteResult>.Error(resolved.GetErrorOrDefault()!);
        var full = resolved.GetValueOrThrow();
        if (!File.Exists(full))
            return OperationResult<DeleteResult>.Error(KnitError.NotFound($"no doc at {relPath}"));

        var projectDir = ProjectsLayout.ProjectDir(root, projectSlug);
        var plan = DocFolderDelete.Plan(projectDir, full);
        if (plan.IsError) return OperationResult<DeleteResult>.Error(plan.GetErrorOrDefault()!);
        var p = plan.GetValueOrThrow();

        var exec = DocFolderDelete.Execute(projectDir, p, deleteFolder);
        if (exec.IsError) return OperationResult<DeleteResult>.Error(exec.GetErrorOrDefault()!);
        var deleted = exec.GetValueOrThrow();
        return OperationResult<DeleteResult>.Ok(new DeleteResult(
            p.TargetPath, p.IsFolderForm, deleted, deleted ? [] : p.Blockers));
    }

    /// <summary>Move / rename a free-form doc (flat file or whole folder). Both
    /// paths are containment-guarded; the destination must not already exist.
    /// Free-form docs are referenced only by prose links, which are repointed.</summary>
    public static OperationResult<MoveResult> Move(
        string root, string projectSlug, string oldRelPath, string newRelPath, ProseRewriter proseRewriter)
    {
        var oldR = Resolve(root, projectSlug, oldRelPath);
        if (oldR.IsError) return OperationResult<MoveResult>.Error(oldR.GetErrorOrDefault()!);
        var newR = Resolve(root, projectSlug, newRelPath);
        if (newR.IsError) return OperationResult<MoveResult>.Error(newR.GetErrorOrDefault()!);
        var oldFull = oldR.GetValueOrThrow();
        var newFull = newR.GetValueOrThrow();

        if (!File.Exists(oldFull))
            return OperationResult<MoveResult>.Error(KnitError.NotFound($"no doc at {oldRelPath}"));
        if (IsReserved(newRelPath))
            return OperationResult<MoveResult>.Error(KnitError.BadArgument(
                $"'{newRelPath}' is reserved — readme/roadmap and milestones/ have their own commands"));

        // Folder-form source moves the whole folder; the destination must name a
        // readme.md so DocFolderMove routes it as a folder move.
        var destForm = DocFolder.IsFolderForm(oldFull) ? DocFolder.FolderReadmePath(
            ProjectsLayout.ProjectDir(root, projectSlug), StripMd(newRelPath)) : newFull;
        destForm = Path.GetFullPath(destForm);

        var destFolder = DocFolder.IsFolderForm(oldFull) ? Path.GetDirectoryName(destForm)! : destForm;
        if (File.Exists(destForm) || (DocFolder.IsFolderForm(oldFull) && Directory.Exists(destFolder)))
            return OperationResult<MoveResult>.Error(KnitError.Conflict($"{newRelPath} already exists"));

        var result = DocFolderMove.Move(oldFull, destForm);
        var proseMove = result.WasFolderForm
            ? (Path.GetDirectoryName(result.OldPath)!, Path.GetDirectoryName(result.NewPath)!)
            : (result.OldPath, result.NewPath);
        var relinked = proseRewriter.RewriteProse([proseMove, .. result.Assets], []);
        return OperationResult<MoveResult>.Ok(new MoveResult(result.OldPath, result.NewPath, result.WasFolderForm, relinked));
    }

    private static string StripMd(string rel)
    {
        var r = rel.Replace('\\', '/').Trim('/');
        return r.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? r[..^3] : r;
    }

    private static string LastSegment(string slug)
    {
        var parts = slug.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? slug : parts[^1];
    }

    private static (string Rel, string? Error) NormalizeRel(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return ("", "doc path must not be empty");
        var rel = relPath.Replace('\\', '/').Trim('/');
        if (rel.Length == 0) return ("", "doc path must not be empty");
        if (Path.IsPathRooted(relPath)) return ("", "doc path must be relative");
        foreach (var seg in rel.Split('/'))
        {
            if (seg.Length == 0 || seg == "." || seg == "..")
                return ("", $"invalid doc path segment '{seg}'");
        }
        return (rel, null);
    }
}
