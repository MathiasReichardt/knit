namespace Knit.WebApi.Contracts;

/// <summary>
/// Result of <c>GET /api/config</c> — the same rows <c>knit config list</c>
/// prints, from the same <c>ConfigReport</c>, so there is one definition of
/// "effective value" and one of "source". Mounts are not repeated
/// here: they have their own read-only endpoint (<c>/api/mounts</c>) and the
/// settings view renders those rows through it.
/// </summary>
/// <param name="Unreadable">
/// Config files that exist but do not parse. Non-empty means every value
/// above is a fallback, not the file's content — the view must say so rather
/// than show a confident table.
/// </param>
/// <param name="Layers">
/// Every layer with the file it lives in, lowest precedence first. The
/// source name alone leaves "so which file do I edit?" unanswered, and the
/// global one is not even inside the repo.
/// </param>
/// <param name="IgnoredGlobal">
/// What the user-level config declares that applies to no repo — shown
/// beside the table, since these values have no effective value to be a row of.
/// </param>
public sealed record ConfigDto(
    IReadOnlyList<ConfigKeyDto> Keys,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<ConfigLayerDto> Layers,
    IReadOnlyList<IgnoredGlobalKeyDto> IgnoredGlobal);

public sealed record IgnoredGlobalKeyDto(string Key, string Reason);

/// <param name="Source"><c>global</c>, <c>config.json</c>, <c>config.local.json</c> or <c>default</c>.</param>
/// <param name="Known">False for a key knit does not model — listed, never dropped.</param>
public sealed record ConfigKeyDto(
    string Key,
    string Value,
    string Source,
    string Default,
    bool IsSet,
    bool Known,
    bool Settable,
    string Description);

public sealed record ConfigLayerDto(string Label, string Path);
