namespace Knit.WebApi.Contracts;

/// <summary>
/// Strongly-typed projection of a KB artifact's frontmatter. Carries the
/// union of every bucket's fields; unrelated ones are null/empty. Lets
/// the SPA round-trip frontmatter without dynamic dictionaries.
/// </summary>
public sealed record KbFrontmatterDto(
    string? Id,
    string? Title,
    string? Date,
    string? Status,
    IReadOnlyList<string> Supersedes,
    string? SupersededBy,
    IReadOnlyList<string> Feature,
    IReadOnlyList<string> Origin,
    IReadOnlyList<string> Tags,
    string? Summary,
    string? SourceKind,
    string? Url,
    string? Path,
    // Mount `Path` is relative to; null means the repo root.
    string? Mount = null);

public sealed record KbSummaryDto(
    string Bucket,
    string Slug,
    string Ref,
    string Title,
    string? Summary,
    string FilePath,
    string Modified);

public sealed record KbDto(
    string Bucket,
    string Slug,
    string Ref,
    string Title,
    string FilePath,
    KbFrontmatterDto Frontmatter,
    string Body);

/// <summary>
/// Tree node for the KB browse pane. Mirrors filesystem layout under
/// <c>.knit/kb/</c>. Folders carry children; entry rows carry a ref +
/// title resolved from the file's frontmatter.
/// </summary>
public sealed record KbTreeNodeDto(
    string Name,
    string RelPath,
    bool IsFolder,
    string? Bucket,
    string? Ref,
    string? Title,
    string? FilePath,
    IReadOnlyList<string> Tags,
    IReadOnlyList<KbTreeNodeDto> Children);

public sealed record KbFolderDto(
    string RelPath,
    string FolderPath,
    bool IndexExists,
    string? IndexBody);

public sealed record KbCreateRequestDto(
    string Bucket,
    string? Title,
    string? Slug,
    string? Summary,
    string? Feature,
    string? Date,
    string? SourceKind,
    string? Url,
    string? Path,
    // Config mount `Path` is relative to, for file sources.
    string? Mount = null);

public sealed record KbPatchRequestDto(
    KbFrontmatterDto? Frontmatter,
    string? Body);

public sealed record KbRenameRequestDto(string Ref, string NewSlug);

public sealed record KbRenameResultDto(
    string OldRef,
    string NewRef,
    string OldPath,
    string NewPath,
    IReadOnlyList<string> ItemsRewritten,
    IReadOnlyList<string> KbEntriesAffected,
    IReadOnlyList<string> FilesRelinked);

public sealed record KbDeleteRequestDto(string Ref, bool DeleteFolder, bool DryRun);

/// <summary>
/// Shared shape for the delete preview (<c>dryRun</c>) and the actual
/// delete. <c>BlockingFiles</c> non-empty means a folder-form entry has
/// sibling files: on a real delete that's the 409 "needs deleteFolder"
/// case; on a dry run it's the list the SPA shows behind the
/// "delete folder anyway?" checkbox. Paths are repo-relative.
/// </summary>
public sealed record KbDeleteResultDto(
    string Ref,
    string? DeletedPath,
    bool DeletedFolder,
    bool DryRun,
    bool IsFolderForm,
    IReadOnlyList<string> ItemsAffected,
    IReadOnlyList<string> BlockingFiles,
    IReadOnlyList<string> KbEntriesAffected);

public sealed record KbSupersedeRequestDto(string NewRef, string OldRef);

public sealed record KbSplitRequestDto(string Ref);

public sealed record KbSupersedeResultDto(string NewRef, string OldRef);

public sealed record KbSplitResultDto(
    string OldPath, string NewPath, string Ref,
    IReadOnlyList<string> ItemsRewritten, IReadOnlyList<string> FilesRelinked);

public sealed record KbIndexRebuildResultDto(IReadOnlyList<string> FilesWritten);
