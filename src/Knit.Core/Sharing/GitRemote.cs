using System.Text.RegularExpressions;

namespace Knit.Core.Sharing;

/// <summary>
/// A remote reduced to what a share link needs — never the URL it came from,
/// which may carry a token (SL3).
/// </summary>
/// <param name="Host">The host as git connects to it, lower-cased, no port — what <c>forges</c> is keyed by.</param>
/// <param name="Base">The web origin the host serves: <c>https://host</c>, or an http(s) remote's own scheme and port.</param>
/// <param name="Path">The repo path under <paramref name="Base"/>, no <c>.git</c>, no leading or trailing <c>/</c>.</param>
public sealed record RemoteRepo(string Host, string Base, string Path);

public enum RemoteParseKind
{
    Ok,
    /// <summary>A local path or <c>file://</c> remote: no forge serves it.</summary>
    Local,
    Invalid,
}

/// <param name="Host">On <see cref="RemoteParseKind.Invalid"/>, the host when one could be read — the only part an error may name.</param>
public sealed record RemoteParse(RemoteParseKind Kind, RemoteRepo? Repo, string? Host);

public static partial class GitRemote
{
    // scp-like `[user@]host:path` — git's own rule: no `/` before the first colon.
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^@:/\s]+):(?<path>[^\s]*)$")]
    private static partial Regex ScpLike();

    /// <summary>The <c>origin</c> url of the repo whose git dir is <paramref name="gitDir"/>, or null.</summary>
    public static string? OriginUrl(string gitDir)
    {
        var config = Path.Combine(Discovery.GitWorktree.CommonDirOf(gitDir), "config");
        var url = Discovery.GitConfigFile.Get(Discovery.GitConfigFile.Read(config), "remote", "origin", "url");
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    /// <summary>
    /// Normalize: <c>git@host:o/r.git</c> and <c>ssh://git@host:2222/o/r</c>
    /// become <c>https://host/o/r</c>; userinfo is dropped everywhere. What
    /// doesn't parse is <see cref="RemoteParseKind.Invalid"/>, never a guess.
    /// </summary>
    public static RemoteParse Parse(string url)
    {
        var s = url.Trim();
        if (s.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return Invalid(null);
            var host = u.Host.ToLowerInvariant();
            switch (u.Scheme)
            {
                case "file":
                    return new RemoteParse(RemoteParseKind.Local, null, null);
                case "http" or "https":
                    return Repo(host, $"{u.Scheme}://{host}{(u.IsDefaultPort ? "" : $":{u.Port}")}", u.AbsolutePath);
                case "ssh" or "git" or "git+ssh" or "ssh+git":
                    return Repo(host, $"https://{host}", u.AbsolutePath);
                default:
                    return Invalid(host.Length > 0 ? host : null);
            }
        }

        var m = ScpLike().Match(s);
        // A one-letter "host" is a Windows drive (`C:\repo`, `C:/repo`).
        if (!m.Success || m.Groups["host"].Value.Length == 1)
            return new RemoteParse(RemoteParseKind.Local, null, null);
        var scpHost = m.Groups["host"].Value.ToLowerInvariant();
        return Repo(scpHost, $"https://{scpHost}", m.Groups["path"].Value);
    }

    private static RemoteParse Repo(string host, string webBase, string path)
    {
        if (host.Length == 0) return Invalid(null);
        var p = path.Trim('/');
        if (p.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
        p = p.TrimEnd('/');
        if (p.Length == 0 || p.Split('/').Any(seg => seg is "" or "." or "..")) return Invalid(host);
        return new RemoteParse(RemoteParseKind.Ok, new RemoteRepo(host, webBase, p), host);
    }

    private static RemoteParse Invalid(string? host) => new(RemoteParseKind.Invalid, null, host);
}
