using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Knit.Core.Config;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// The on-disk <c>format_version</c> stamp and its forward-safety
/// guard. There is no migration engine: a newer repo is refused, never guessed at.
/// </summary>
[Collection(nameof(CliCollection))]
public class FormatVersionTests
{
    [Fact]
    public void Init_stamps_format_version_1()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        var raw = File.ReadAllText(Path.Combine(root.Path, ".knit", "config.json"));
        Assert.Contains("\"format_version\": 1", raw);

        var config = ConfigLoader.Load(Path.Combine(root.Path, ".knit"));
        Assert.Equal(1, config!.FormatVersion);
    }

    [Fact]
    public void An_omitted_key_takes_its_default()
    {
        using var root = new TempKnitRoot();
        var knitDir = Path.Combine(root.Path, ".knit");
        Directory.CreateDirectory(knitDir);
        File.WriteAllText(Path.Combine(knitDir, "config.json"), "{\"handle\":\"a\",\"display\":\"A\"}");

        var loaded = ConfigLoader.Load(knitDir);
        Assert.Equal(1, loaded.FormatVersion);
        Assert.Equal(3, loaded.DueSoonDays);
    }

    [Fact]
    public void An_explicit_zero_is_not_confused_with_an_omitted_key()
    {
        using var root = new TempKnitRoot();
        var knitDir = Path.Combine(root.Path, ".knit");
        Directory.CreateDirectory(knitDir);
        File.WriteAllText(
            Path.Combine(knitDir, "config.json"),
            "{\"handle\":\"a\",\"display\":\"A\",\"due_soon_days\":0}");

        // 0 is a real setting — "amber only on the due day itself" — so it must
        // survive the load rather than being read back as the default.
        Assert.Equal(0, ConfigLoader.Load(knitDir).DueSoonDays);
    }

    [Fact]
    public void A_newer_repo_is_refused_with_the_upgrade_message()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Stamp(root, 99);

        var r = root.Run("list");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("format_version 99", r.Stderr);
        Assert.Contains("upgrade knit", r.Stderr);
        Assert.Equal("", r.Stdout);
    }

    [Fact]
    public void A_matching_repo_runs_normally()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Stamp(root, FormatVersion.Current);

        Assert.Equal(0, root.Run("list").ExitCode);
    }

    [Fact]
    public void Version_stays_usable_in_a_newer_repo()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Stamp(root, 99);

        // it is what you run to find out which knit you have — refusing it
        // would leave the "upgrade knit" message unactionable
        var r = root.Run("version");
        Assert.Equal(0, r.ExitCode);
        Assert.NotEmpty(r.Stdout.Trim());
    }

    [Fact]
    public async Task Version_endpoint_reports_repo_and_supported_format()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Stamp(root, 99);

        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = root.Path,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
        });
        await app.StartAsync();
        try
        {
            using var http = TestApi.Client(port);
            var opts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var dto = await http.GetFromJsonAsync<VersionDto>("/api/version", opts);

            Assert.Equal(99, dto!.FormatVersion);
            Assert.Equal(FormatVersion.Current, dto.SupportedFormatVersion);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static void Stamp(TempKnitRoot root, int version)
    {
        var knitDir = Path.Combine(root.Path, ".knit");
        var config = ConfigLoader.Load(knitDir);
        ConfigLoader.Save(knitDir, config with { FormatVersion = version });
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
