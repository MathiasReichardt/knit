namespace Knit.WebApi.Contracts;

/// <param name="Version">Binary version of the serving host.</param>
/// <param name="FormatVersion">On-disk format version stamped in the repo's config.</param>
/// <param name="SupportedFormatVersion">Highest format this host understands.</param>
public sealed record VersionDto(string Version, int FormatVersion, int SupportedFormatVersion);
