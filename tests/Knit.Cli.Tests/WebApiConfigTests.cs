using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.WebApi;
using Knit.WebApi.Contracts;

namespace Knit.Cli.Tests;

/// <summary>
/// `/api/config` — the settings view reads the same report the CLI
/// prints, so there is one definition of "effective value" and one of "source".
/// </summary>
public class WebApiConfigTests
{
    [Fact]
    public async Task Keys_carry_the_same_values_sources_and_defaults_as_the_report()
    {
        var root = CreateRepo("""{"handle":"alice","display":"Alice","due_soon_days":5}""");
        try
        {
            await WithApi(root, async http =>
            {
                var dto = await http.GetFromJsonAsync<ConfigDto>(
                    "/api/config", new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.NotNull(dto);

                var expected = ConfigReport.Build(root);
                Assert.Equal(
                    expected.Keys.Select(e => (e.Name, e.Value, e.SourceLabel, e.Default)).ToList(),
                    dto.Keys.Select(k => (k.Key, k.Value, k.Source, k.Default)).ToList());

                var window = dto.Keys.Single(k => k.Key == "due_soon_days");
                Assert.Equal("5", window.Value);
                Assert.Equal(ConfigLoader.FileName, window.Source);
                Assert.Empty(dto.Unreadable);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task An_unreadable_layer_is_named_so_the_view_cannot_show_defaults_as_fact()
    {
        var root = CreateRepo("{ not json");
        try
        {
            await WithApi(root, async http =>
            {
                var dto = await http.GetFromJsonAsync<ConfigDto>(
                    "/api/config", new JsonSerializerOptions(JsonSerializerDefaults.Web));

                Assert.Equal([ConfigLoader.FileName], dto!.Unreadable);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    private static string CreateRepo(string configText)
    {
        var parent = Path.Combine(Path.GetTempPath(), $"knit-configapi-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "repo");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        File.WriteAllText(Path.Combine(knitDir, ConfigLoader.FileName), configText);
        return root;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static async Task WithApi(string root, Func<HttpClient, Task> body)
    {
        var port = GetFreePort();
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
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
