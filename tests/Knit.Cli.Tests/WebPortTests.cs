using System.Net;
using System.Net.Sockets;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class WebPortTests
{
    [Fact]
    public void An_explicit_port_that_is_taken_fails_instead_of_moving()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        try
        {
            var port = ((IPEndPoint)occupier.LocalEndpoint).Port;

            var r = root.Run("web", "--port", port.ToString(), "--no-open");

            Assert.Equal(4, r.ExitCode);
            Assert.Contains($"port {port} is already in use", r.Stderr);
            Assert.DoesNotContain("listening", r.Stdout);
        }
        finally
        {
            occupier.Stop();
        }
    }
}
