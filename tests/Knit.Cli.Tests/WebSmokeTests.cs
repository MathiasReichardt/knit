using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using Knit.WebApi;
using Xunit;

namespace Knit.Cli.Tests;

public class WebSmokeTests
{
    [Fact]
    public async Task Boot_serves_api_version_and_static_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".knit"));
        try
        {
            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = true,
            });

            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var version = await http.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/version");
                Assert.NotNull(version);
                Assert.True(version!.ContainsKey("version"));
                Assert.False(string.IsNullOrEmpty(version["version"].GetString()));

                var index = await http.GetAsync("/");
                Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_throws_when_root_has_no_knit_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-web-noknit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() =>
                WebApiBuilder.Build([], new WebApiOptions
                {
                    RootPath = root,
                    Port = GetFreePort(),
                    OpenBrowser = false,
                    EnableStaticFiles = false,
                }));
            Assert.Contains(".knit/", ex.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
