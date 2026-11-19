namespace Knit.WebApi.Contracts;

/// <summary>
/// Response envelope for <c>GET /api/file?path=...</c>. The SPA uses
/// <see cref="Mime"/> to drive syntax-highlight selection and to detect
/// binary files (<c>application/octet-stream</c>, body empty).
/// <see cref="Truncated"/> is set when the file exceeds the size cap;
/// in that case <see cref="Content"/> is empty and the SPA renders a
/// "too large" placeholder.
/// </summary>
public sealed record FileViewDto(
    string Path,
    long SizeBytes,
    string Mime,
    bool Truncated,
    string Content,
    // Absolute path on disk, for "copy path" (matches the other DTOs' FilePath).
    string FilePath,
    // Set when the file was read through a config mount; `Path` is then
    // relative to the mount, not to the repo root.
    string? Mount = null);
