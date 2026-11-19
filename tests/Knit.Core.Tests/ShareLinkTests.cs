using System.Text.Json.Nodes;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Sharing;

namespace Knit.Core.Tests;

/// <summary>Origin normalization, forge kinds and the blob URL per kind.</summary>
public class GitRemoteTests
{
    [Theory]
    [InlineData("https://github.com/acme/app.git", "github.com", "https://github.com", "acme/app")]
    [InlineData("https://github.com/acme/app", "github.com", "https://github.com", "acme/app")]
    [InlineData("https://bob:ghp_secret@github.com/acme/app.git", "github.com", "https://github.com", "acme/app")]
    [InlineData("git@github.com:acme/app.git", "github.com", "https://github.com", "acme/app")]
    [InlineData("ssh://git@git.example.com:2222/team/app.git", "git.example.com", "https://git.example.com", "team/app")]
    [InlineData("http://git.example.com:3000/team/app", "git.example.com", "http://git.example.com:3000", "team/app")]
    [InlineData("https://org@dev.azure.com/org/Proj/_git/repo", "dev.azure.com", "https://dev.azure.com", "org/Proj/_git/repo")]
    [InlineData("git@ssh.dev.azure.com:v3/org/Proj/repo", "ssh.dev.azure.com", "https://ssh.dev.azure.com", "v3/org/Proj/repo")]
    [InlineData("github-work:acme/app.git", "github-work", "https://github-work", "acme/app")]
    public void Normalizes(string url, string host, string webBase, string path)
    {
        var p = GitRemote.Parse(url);
        Assert.Equal(RemoteParseKind.Ok, p.Kind);
        Assert.Equal(new RemoteRepo(host, webBase, path), p.Repo);
    }

    [Theory]
    [InlineData("C:\\src\\app")]
    [InlineData("C:/src/app")]
    [InlineData("../app")]
    [InlineData("/srv/git/app.git")]
    [InlineData("file:///srv/git/app.git")]
    public void A_local_remote_has_no_forge(string url) => Assert.Equal(RemoteParseKind.Local, GitRemote.Parse(url).Kind);

    [Theory]
    [InlineData("https://github.com/")]
    [InlineData("ftp://host/o/r")]
    [InlineData("git@host:")]
    public void What_does_not_parse_is_invalid(string url) => Assert.Equal(RemoteParseKind.Invalid, GitRemote.Parse(url).Kind);
}

public class ForgesTests
{
    private static ForgeEntry Kind(string k, string? web = null) => Forges.TryParse(k, web, out _)!;

    [Theory]
    [InlineData("github", "https://github.com/acme/app/blob/HEAD/.knit/items/k3f9-x.md")]
    [InlineData("gitlab", "https://github.com/acme/app/-/blob/HEAD/.knit/items/k3f9-x.md")]
    [InlineData("gitea", "https://github.com/acme/app/src/branch/HEAD/.knit/items/k3f9-x.md")]
    [InlineData("custom:{web}/src/{ref}/{path}", "https://github.com/acme/app/src/HEAD/.knit/items/k3f9-x.md")]
    public void Blob_url_per_kind(string kind, string expected) =>
        Assert.Equal(expected, Forges.BlobUrl(Kind(kind), "https://github.com/acme/app", ".knit/items/k3f9-x.md"));

    [Fact]
    public void Azure_puts_the_path_in_the_query_and_names_no_branch() =>
        Assert.Equal("https://dev.azure.com/org/Proj/_git/repo?path=/.knit/kb/doc/a%20b.md",
            Forges.BlobUrl(Kind("azure"), "https://dev.azure.com/org/Proj/_git/repo", ".knit/kb/doc/a b.md"));

    [Theory]
    [InlineData("git@ssh.dev.azure.com:v3/org/Proj/repo", "https://dev.azure.com/org/Proj/_git/repo")]
    [InlineData("org@vs-ssh.visualstudio.com:v3/org/Proj/repo", "https://org.visualstudio.com/Proj/_git/repo")]
    [InlineData("https://org@dev.azure.com/org/Proj/_git/repo", "https://dev.azure.com/org/Proj/_git/repo")]
    public void Azure_ssh_remotes_map_to_the_web_route(string url, string web)
    {
        var repo = GitRemote.Parse(url).Repo!;
        Assert.Equal(web, Forges.Web(Forges.BuiltIn(repo.Host)!, repo));
    }

    [Fact]
    public void A_web_override_replaces_the_host_in_front_of_the_repo_path()
    {
        var repo = GitRemote.Parse("git@github-work:acme/app.git").Repo!;
        Assert.Equal("https://github.com/acme/app", Forges.Web(Kind("github", "https://github.com/"), repo));
    }

    [Theory]
    [InlineData("custom:ssh://{web}/{path}")]
    [InlineData("custom:{web}/blob")]
    [InlineData("bitbucket")]
    public void Refuses_what_cannot_build_a_web_link(string kind) => Assert.Null(Forges.TryParse(kind, null, out _));

    [Fact]
    public void Refuses_a_non_http_web_base() => Assert.Null(Forges.TryParse("gitea", "ssh://x", out _));

    [Fact]
    public void Round_trips_through_json()
    {
        var e = Kind("gitea", "https://example.com/gitea");
        Assert.Equal(e, Forges.FromJson(Forges.ToJson(e), out _));
        Assert.Equal(Kind("github"), Forges.FromJson(JsonValue.Create("github"), out _));
    }
}

public sealed class ShareLinkOriginTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("knit-share-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Origin(string url) =>
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, ".git")).FullName, "config"),
            $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {url}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");

    [Fact]
    public void A_knit_root_in_a_subfolder_links_relative_to_the_git_root()
    {
        Origin("git@github.com:acme/mono.git");
        var knitRoot = Directory.CreateDirectory(Path.Combine(_dir, "tools", "app")).FullName;
        var file = Path.Combine(knitRoot, ".knit", "items", "k3f9-x.md");
        var origin = ShareLinks.Origin(knitRoot, _dir, new KnitConfig());
        Assert.Equal("https://github.com/acme/mono/blob/HEAD/tools/app/.knit/items/k3f9-x.md",
            ShareLinks.Link(origin, knitRoot, file).Url);

        File.Delete(Path.Combine(_dir, ".git", "config"));
        var none = ShareLinks.Origin(knitRoot, _dir, new KnitConfig(RepoName: "mono"));
        Assert.Equal("mono:tools/app/.knit/items/k3f9-x.md", ShareLinks.Link(none, knitRoot, file).Url);
    }

    [Fact]
    public void A_config_entry_beats_the_built_in_host()
    {
        Origin("https://github.com/acme/app");
        var config = new KnitConfig(Forges: new Dictionary<string, JsonNode?> { ["GitHub.com"] = "custom:{web}/raw/{ref}/{path}" });
        var origin = ShareLinks.Origin(_dir, _dir, config);
        Assert.Equal("https://github.com/acme/app/raw/HEAD/a.md",
            ShareLinks.Link(origin, _dir, Path.Combine(_dir, "a.md")).Url);
    }

    [Fact]
    public void An_unknown_host_names_the_command_and_never_the_raw_origin()
    {
        Origin("https://bob:tok3n@git.example.com/team/app.git");
        var origin = ShareLinks.Origin(_dir, _dir, new KnitConfig());
        Assert.Equal(ShareOriginKind.UnknownHost, origin.Kind);
        Assert.Contains("knit config set forges.git.example.com", origin.Error);
        Assert.DoesNotContain("tok3n", origin.ToString());
    }

    [Fact]
    public void A_bad_origin_is_invalid_and_names_at_most_the_host()
    {
        Origin("https://bob:tok3n@git.example.com/");
        var origin = ShareLinks.Origin(_dir, _dir, new KnitConfig());
        Assert.Equal(ShareOriginKind.Invalid, origin.Kind);
        Assert.DoesNotContain("tok3n", origin.ToString());
        Assert.Contains("git.example.com", origin.Error);
    }
}

public class GitConfigFileTests
{
    [Fact]
    public void Reads_quoted_subsections_case_sensitively_and_strips_comments()
    {
        var entries = GitConfigFile.Parse([
            "# top",
            "[Remote \"Origin\"]",
            "  url = wrong",
            "[remote \"origin\"] ",
            "\tURL = \"https://h/o/r\" ; trailing",
            "\turl = second",
            "[core]",
            "\tbare",
        ]);
        Assert.Equal("https://h/o/r", GitConfigFile.Get(entries, "remote", "origin", "url"));
        Assert.Equal("true", GitConfigFile.Get(entries, "core", null, "bare"));
    }

    [Fact]
    public void A_worktree_reads_the_common_dir_config()
    {
        var dir = Directory.CreateTempSubdirectory("knit-gitcfg-").FullName;
        try
        {
            var common = Directory.CreateDirectory(Path.Combine(dir, "main", ".git")).FullName;
            File.WriteAllText(Path.Combine(common, "config"), "[remote \"origin\"]\n\turl = git@github.com:a/b.git\n");
            var wt = Directory.CreateDirectory(Path.Combine(common, "worktrees", "feat")).FullName;
            File.WriteAllText(Path.Combine(wt, "commondir"), "../..\n");
            Assert.Equal(common, GitWorktree.CommonDirOf(wt));
            Assert.Equal("git@github.com:a/b.git", GitRemote.OriginUrl(wt));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
