using Knit.WebApi;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// The bind address is a security boundary, not a convenience: the API is
/// unauthenticated and has mutators, so the default must stay loopback and a
/// wider bind must be something the user typed. Reaching the server from
/// off-box isn't testable here, so the wide binds are asserted on the url the
/// host is configured with rather than by dialing it.
/// </summary>
public class WebBindAddressTests
{
    [Fact]
    public void The_default_bind_is_loopback()
    {
        Assert.True(BindAddress.TryParse(null, out var bind));
        Assert.Equal("127.0.0.1", bind.Value);
        Assert.True(bind.IsLoopback);
        Assert.Equal(BindAddress.Loopback, new WebApiOptions().Bind);
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1", true)]
    [InlineData("127.0.0.2", "127.0.0.2", true)]
    [InlineData("::1", "::1", true)]
    [InlineData("0.0.0.0", "0.0.0.0", false)]
    [InlineData("192.168.1.10", "192.168.1.10", false)]
    public void Parsing_normalizes_and_classifies(string raw, string value, bool loopback)
    {
        Assert.True(BindAddress.TryParse(raw, out var bind));
        Assert.Equal(value, bind.Value);
        Assert.Equal(loopback, bind.IsLoopback);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("256.0.0.1")]
    [InlineData("not an address")]
    public void A_hostname_or_garbage_is_rejected(string raw) =>
        Assert.False(BindAddress.TryParse(raw, out _));

    [Fact]
    public void An_ipv6_literal_is_bracketed_in_the_url()
    {
        Assert.True(BindAddress.TryParse("::", out var bind));
        Assert.Equal("http://[::]:5174", bind.ListenUrl(5174));
        Assert.Equal("http://127.0.0.1:5174", bind.BrowsableUrl(5174));
    }

    [Fact]
    public void A_wildcard_bind_hands_the_browser_loopback_instead()
    {
        Assert.True(BindAddress.TryParse("0.0.0.0", out var bind));
        Assert.True(bind.IsWildcard);
        Assert.Equal("http://0.0.0.0:5174", bind.ListenUrl(5174));
        Assert.Equal("http://127.0.0.1:5174", bind.BrowsableUrl(5174));
    }

    [Fact]
    public void The_host_listens_on_the_configured_address()
    {
        Assert.Equal("http://127.0.0.1:5174", ConfiguredUrl(BindAddress.Loopback));
        Assert.True(BindAddress.TryParse("0.0.0.0", out var any));
        Assert.Equal("http://0.0.0.0:5174", ConfiguredUrl(any));
    }

    /// <summary>
    /// Builds the host without starting it — no socket is bound, so this
    /// stays firewall-safe on a wildcard address.
    /// </summary>
    private static string? ConfiguredUrl(BindAddress bind)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-bind-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".knit"));
        try
        {
            using var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Bind = bind,
                Port = 5174,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
            });
            return app.Configuration["urls"];
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
