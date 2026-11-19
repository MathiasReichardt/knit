using Knit.Core.DocFolders;
using Knit.Core.Kb;
using Knit.Core.Result;

namespace Knit.Core.Designs;

/// <summary>
/// Moves an active design doc into
/// <c>completed/&lt;slug&gt;/&lt;cycle&gt;-design.md</c>. Surfaced as
/// <c>knit design archive</c>.
/// </summary>
public static class DesignArchive
{
    public sealed record ArchiveResult(string Path, IReadOnlyList<string> FilesRelinked);

    /// <summary>Inbound prose links and the doc's own relative links are
    /// repointed; <c>design:</c> refs resolve to the archive unchanged.</summary>
    public static OperationResult<ArchiveResult> MoveToCompleted(
        string repoRoot, string slug, string cycle, ProseRewriter proseRewriter)
    {
        var source = DesignsLayout.DesignDocPath(repoRoot, slug);
        if (!File.Exists(source))
            return OperationResult<ArchiveResult>.Error(KnitError.NotFound($"design doc not found at {source}"));
        var dir = Path.Combine(DesignsLayout.CompletedDir(repoRoot), slug);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{cycle}-design.md");
        if (File.Exists(dest))
            return OperationResult<ArchiveResult>.Error(KnitError.Conflict($"archive already exists at {dest}"));
        File.Move(source, dest);
        var relinked = proseRewriter.RewriteProse([(source, dest), .. DocAssets.Carry(source, dest)], []);
        return OperationResult<ArchiveResult>.Ok(new ArchiveResult(dest, relinked));
    }
}
