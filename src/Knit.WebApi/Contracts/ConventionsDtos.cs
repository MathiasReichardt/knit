namespace Knit.WebApi.Contracts;

/// <summary>Result of <c>GET /api/conventions</c>: user, then repo — the order <c>knit conventions</c> prints.</summary>
public sealed record ConventionsDto(IReadOnlyList<ConventionsScopeDto> Scopes);

/// <param name="Text">Empty when the file doesn't exist.</param>
/// <param name="Writable">
/// False for the user scope on a host bound beyond loopback: the browser may
/// not be the machine's user, and the file is theirs.
/// </param>
/// <param name="Etag">The <c>If-Match</c> a <c>PUT</c> or <c>DELETE</c> sends.</param>
public sealed record ConventionsScopeDto(string Scope, string Path, bool Exists, string Text, bool Writable, string Etag);

/// <summary>Body of <c>PUT /api/conventions/{scope}</c>; blank text removes the file.</summary>
public sealed record ConventionsWriteDto(string? Text);
