using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Knit.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Knit.Cli.Tests;

/// <summary>
/// `/api/root/name` and `/api/root/git` — the header's repo name (the one
/// config key the web API writes) and the branch label.
/// </summary>
public class WebApiRepoTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"knit-repoapi-{Guid.NewGuid():N}");
    private readonly string _root;

    public WebApiRepoTests()
    {
        _root = Path.Combine(_parent, "my-app");
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "items"));
        File.WriteAllText(Path.Combine(_root, ".knit", ConfigLoader.FileName), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch { }
    }

    private string KnitFile(string name) => Path.Combine(_root, ".knit", name);

    [Fact]
    public async Task An_unset_name_is_the_folder_name() =>
        await WithApi(null, async http =>
        {
            var dto = await http.GetFromJsonAsync<RepoNameDto>("/api/root/name", Web);

            Assert.Equal(new RepoNameDto("my-app", "my-app", "default"), dto);
        });

    [Fact]
    public async Task Renaming_writes_config_json_and_a_blank_name_unsets_it() =>
        await WithApi(null, async http =>
        {
            var set = await http.PutAsJsonAsync("/api/root/name", new { name = "  My App " });
            set.EnsureSuccessStatusCode();
            Assert.Equal("My App", (await set.Content.ReadFromJsonAsync<RepoNameDto>(Web))!.Name);
            Assert.Contains("\"repo_name\": \"My App\"", File.ReadAllText(KnitFile(ConfigLoader.FileName)));

            var unset = await http.PutAsJsonAsync("/api/root/name", new { name = "" });
            Assert.Equal("my-app", (await unset.Content.ReadFromJsonAsync<RepoNameDto>(Web))!.Name);
            Assert.DoesNotContain("repo_name", File.ReadAllText(KnitFile(ConfigLoader.FileName)));
        });

    [Fact]
    public async Task A_local_override_refuses_the_rename_instead_of_writing_a_value_that_would_not_show()
    {
        File.WriteAllText(KnitFile(ConfigLoader.LocalFileName), """{"repo_name":"Mine"}""");

        await WithApi(null, async http =>
        {
            var r = await http.PutAsJsonAsync("/api/root/name", new { name = "Team" });

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.DoesNotContain("repo_name", File.ReadAllText(KnitFile(ConfigLoader.FileName)));
        });
    }

    [Fact]
    public async Task An_unreadable_config_json_is_not_overwritten()
    {
        File.WriteAllText(KnitFile(ConfigLoader.FileName), "{ not json");

        await WithApi(null, async http =>
        {
            var r = await http.PutAsJsonAsync("/api/root/name", new { name = "Team" });

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("{ not json", File.ReadAllText(KnitFile(ConfigLoader.FileName)));
        });
    }

    [Fact]
    public async Task The_branch_is_read_fresh_on_every_request()
    {
        var gitDir = Path.Combine(_root, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        await WithApi(_root, async http =>
        {
            Assert.Equal("main", (await http.GetFromJsonAsync<GitHeadDto>("/api/root/git", Web))!.Branch);

            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feat\n");
            var dto = await http.GetFromJsonAsync<GitHeadDto>("/api/root/git", Web);

            Assert.Equal(new GitHeadDto(_root, "feat", null, false, null), dto);
        });
    }

    [Fact]
    public async Task A_checkout_pushes_a_git_event_the_way_git_writes_head()
    {
        var gitDir = Path.Combine(_root, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        using var watcher = new FileWatcher(
            new WebApiOptions { RootPath = _root, GitRoot = _root }, NullLogger<FileWatcher>.Instance);
        await watcher.StartAsync(CancellationToken.None);
        using var sub = watcher.Subscribe(out var reader);

        File.WriteAllText(Path.Combine(gitDir, "HEAD.lock"), "ref: refs/heads/feat\n");
        File.Move(Path.Combine(gitDir, "HEAD.lock"), Path.Combine(gitDir, "HEAD"), overwrite: true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await reader.ReadAsync(timeout.Token);
        Assert.Equal(new EventDto("git", "head", "changed"), evt);
    }

    [Fact]
    public async Task A_watcher_error_sends_a_resync()
    {
        using var watcher = new FileWatcher(new WebApiOptions { RootPath = _root }, NullLogger<FileWatcher>.Instance);
        await watcher.StartAsync(CancellationToken.None);
        using var sub = watcher.Subscribe(out var reader);

        watcher.OnWatcherError(new InternalBufferOverflowException());

        Assert.True(reader.TryRead(out var evt));
        Assert.Equal(FileWatcher.Resync, evt);
    }

    [Fact]
    public async Task Outside_git_there_is_no_head() =>
        await WithApi(null, async http =>
        {
            var dto = await http.GetFromJsonAsync<GitHeadDto>("/api/root/git", Web);

            Assert.Equal(new GitHeadDto(null, null, null, false, null), dto);
        });

    [Fact]
    public async Task The_root_carries_an_unshared_worktree_and_its_command()
    {
        var main = Path.Combine(_parent, "main");
        await WithApi(null, async http =>
        {
            var dto = await http.GetFromJsonAsync<RootDto>("/api/root", Web);

            Assert.Equal(new UnsharedWorktreeDto(main, true, $"knit root {main}"), dto!.Unshared);
        }, new GitWorktree.UnsharedWorktree(main, Bare: true));
    }

    [Fact]
    public async Task A_decided_checkout_has_no_unshared_marker() =>
        await WithApi(null, async http =>
            Assert.Null((await http.GetFromJsonAsync<RootDto>("/api/root", Web))!.Unshared));

    private async Task WithApi(
        string? gitRoot, Func<HttpClient, Task> body, GitWorktree.UnsharedWorktree? unshared = null)
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = _root,
            GitRoot = gitRoot,
            Unshared = unshared,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
            EnableSearch = false,
        });
        await app.StartAsync();
        try
        {
            using var http = TestApi.Client(port);
            await body(http);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
