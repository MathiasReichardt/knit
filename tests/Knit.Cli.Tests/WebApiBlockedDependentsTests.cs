using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Knit.WebApi;
using Knit.WebApi.Contracts;

namespace Knit.Cli.Tests;

/// <summary>
/// The web close paths (PATCH state, dismiss) return
/// <c>blockedDependents</c> like the CLI, and the doctor carries the verify
/// finding.
/// </summary>
[Collection(nameof(CliCollection))]
public class WebApiBlockedDependentsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static (TempKnitRoot Root, string Blocker, string Dep) Seed()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        string Add(string title) => JsonDocument.Parse(root.Run("add", "task", title, "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        var blocker = Add("blocker");
        var dep = Add("dependent");
        Assert.Equal(0, root.Run("block", dep, "--by", blocker).ExitCode);
        return (root, blocker, dep);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string id, string suffix, object? body)
    {
        var etag = (await http.GetAsync($"/api/items/{id}")).Headers.ETag!.Tag;
        var req = new HttpRequestMessage(method, $"/api/items/{id}{suffix}");
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return resp;
    }

    [Fact]
    public async Task Patch_to_done_returns_the_dependents()
    {
        var (root, blocker, dep) = Seed();
        using var _ = root;
        await using var host = await Host.StartAsync(root.Path);

        var resp = await Send(host.Client, HttpMethod.Patch, blocker, "", new ItemPatchRequestDto(State: "done"));
        var dto = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
        var d = Assert.Single(dto!.BlockedDependents!);
        Assert.Equal(dep, d.Id);
        Assert.Equal("dependent", d.Title);
        Assert.Null(d.Remaining);
        Assert.False(string.IsNullOrEmpty(d.ShortId));

        // Another PATCH that closes nothing omits the field.
        var again = await Send(host.Client, HttpMethod.Patch, blocker, "", new ItemPatchRequestDto(Assignee: "bob"));
        Assert.DoesNotContain("blockedDependents", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dismiss_returns_the_dependents_and_empty_when_none()
    {
        var (root, blocker, dep) = Seed();
        using var _ = root;
        await using var host = await Host.StartAsync(root.Path);

        var resp = await Send(host.Client, HttpMethod.Post, blocker, "/dismiss", null);
        var dto = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
        Assert.Equal(dep, Assert.Single(dto!.BlockedDependents!).Id);

        var lone = await Send(host.Client, HttpMethod.Post, dep, "/dismiss", null);
        Assert.Empty((await lone.Content.ReadFromJsonAsync<ItemDto>(JsonOpts))!.BlockedDependents!);
    }

    [Fact]
    public async Task Doctor_verify_carries_the_stale_blocker()
    {
        var (root, blocker, dep) = Seed();
        using var _ = root;
        Assert.Equal(0, root.Run("done", blocker).ExitCode);
        await using var host = await Host.StartAsync(root.Path);

        var verify = await host.Client.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", JsonOpts);
        var row = Assert.Single(verify!.StaleBlockers);
        Assert.Equal(dep, row.ItemId);
        Assert.Equal(blocker, row.BlockerId);
        Assert.Equal("done", row.BlockerState);
        Assert.True(row.Sole);
    }

    [Fact]
    public async Task Context_lists_the_own_block_and_the_holding_parent_with_ids()
    {
        var (root, blocker, dep) = Seed();
        using var _ = root;
        var feature = JsonDocument.Parse(root.Run("add", "feature", "phase 2", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;
        Assert.Equal(0, root.Run("link", dep, "--parent", feature).ExitCode);
        Assert.Equal(0, root.Run("block", feature, "--reason", "phase 1 first").ExitCode);
        await using var host = await Host.StartAsync(root.Path);

        var ctx = await host.Client.GetFromJsonAsync<ContextDto>($"/api/items/{dep}/context", JsonOpts);
        Assert.Equal(2, ctx!.Blocked.Count);
        Assert.Null(ctx.Blocked[0].InheritedFrom);
        Assert.Equal(blocker, Assert.Single(ctx.Blocked[0].BlockedBy).Id);
        Assert.Equal(feature, ctx.Blocked[1].InheritedFrom);
        Assert.Equal("phase 1 first", ctx.Blocked[1].Note);

        var item = await host.Client.GetFromJsonAsync<ItemDto>($"/api/items/{dep}", JsonOpts);
        Assert.Equal(feature, item!.HeldBy!.Id);
    }

    private sealed class Host(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Host> StartAsync(string root)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            return new Host(app, TestApi.Client(port));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
