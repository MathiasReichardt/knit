namespace Knit.WebApi.Contracts;

public sealed record SearchHitDto(
    string Kind, string Id, string Title, string Snippet, string Field, int? BodyOffset);

// Clipped is true when more hits matched than the limit returned, so the UI
// can tell the user the result list was truncated.
public sealed record SearchResultDto(bool Clipped, IReadOnlyList<SearchHitDto> Hits);
