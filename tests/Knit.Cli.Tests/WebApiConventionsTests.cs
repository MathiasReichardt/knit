using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Paths;
using Knit.WebApi;
using Knit.WebApi.Contracts;

namespace Knit.Cli.Tests;

/// <summary><c>/api/conventions</c> — both scopes read, each written under its etag.</summary>
public class WebApiConventionsTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-convapi-{Guid.NewGuid():N}");
    private readonly string _configHome;
    private readonly string? _previousConfigHome = Environment.GetEnvironmentVariable(UserPaths.ConfigHomeVariable);

    public WebApiConventionsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "items"));
        File.WriteAllText(Path.Combine(_root, ".knit", ConfigLoader.FileName), "{}");
        _configHome = Path.Combine(_root, "user-config");
        Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, _configHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, _previousConfigHome);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string RepoFile => Path.Combine(_root, ".knit", "conventions.md");

    private string UserFile => Path.Combine(_configHome, "conventions.md");

    [Fact]
    public async Task Missing_files_read_as_empty_scopes_with_their_paths() =>
        await WithApi(BindAddress.Loopback, async http =>
        {
            var dto = await http.GetFromJsonAsync<ConventionsDto>("/api/conventions", Web);

            Assert.Equal(["user", "repo"], dto!.Scopes.Select(s => s.Scope));
            Assert.All(dto.Scopes, s => Assert.False(s.Exists));
            Assert.Equal(UserFile, dto.Scopes[0].Path);
            Assert.Equal(RepoFile, dto.Scopes[1].Path);
            Assert.All(dto.Scopes, s => Assert.True(s.Writable));
        });

    [Fact]
    public async Task A_write_needs_the_current_etag_and_blank_text_removes_the_file() =>
        await WithApi(BindAddress.Loopback, async http =>
        {
            var repo = (await http.GetFromJsonAsync<ConventionsDto>("/api/conventions", Web))!.Scopes[1];

            var written = await Put(http, "repo", "Tag bugs.\r\n", repo.Etag);
            written.EnsureSuccessStatusCode();
            var dto = (await written.Content.ReadFromJsonAsync<ConventionsScopeDto>(Web))!;
            Assert.Equal("Tag bugs.\n", File.ReadAllText(RepoFile));
            Assert.True(dto.Exists);

            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(http, "repo", "Stale.", repo.Etag)).StatusCode);
            Assert.Equal("Tag bugs.\n", File.ReadAllText(RepoFile));

            (await Put(http, "repo", " ", dto.Etag)).EnsureSuccessStatusCode();
            Assert.False(File.Exists(RepoFile));
        });

    [Fact]
    public async Task Delete_removes_the_file_under_its_etag()
    {
        File.WriteAllText(RepoFile, "Rule.\n");

        await WithApi(BindAddress.Loopback, async http =>
        {
            var repo = (await http.GetFromJsonAsync<ConventionsDto>("/api/conventions", Web))!.Scopes[1];

            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Delete(http, "repo", "\"stale\"")).StatusCode);
            Assert.True(File.Exists(RepoFile));

            var r = await Delete(http, "repo", repo.Etag);
            r.EnsureSuccessStatusCode();
            Assert.False((await r.Content.ReadFromJsonAsync<ConventionsScopeDto>(Web))!.Exists);
            Assert.False(File.Exists(RepoFile));
        });
    }

    [Fact]
    public async Task The_user_scope_writes_beside_the_user_config() =>
        await WithApi(BindAddress.Loopback, async http =>
        {
            var user = (await http.GetFromJsonAsync<ConventionsDto>("/api/conventions", Web))!.Scopes[0];

            (await Put(http, "user", "Mine.", user.Etag)).EnsureSuccessStatusCode();

            Assert.Equal("Mine.\n", File.ReadAllText(UserFile));
        });

    [Fact]
    public async Task Beyond_loopback_the_user_scope_is_read_only()
    {
        Directory.CreateDirectory(_configHome);
        File.WriteAllText(UserFile, "Mine.\n");
        // Stands in for a LAN bind without opening a port beyond loopback.
        var lan = new BindAddress("127.0.0.1", IPAddress.Loopback, IsLoopback: false);

        await WithApi(lan, async http =>
        {
            var dto = (await http.GetFromJsonAsync<ConventionsDto>("/api/conventions", Web))!;
            Assert.False(dto.Scopes[0].Writable);
            Assert.Equal("Mine.\n", dto.Scopes[0].Text);
            Assert.True(dto.Scopes[1].Writable);

            var r = await Put(http, "user", "Hijacked.", dto.Scopes[0].Etag);

            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Delete(http, "user", dto.Scopes[0].Etag)).StatusCode);
            Assert.Equal("Mine.\n", File.ReadAllText(UserFile));
        });
    }

    [Fact]
    public async Task An_unknown_scope_is_not_found() =>
        await WithApi(BindAddress.Loopback, async http =>
            Assert.Equal(HttpStatusCode.NotFound, (await Put(http, "team", "x", "*")).StatusCode));

    private static Task<HttpResponseMessage> Put(HttpClient http, string scope, string text, string etag)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/conventions/{scope}")
        {
            Content = JsonContent.Create(new { text }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        return http.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Delete(HttpClient http, string scope, string etag)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/conventions/{scope}");
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        return http.SendAsync(req);
    }

    private async Task WithApi(BindAddress bind, Func<HttpClient, Task> body)
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = _root,
            Bind = bind,
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
