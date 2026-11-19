using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary><c>knit url</c>, the <c>forges.&lt;host&gt;</c> config entry and the init line.</summary>
[Collection(nameof(CliCollection))]
public class ShareLinkCliTests
{
    private static TempKnitRoot Init(string? origin)
    {
        var root = new TempKnitRoot();
        if (origin is not null)
        {
            var git = Directory.CreateDirectory(Path.Combine(root.Path, ".git")).FullName;
            File.WriteAllText(Path.Combine(git, "config"), $"[remote \"origin\"]\n\turl = {origin}\n");
            File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        }
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        return root;
    }

    private static string AddItem(TempKnitRoot root)
    {
        var r = root.Run("add", "task", "Share me", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public void Prints_the_forge_url_of_an_item()
    {
        using var root = Init("git@github.com:acme/app.git");
        var id = AddItem(root);
        var r = root.Run("url", id[..6]);
        Assert.Equal(0, r.ExitCode);
        Assert.Matches($@"^https://github\.com/acme/app/blob/HEAD/\.knit/items/{id}-share-me\.md$", r.Stdout.Trim());

        var json = JsonDocument.Parse(root.Run("url", id, "--json").Stdout).RootElement;
        Assert.Equal("github", json.GetProperty("forge").GetString());
        Assert.Equal("HEAD", json.GetProperty("ref").GetString());
    }

    [Fact]
    public void No_origin_gives_the_repo_name_form()
    {
        using var root = Init(origin: null);
        Assert.Equal(0, root.Run("config", "set", "repo_name", "app").ExitCode);
        var id = AddItem(root);
        Assert.Equal($"app:.knit/items/{id}-share-me.md", root.Run("url", $"item:{id}").Stdout.Trim());
    }

    [Fact]
    public void An_unknown_host_exits_non_zero_with_the_command_and_no_token()
    {
        using var root = Init("https://bob:tok3n@git.example.com/team/app.git");
        var id = AddItem(root);
        var r = root.Run("url", id);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit config set forges.git.example.com", r.Stderr);
        var j = root.Run("url", id, "--json");
        foreach (var output in new[] { r.Stdout, r.Stderr, j.Stdout, j.Stderr })
            Assert.DoesNotContain("tok3n", output);

        Assert.Equal(0, root.Run("config", "set", "forges.git.example.com", "gitea").ExitCode);
        Assert.StartsWith("https://git.example.com/team/app/src/branch/HEAD/.knit/items/", root.Run("url", id).Stdout.Trim());
    }

    [Fact]
    public void A_dotted_host_round_trips_in_the_repo_and_the_global_layer()
    {
        using var root = Init("git@github-work:acme/app.git");
        Assert.Equal(0, root.Run("config", "set", "forges.git.example.com", "gitlab").ExitCode);
        Assert.Equal("gitlab", root.Run("config", "get", "forges.git.example.com").Stdout.Trim());
        Assert.Equal(0, root.Run("config", "unset", "forges.git.example.com").ExitCode);
        Assert.NotEqual(0, root.Run("config", "get", "forges.git.example.com").ExitCode);

        Assert.Equal(0, root.Run("config", "set", "--global", "forges.github-work", "github", "--web", "https://github.com").ExitCode);
        Assert.Contains("\"github-work\"", File.ReadAllText(Path.Combine(root.ConfigHome, "config.json")));
        Assert.Equal("github (web https://github.com)", root.Run("config", "get", "forges.github-work").Stdout.Trim());
        var id = AddItem(root);
        Assert.StartsWith("https://github.com/acme/app/blob/HEAD/", root.Run("url", id).Stdout.Trim());
        Assert.Equal(0, root.Run("config", "unset", "--global", "forges.github-work").ExitCode);
    }

    [Theory]
    [InlineData("custom:ssh://{web}/{path}")]
    [InlineData("bitbucket")]
    public void Config_set_refuses_a_kind_that_cannot_build_a_web_link(string kind)
    {
        using var root = Init(origin: null);
        var r = root.Run("config", "set", "forges.git.example.com", kind);
        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain("forges", File.ReadAllText(Path.Combine(root.Path, ".knit", "config.json")));
    }

    [Fact]
    public void Config_set_of_the_whole_map_points_at_the_host_form()
    {
        using var root = Init(origin: null);
        var r = root.Run("config", "set", "forges", "x");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("forges.<host>", r.Stderr);
    }

    [Fact]
    public void Init_names_the_detected_forge()
    {
        var root = new TempKnitRoot();
        using (root)
        {
            var git = Directory.CreateDirectory(Path.Combine(root.Path, ".git")).FullName;
            File.WriteAllText(Path.Combine(git, "config"), "[remote \"origin\"]\n\turl = https://gitlab.com/acme/app.git\n");
            var r = root.Run("init", "--handle", "a", "--display", "A", "--non-interactive");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("share links: gitlab at gitlab.com", r.Stdout);
        }
    }

    [Fact]
    public void A_kb_entry_and_a_section_ref()
    {
        using var root = Init("https://gitlab.com/acme/app");
        Assert.Equal(0, root.Run("kb", "add", "doc", "guide", "--title", "Guide").ExitCode);
        Assert.Equal("https://gitlab.com/acme/app/-/blob/HEAD/.knit/kb/docs/guide.md", root.Run("url", "doc:guide").Stdout.Trim());
        Assert.NotEqual(0, root.Run("url", "doc:guide#intro").ExitCode);
    }
}
