using Knit.Core.Config;
using Knit.Core.Discovery;

namespace Knit.Core.Sharing;

public enum ShareOriginKind
{
    /// <summary>The origin's host maps to a forge: links are forge URLs.</summary>
    Forge,
    /// <summary>No git repo, no origin, or a local one: links are the SL5 form.</summary>
    None,
    /// <summary>The origin's host is neither built in nor in <c>forges</c> (SL4).</summary>
    UnknownHost,
    /// <summary>The origin doesn't parse, or its <c>forges</c> entry doesn't.</summary>
    Invalid,
}

/// <summary>
/// What this repo's share links are built from, read live (SL3). Carries the
/// normalized parts only — the raw origin never leaves <see cref="ShareLinks.Origin"/>.
/// </summary>
/// <param name="GitRoot">The checkout holding the knit root — paths are relative to it, not to the knit root.</param>
/// <param name="Host">The origin's host; on <see cref="ShareOriginKind.Invalid"/> only when it could be read.</param>
/// <param name="Web">The repo's web address — what a pasted URL must start with (SL1). Forge only.</param>
/// <param name="Error">Why there is no link, with the command that fixes it. UnknownHost / Invalid only.</param>
public sealed record ShareOrigin(
    ShareOriginKind Kind, string? GitRoot, string RepoName, ForgeEntry? Forge, string? Host, string? Web, string? Error);

/// <param name="Url">The forge URL or the <c>&lt;repo_name&gt;:&lt;path&gt;</c> form; null when <see cref="ShareOrigin.Error"/> stands.</param>
/// <param name="Path">The file relative to the git root (the knit root without one), <c>/</c>-separated.</param>
public sealed record ShareLink(string? Url, string Path);

/// <summary>
/// Share links: a doc's blob URL on the default branch of the
/// repo's forge, so a coworker can open it without knowing knit.
/// </summary>
public static class ShareLinks
{
    public static ShareOrigin Origin(string knitRoot, string? gitRoot, KnitConfig config)
    {
        var checkout = GitHead.CheckoutFor(knitRoot, gitRoot) ?? RepoDiscovery.FindGitRoot(Path.GetFullPath(knitRoot));
        var name = string.IsNullOrWhiteSpace(config.RepoName) ? GitHead.DefaultRepoName(knitRoot, gitRoot) : config.RepoName!;
        if (checkout is null || GitWorktree.GitDir(checkout) is not { } gitDir || GitRemote.OriginUrl(gitDir) is not { } url)
            return new ShareOrigin(ShareOriginKind.None, checkout, name, null, null, null, null);

        var parsed = GitRemote.Parse(url);
        if (parsed.Kind == RemoteParseKind.Local)
            return new ShareOrigin(ShareOriginKind.None, checkout, name, null, null, null, null);
        if (parsed.Repo is not { } repo)
            return new ShareOrigin(ShareOriginKind.Invalid, checkout, name, null, parsed.Host, null,
                $"the origin remote{(parsed.Host is null ? "" : $" (host {parsed.Host})")} does not parse as a git URL — " +
                "fix it with 'git remote set-url origin <url>'");

        var configured = config.Forges?.FirstOrDefault(kv => string.Equals(kv.Key, repo.Host, StringComparison.OrdinalIgnoreCase));
        ForgeEntry? forge;
        if (configured is { Key: not null } c)
        {
            forge = Forges.FromJson(c.Value, out var error);
            if (forge is null)
                return new ShareOrigin(ShareOriginKind.Invalid, checkout, name, null, repo.Host, null,
                    $"config '{Forges.Key}.{c.Key}' is invalid — {error}; fix it with " +
                    $"'knit config set {Forges.Key}.{repo.Host} <kind>'");
        }
        else forge = Forges.BuiltIn(repo.Host);

        if (forge is null)
            return new ShareOrigin(ShareOriginKind.UnknownHost, checkout, name, null, repo.Host, null, UnknownHost(repo.Host));
        return new ShareOrigin(ShareOriginKind.Forge, checkout, name, forge, repo.Host, Forges.Web(forge, repo), null);
    }

    /// <summary>The link to the file at <paramref name="fullPath"/>.</summary>
    public static ShareLink Link(ShareOrigin origin, string knitRoot, string fullPath)
    {
        var path = Path.GetRelativePath(origin.GitRoot ?? knitRoot, Path.GetFullPath(fullPath)).Replace('\\', '/');
        var url = origin.Kind switch
        {
            ShareOriginKind.Forge => Forges.BlobUrl(origin.Forge!, origin.Web!, path),
            ShareOriginKind.None => $"{origin.RepoName}:{path}",
            _ => null,
        };
        return new ShareLink(url, path);
    }

    private static string UnknownHost(string host) =>
        $"knit doesn't know which forge runs {host} — tell it once: " +
        $"'knit config set {Forges.Key}.{host} <{string.Join('|', "github", "gitlab", "gitea", "azure")}|custom:<template>>' " +
        "(--global for every repo on this machine; --web <url> when the host isn't the web address)";
}
