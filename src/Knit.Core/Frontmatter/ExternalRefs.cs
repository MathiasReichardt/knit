using Knit.Core.Config;

namespace Knit.Core.Frontmatter;

/// <param name="Text">What the Target cell displays — PR and commit URLs are shortened.</param>
/// <param name="Path">
/// The href, or empty when the target isn't addressable by URL (a branch, a
/// mount name, a local directory). An empty path is what keeps these rows out
/// of every path-based scan.
/// </param>
public sealed record ExternalTarget(string Text, string Path);

/// <summary>
/// The <c>## Links</c> rels that point outside the repo: <c>Branch</c>,
/// <c>Repo</c>, <c>PR</c>, <c>Commit</c>. They are explicit rather than one
/// generic scheme-prefixed rel so the table, <c>context</c> and the CLI all
/// read as what they are.
/// </summary>
public static class ExternalRefs
{
    public static bool IsExternal(LinkRel rel) =>
        rel is LinkRel.Branch or LinkRel.Repo or LinkRel.Pr or LinkRel.Commit;

    /// <summary>
    /// Whether <c>context</c> carries the rel down the parent chain. A fix
    /// commit belongs to the item that names it, not to its subtree.
    /// </summary>
    public static bool Inherits(LinkRel rel) => IsExternal(rel) && rel != LinkRel.Commit;

    /// <summary>
    /// Validate and canonicalise a target for one of the external rels.
    /// <paramref name="config"/> supplies the declared mounts a <c>Repo</c>
    /// may name; pass the effective (merged) config.
    /// </summary>
    public static bool TryResolve(
        LinkRel rel, string raw, string root, KnitConfig? config,
        out ExternalTarget target, out string? error)
    {
        target = new ExternalTarget("", "");
        var value = raw.Trim();
        if (value.Length == 0)
        {
            error = "target must not be empty";
            return false;
        }
        // A pipe would split the row into an extra cell on the next read.
        if (value.Contains('|', StringComparison.Ordinal))
        {
            error = "target must not contain '|'";
            return false;
        }

        switch (rel)
        {
            case LinkRel.Branch:
                // Knit never shells out to git, so existence can't be checked
                // here — the value is stored and displayed, never verified.
                if (value.Any(char.IsWhiteSpace))
                {
                    error = "a branch name must not contain whitespace";
                    return false;
                }
                target = new ExternalTarget(value, "");
                error = null;
                return true;

            case LinkRel.Pr:
                if (!IsWebUrl(value))
                {
                    error = "a PR target must be an http(s) URL — the full link, so it works for any forge";
                    return false;
                }
                target = new ExternalTarget(ShortenPr(value), value);
                error = null;
                return true;

            case LinkRel.Commit:
                // A bare SHA is stored as written, like a branch: its repo is
                // the item's Repo row, and it is no link.
                if (IsSha(value))
                {
                    target = new ExternalTarget(value, "");
                    error = null;
                    return true;
                }
                if (!IsWebUrl(value))
                {
                    error = "a commit target must be a SHA (7–64 hex characters) or an http(s) URL";
                    return false;
                }
                target = new ExternalTarget(ShortenCommit(value), value);
                error = null;
                return true;

            case LinkRel.Repo:
                if (IsGitUrl(value))
                {
                    // A browsable remote keeps the URL as the href and shows the
                    // short form, so the cell doesn't read `[url](url)`. An scp-like
                    // or ssh remote isn't a link, so it displays as written.
                    target = IsWebUrl(value)
                        ? new ExternalTarget(ShortenRepoUrl(value), value)
                        : new ExternalTarget(value, "");
                    error = null;
                    return true;
                }
                if (config?.Mounts is not null && config.Mounts.ContainsKey(value))
                {
                    target = new ExternalTarget(value, "");
                    error = null;
                    return true;
                }
                string full;
                try { full = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), value)); }
                catch (Exception ex) { error = ex.Message; return false; }
                if (!Directory.Exists(full))
                {
                    error = $"'{value}' is not a declared mount, a git URL, or a directory at {full}";
                    return false;
                }
                target = new ExternalTarget(value, "");
                error = null;
                return true;

            default:
                error = $"{LinkRels.ToWire(rel)} is not an external rel";
                return false;
        }
    }

    // Uri would escape an inner space; in a table cell it breaks the link.
    public static bool IsWebUrl(string s) =>
        !s.Any(char.IsWhiteSpace)
        && Uri.TryCreate(s, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// An http(s) URL, a <c>scheme://</c> git remote (ssh, git, file), or the
    /// scp-like <c>git@host:org/repo.git</c> form.
    /// </summary>
    public static bool IsGitUrl(string s)
    {
        if (s.Contains("://", StringComparison.Ordinal)) return true;
        var at = s.IndexOf('@');
        var colon = s.IndexOf(':');
        return at > 0 && colon > at + 1;
    }

    /// <summary>
    /// <c>https://github.com/org/repo.git</c> → <c>github.com/org/repo</c> —
    /// the scheme and the <c>.git</c> suffix carry nothing a reader needs.
    /// </summary>
    public static string ShortenRepoUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var path = u.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return $"{u.Host}{path}";
    }

    /// <summary>
    /// <c>https://host/org/repo/pull/42</c> → <c>org/repo#42</c>. Matches
    /// GitHub / Gitea (<c>/pull/</c>) and GitLab (<c>/merge_requests/</c>);
    /// anything else is displayed as the URL it is.
    /// </summary>
    public static string ShortenPr(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segs.Length - 2; i >= 2; i--)
        {
            if (segs[i] is not ("pull" or "pulls" or "merge_requests")) continue;
            // GitLab separates the project path from the route with a `-`.
            var owner = segs[..i].Where(s => s != "-").ToArray();
            if (owner.Length < 2) return url;
            return $"{owner[^2]}/{owner[^1]}#{segs[i + 1]}";
        }
        return url;
    }

    // 40 is SHA-1; a SHA-256 repo's 64 is accepted too.
    public static bool IsSha(string s) =>
        s.Length is >= 7 and <= 64 && s.All(char.IsAsciiHexDigit);

    private static readonly string[] PrRoutes = ["pull", "pulls", "merge_requests"];

    /// <summary>
    /// <c>https://host/org/repo/commit/&lt;sha&gt;</c> → <c>org/repo@abc1234</c>.
    /// Matches GitHub / Gitea (<c>/commit/</c>), GitLab (<c>/-/commit/</c>),
    /// Bitbucket (<c>/commits/</c>) and a commit inside a PR
    /// (<c>/pull/42/commits/</c>); anything else is displayed as the URL it is.
    /// </summary>
    public static string ShortenCommit(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindLastIndex(segs, s => s is "commit" or "commits");
        if (i < 2 || i != segs.Length - 2) return url;
        var sha = segs[i + 1];
        if (!IsSha(sha)) return url;

        var owner = segs[..i];
        var pr = Array.FindIndex(owner, s => PrRoutes.Contains(s));
        if (pr >= 0) owner = owner[..pr];
        owner = [.. owner.Where(s => s != "-")];
        // Azure DevOps routes through `_git`; its shape isn't one we shorten.
        if (owner.Length < 2 || owner.Contains("_git")) return url;
        return $"{owner[^2]}/{owner[^1]}@{sha[..7]}";
    }
}
