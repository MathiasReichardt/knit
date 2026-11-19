using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Knit.Core.Config;
using Knit.WebApi;
using Microsoft.AspNetCore.Http;

namespace Knit.Cli.Tests;

public class RequestOriginGuardTests : IDisposable
{
    private const string Json = "application/json";
    private static readonly BindAddress Wide = Parse("0.0.0.0");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-origin-{Guid.NewGuid():N}");

    public RequestOriginGuardTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "items"));
        File.WriteAllText(Path.Combine(_root, ".knit", ConfigLoader.FileName), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("localhost:5199")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1:5199")]
    [InlineData("127.0.0.2")]
    [InlineData("[::1]:5199")]
    public void A_loopback_bind_answers_a_loopback_host(string host) =>
        Assert.Null(Reject(BindAddress.Loopback, "GET", host, origin: null));

    [Theory]
    [InlineData("evil.example:5199")]
    [InlineData("localhost.evil.example")]
    [InlineData("192.168.1.10:5199")]
    [InlineData("")]
    public void A_loopback_bind_refuses_any_other_host(string host) =>
        Assert.NotNull(Reject(BindAddress.Loopback, "GET", host, origin: null));

    [Fact]
    public void A_wide_bind_answers_any_host() =>
        Assert.Null(Reject(Wide, "GET", "devbox:5199", origin: null));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void A_foreign_origin_may_not_write(string method)
    {
        Assert.NotNull(Reject(BindAddress.Loopback, method, "127.0.0.1:5199", "http://evil.example"));
        Assert.NotNull(Reject(Wide, method, "devbox:5199", "http://evil.example"));
        Assert.NotNull(Reject(BindAddress.Loopback, method, "127.0.0.1:5199", "null"));
    }

    [Fact]
    public void The_own_origin_and_no_origin_may_write()
    {
        Assert.Null(Reject(BindAddress.Loopback, "POST", "127.0.0.1:5199", "http://127.0.0.1:5199"));
        Assert.Null(Reject(Wide, "POST", "devbox:5199", "http://DevBox:5199"));
        Assert.Null(Reject(BindAddress.Loopback, "POST", "127.0.0.1:5199", origin: null));
    }

    [Theory]
    [InlineData("POST", "application/x-www-form-urlencoded")]
    [InlineData("POST", "multipart/form-data; boundary=x")]
    [InlineData("POST", "text/plain")]
    [InlineData("POST", null)]
    [InlineData("DELETE", null)]
    [InlineData("PUT", "application/jsonx")]
    public void A_write_without_json_content_type_is_refused_even_without_origin(string method, string? contentType)
    {
        var refusal = Reject(BindAddress.Loopback, method, "127.0.0.1:5199", origin: null, contentType);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, refusal?.Status);
        Assert.NotNull(Reject(BindAddress.Loopback, method, "127.0.0.1:5199", "http://127.0.0.1:5199", contentType));
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("Application/JSON")]
    public void A_json_content_type_with_parameters_may_write(string contentType) =>
        Assert.Null(Reject(BindAddress.Loopback, "DELETE", "127.0.0.1:5199", origin: null, contentType));

    [Fact]
    public void A_read_needs_no_content_type() =>
        Assert.Null(Reject(BindAddress.Loopback, "GET", "127.0.0.1:5199", origin: null, contentType: null));

    [Fact]
    public void A_foreign_origin_may_read() =>
        Assert.Null(Reject(BindAddress.Loopback, "GET", "127.0.0.1:5199", "http://evil.example"));

    [Fact]
    public async Task The_host_refuses_a_rebound_host_and_a_cross_site_write()
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = _root,
            Port = port,
            EnableStaticFiles = false,
            EnableWatch = false,
            EnableSearch = false,
        });
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            var ok = await http.GetAsync("/api/version");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(WebApiBuilder.ContentSecurityPolicy, ok.Headers.GetValues("Content-Security-Policy").Single());

            using var rebound = new HttpRequestMessage(HttpMethod.Get, "/api/version");
            rebound.Headers.Host = $"evil.example:{port}";
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(rebound)).StatusCode);

            using var crossSite = new HttpRequestMessage(HttpMethod.Put, "/api/root/name")
            {
                Content = JsonContent.Create(new { name = "pwned" }),
            };
            crossSite.Headers.Add("Origin", "http://evil.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(crossSite)).StatusCode);
            Assert.DoesNotContain("pwned", File.ReadAllText(Path.Combine(_root, ".knit", ConfigLoader.FileName)));

            using var formPost = new HttpRequestMessage(HttpMethod.Put, "/api/root/name")
            {
                Content = new StringContent("{\"name\":\"pwned\"}", System.Text.Encoding.UTF8, "text/plain"),
            };
            var refused = await http.SendAsync(formPost);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
            Assert.Contains("json_required", await refused.Content.ReadAsStringAsync());
            Assert.DoesNotContain("pwned", File.ReadAllText(Path.Combine(_root, ".knit", ConfigLoader.FileName)));

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await http.DeleteAsync("/api/cache")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static RequestOriginGuard.Refusal? Reject(
        BindAddress bind, string method, string host, string? origin, string? contentType = Json) =>
        RequestOriginGuard.Reject(bind, "http", method, new HostString(host), origin, contentType);

    private static BindAddress Parse(string raw)
    {
        Assert.True(BindAddress.TryParse(raw, out var bind));
        return bind;
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
