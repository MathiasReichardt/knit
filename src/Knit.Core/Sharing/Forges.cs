using System.Text.Json.Nodes;
using Knit.Core.Frontmatter;

namespace Knit.Core.Sharing;

public enum ForgeKind
{
    GitHub,
    GitLab,
    Gitea,
    Azure,
    Custom,
}

/// <summary>
/// One <c>forges</c> entry (SL2): what runs on a host, and — when the origin's
/// host is no web host (an SSH alias, a separate SSH host, a sub-path install)
/// — the web address its repos live under.
/// </summary>
/// <param name="Template">The <c>custom:</c> URL template, with <c>{web}</c>, <c>{ref}</c> and <c>{path}</c>.</param>
/// <param name="Web">Replaces <c>https://&lt;host&gt;</c> in front of the repo path; null to keep it.</param>
public sealed record ForgeEntry(ForgeKind Kind, string? Template, string? Web)
{
    /// <summary>The kind as written: <c>github</c>, …, <c>custom:&lt;template&gt;</c>.</summary>
    public string KindText => Kind == ForgeKind.Custom ? $"{Forges.CustomPrefix}{Template}" : Forges.Name(Kind);
}

/// <summary>
/// The forge kinds, the built-in hosts and the blob URL per kind. Every kind
/// names the default branch with the literal <c>HEAD</c> (SL3).
/// </summary>
public static class Forges
{
    public const string Key = "forges";
    public const string CustomPrefix = "custom:";
    public const string Ref = "HEAD";
    private const string KindList = "github, gitlab, gitea, azure or custom:<template>";

    public static string Name(ForgeKind kind) => kind switch
    {
        ForgeKind.GitHub => "github",
        ForgeKind.GitLab => "gitlab",
        ForgeKind.Gitea => "gitea",
        ForgeKind.Azure => "azure",
        _ => "custom",
    };

    /// <summary>The hosts knit knows without a <c>forges</c> entry; an entry for the same host wins.</summary>
    public static ForgeEntry? BuiltIn(string host) => host.ToLowerInvariant() switch
    {
        "github.com" => new ForgeEntry(ForgeKind.GitHub, null, null),
        "gitlab.com" => new ForgeEntry(ForgeKind.GitLab, null, null),
        "codeberg.org" or "gitea.com" => new ForgeEntry(ForgeKind.Gitea, null, null),
        "dev.azure.com" or "ssh.dev.azure.com" => new ForgeEntry(ForgeKind.Azure, null, null),
        var h when h.EndsWith(".visualstudio.com", StringComparison.Ordinal) => new ForgeEntry(ForgeKind.Azure, null, null),
        _ => null,
    };

    /// <summary>A kind and an optional web base from the command line; the error says why not.</summary>
    public static ForgeEntry? TryParse(string kind, string? web, out string? error)
    {
        var k = kind.Trim();
        ForgeEntry? entry = k.ToLowerInvariant() switch
        {
            "github" => new ForgeEntry(ForgeKind.GitHub, null, null),
            "gitlab" => new ForgeEntry(ForgeKind.GitLab, null, null),
            "gitea" or "forgejo" => new ForgeEntry(ForgeKind.Gitea, null, null),
            "azure" => new ForgeEntry(ForgeKind.Azure, null, null),
            _ when k.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)
                => new ForgeEntry(ForgeKind.Custom, k[CustomPrefix.Length..], null),
            _ => null,
        };
        if (entry is null)
        {
            error = $"unknown forge kind '{k}' — use {KindList}";
            return null;
        }
        if (entry.Template is { } t && TemplateError(t) is { } te)
        {
            error = te;
            return null;
        }
        if (web is not null)
        {
            var w = web.Trim().TrimEnd('/');
            if (!ExternalRefs.IsWebUrl(w))
            {
                error = $"--web must be an http(s) URL, the address the host's repos live under — got '{web}'";
                return null;
            }
            entry = entry with { Web = w };
        }
        error = null;
        return entry;
    }

    /// <summary>One entry as stored: the kind string, or <c>{ "kind", "web" }</c>.</summary>
    public static ForgeEntry? FromJson(JsonNode? node, out string? error)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var kind):
                return TryParse(kind, null, out error);
            case JsonObject o when o["kind"] is JsonValue kv && kv.TryGetValue<string>(out var kind):
                string? web = null;
                if (o["web"] is { } wn && !(wn is JsonValue wv && wv.TryGetValue(out web)))
                {
                    error = "\"web\" must be a string";
                    return null;
                }
                return TryParse(kind, web, out error);
            default:
                error = $"must be a kind string ({KindList}) or {{ \"kind\": …, \"web\": \"https://…\" }}";
                return null;
        }
    }

    public static JsonNode ToJson(ForgeEntry e) =>
        e.Web is null
            ? JsonValue.Create(e.KindText)
            : new JsonObject { ["kind"] = e.KindText, ["web"] = e.Web };

    /// <summary>
    /// The repo's web address: <paramref name="repo"/>'s path under the entry's
    /// web base. Azure's SSH remotes (<c>v3/{org}/{project}/{repo}</c>) map to
    /// the HTTPS route (<c>{org}/{project}/_git/{repo}</c>).
    /// </summary>
    public static string Web(ForgeEntry entry, RemoteRepo repo)
    {
        var (webBase, path) = (repo.Base, repo.Path);
        if (entry.Kind == ForgeKind.Azure && path.Split('/') is ["v3", var org, var project, var name])
        {
            (webBase, path) = repo.Host.EndsWith("visualstudio.com", StringComparison.Ordinal)
                ? ($"https://{org}.visualstudio.com", $"{project}/_git/{name}")
                : ("https://dev.azure.com", $"{org}/{project}/_git/{name}");
        }
        return $"{entry.Web ?? webBase}/{path}";
    }

    /// <summary>The blob URL of <paramref name="path"/> (git-root-relative, <c>/</c>-separated) on the default branch.</summary>
    public static string BlobUrl(ForgeEntry entry, string web, string path)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return entry.Kind switch
        {
            ForgeKind.GitHub => $"{web}/blob/{Ref}/{escaped}",
            ForgeKind.GitLab => $"{web}/-/blob/{Ref}/{escaped}",
            ForgeKind.Gitea => $"{web}/src/branch/{Ref}/{escaped}",
            // No `version`: Azure then shows the default branch.
            ForgeKind.Azure => $"{web}?path=/{escaped}",
            _ => entry.Template!.Replace("{web}", web).Replace("{ref}", Ref).Replace("{path}", escaped),
        };
    }

    private static string? TemplateError(string template)
    {
        if (!template.Contains("{path}", StringComparison.Ordinal))
            return "a custom: template must contain {path}";
        var sample = template.Replace("{web}", "https://example.com/o/r").Replace("{ref}", Ref).Replace("{path}", "a/b.md");
        return ExternalRefs.IsWebUrl(sample)
            ? null
            : "a custom: template must yield an http(s) URL — start it with {web} or https://";
    }
}
