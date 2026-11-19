using Microsoft.AspNetCore.Http;

namespace Knit.Cli.Tests;

/// <summary>
/// HTTP client for an in-process API host. Like the SPA, it sends
/// <c>Content-Type: application/json</c> on every write, also a bodyless one —
/// the API refuses a write without it.
/// </summary>
internal static class TestApi
{
    public static HttpClient Client(int port) =>
        new(new JsonWrites { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };

    private sealed class JsonWrites : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.Method.Method;
            if (request.Content is null
                && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
            {
                request.Content = new ByteArrayContent([]);
                request.Content.Headers.ContentType = new("application/json");
            }
            return base.SendAsync(request, ct);
        }
    }
}
