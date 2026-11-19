namespace Knit.WebApi.Contracts;

/// <param name="RedirectedFrom">The checkout whose knit root redirect points here, or null.</param>
/// <param name="Unshared">This worktree uses its own .knit/ undecided while another checkout has one, else null.</param>
public sealed record RootDto(string RootPath, string? RedirectedFrom, UnsharedWorktreeDto? Unshared);

/// <param name="Target">The checkout to share, or null when none can be named (reftable, no default worktree).</param>
/// <param name="Bare">A bare repo's worktree — no main checkout, the share is by path.</param>
/// <param name="Command">The command that shares it; <c>knit root --local</c> keeps this one instead.</param>
public sealed record UnsharedWorktreeDto(string? Target, bool Bare, string Command);

/// <param name="Checkout">The checkout whose HEAD this is, or null when the knit root is not in a git repo.</param>
/// <param name="Branch">The checked-out branch, or null when detached or unknown.</param>
/// <param name="ShortSha">The abbreviated commit when HEAD is detached.</param>
/// <param name="Rebasing">A rebase is in progress — why HEAD is detached.</param>
/// <param name="Worktree">The working directory is a linked worktree, whose root this is, else null.</param>
public sealed record GitHeadDto(string? Checkout, string? Branch, string? ShortSha, bool Rebasing, string? Worktree);

/// <param name="Name">The effective name: <c>repo_name</c>, else <paramref name="Default"/>.</param>
/// <param name="Default">The folder name the repo falls back to.</param>
/// <param name="Source">Where the name came from — a config layer label, or <c>default</c>.</param>
public sealed record RepoNameDto(string Name, string Default, string Source);

/// <param name="Name">The new name; null or blank unsets it, back to the folder name.</param>
public sealed record RepoNameRequestDto(string? Name);

/// <summary>
/// GET /api/share/origin — what this repo's share links are built from.
/// <c>Kind</c> is <c>forge</c>, <c>none</c>, <c>unknown_host</c> or <c>invalid</c>;
/// <c>Web</c> (forge only) is what a pasted URL must start with to be this repo's.
/// Never the raw origin, which may carry a token.
/// </summary>
public sealed record ShareOriginDto(string Kind, string? Forge, string? Host, string? Web, string RepoName, string? Error);

/// <summary>GET /api/share?path= — the share link to one file, or the origin state that stops it.</summary>
public sealed record ShareLinkDto(ShareOriginDto Origin, string? Url, string Path);
