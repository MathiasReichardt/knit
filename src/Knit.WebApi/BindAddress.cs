using System.Net;
using System.Net.Sockets;

namespace Knit.WebApi;

/// <summary>
/// The address the web host binds. Loopback by default: the API exposes
/// mutators, so a wider bind hands out write access to anything that can
/// reach the box — callers check <see cref="IsLoopback"/> and warn before
/// that happens.
/// </summary>
public sealed record BindAddress(string Value, IPAddress Address, bool IsLoopback)
{
    public static BindAddress Loopback { get; } = new("127.0.0.1", IPAddress.Loopback, true);

    /// <summary>
    /// Accepts an IP literal or <c>localhost</c> (normalized to
    /// <c>127.0.0.1</c>); blank means the default. Hostnames are rejected —
    /// a bind address is a local interface, not a name to resolve.
    /// </summary>
    public static bool TryParse(string? raw, out BindAddress bind)
    {
        bind = Loopback;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var text = raw.Trim().Trim('[', ']');
        if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(text, out var ip)) return false;
        bind = new BindAddress(text, ip, IPAddress.IsLoopback(ip));
        return true;
    }

    /// <summary>Kestrel binds every interface on the wildcard addresses.</summary>
    public bool IsWildcard => Address.Equals(IPAddress.Any) || Address.Equals(IPAddress.IPv6Any);

    /// <summary>Url to listen on. IPv6 literals need brackets in a url.</summary>
    public string ListenUrl(int port) =>
        Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"http://[{Value}]:{port}" : $"http://{Value}:{port}";

    /// <summary>
    /// Url to hand a browser. A wildcard bind is not dialable, so it
    /// degrades to loopback — which is one of the interfaces it covers.
    /// </summary>
    public string BrowsableUrl(int port) => IsWildcard ? $"http://127.0.0.1:{port}" : ListenUrl(port);
}
