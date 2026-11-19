using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using Knit.Cli.Output;
using Knit.Core.Discovery;
using Knit.WebApi;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class WebCommand
{
    private const int DefaultPort = 5174;

    public static Command Build()
    {
        var cmd = new Command("web", "Run the local web UI in-process");
        var rootOpt = new Option<string?>("--root") { Description = "Repo root containing .knit/ (default: found like every other command, knit root redirect included)" };
        var portOpt = new Option<int?>("--port") { Description = $"Port to bind; fails if taken (default {DefaultPort}, which falls back to a free port if taken)" };
        var hostOpt = new Option<string?>("--host") { Description = "Bind address (default 127.0.0.1). 0.0.0.0 exposes the UI — and its mutators — to the network" };
        var randomOpt = new Option<bool>("--random") { Description = "Bind a random free port" };
        var openOpt = new Option<bool>("--open") { Description = "Open the browser (default)", DefaultValueFactory = _ => true };
        var noOpenOpt = new Option<bool>("--no-open") { Description = "Suppress opening the browser" };
        var noWatchOpt = new Option<bool>("--no-watch") { Description = "Disable file-watcher / SSE (manual refresh in the SPA)" };
        var logLevelOpt = new Option<string?>("--log-level") { Description = "Min ASP.NET log level: trace|debug|info|warn|error|critical (default warn)" };
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(portOpt);
        cmd.Options.Add(hostOpt);
        cmd.Options.Add(randomOpt);
        cmd.Options.Add(openOpt);
        cmd.Options.Add(noOpenOpt);
        cmd.Options.Add(noWatchOpt);
        cmd.Options.Add(logLevelOpt);

        cmd.SetAction(parse =>
        {
            var root = parse.GetValue(rootOpt);
            var port = parse.GetValue(portOpt);
            var random = parse.GetValue(randomOpt);
            var noOpen = parse.GetValue(noOpenOpt);
            var noWatch = parse.GetValue(noWatchOpt);
            var logLevelRaw = parse.GetValue(logLevelOpt);
            if (!TryParseLogLevel(logLevelRaw, out var logLevel))
                return Out.Error($"invalid --log-level '{logLevelRaw}'. Use trace|debug|info|warn|error|critical.", ErrorCode.BadArgument);
            var hostRaw = parse.GetValue(hostOpt);
            if (!BindAddress.TryParse(hostRaw, out var bind))
                return Out.Error($"invalid --host '{hostRaw}'. Use an IP address, 'localhost', or 0.0.0.0 for every interface.", ErrorCode.BadArgument);
            return Run(root, bind, port, random, openBrowser: !noOpen, enableWatch: !noWatch, logLevel: logLevel);
        });
        return cmd;
    }

    private static bool TryParseLogLevel(string? raw, out LogLevel level)
    {
        level = LogLevel.Warning;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "trace": level = LogLevel.Trace; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "info": case "information": level = LogLevel.Information; return true;
            case "warn": case "warning": level = LogLevel.Warning; return true;
            case "error": level = LogLevel.Error; return true;
            case "critical": level = LogLevel.Critical; return true;
            case "none": level = LogLevel.None; return true;
            default: return false;
        }
    }

    private static int Run(string? root, BindAddress bind, int? port, bool random, bool openBrowser, bool enableWatch, LogLevel logLevel)
    {
        // Same resolution as every other command, redirect included — the
        // SPA must show the state the CLI writes to, not the checkout's own.
        var cwd = Directory.GetCurrentDirectory();
        var resolution = RepoDiscovery.ResolveDetailed(cwd, root);
        if (resolution.Root is null) return CliContext.NoRepoError(root, json: false);
        // Once, at startup, to a person — worth saying where the UI's state lives.
        RootNotice.Announce(resolution, cwd, verbose: true);

        var probe = bind.IsWildcard ? IPAddress.Loopback : bind.Address;
        int chosen;
        if (random) chosen = GetFreePort(probe);
        else if (port is { } explicitPort)
        {
            // An asked-for port is a promise to whoever bookmarked or scripted it.
            if (!IsFree(probe, explicitPort))
                return Out.Error($"port {explicitPort} is already in use. Pick another --port, or use --random.", ErrorCode.Conflict);
            chosen = explicitPort;
        }
        else chosen = IsFree(probe, DefaultPort) ? DefaultPort : GetFreePort(probe);

        var options = WebApiOptions.ForRoot(resolution);
        options.Bind = bind;
        options.Port = chosen;
        options.OpenBrowser = openBrowser;
        options.EnableStaticFiles = true;
        options.EnableWatch = enableWatch;
        options.MinLogLevel = logLevel;

        var app = WebApiBuilder.Build([], options);
        AnsiConsole.MarkupLine($"[green]knit web[/] listening on [cyan]{bind.ListenUrl(chosen)}[/]");
        if (!bind.IsLoopback) WarnNonLoopback(bind, chosen);

        app.Run();
        return 0;
    }

    /// <summary>
    /// A non-loopback bind is a write grant, not a read one — every mutator
    /// endpoint is unauthenticated, so anything that can reach the port can
    /// edit the repo.
    /// </summary>
    private static void WarnNonLoopback(BindAddress bind, int port)
    {
        var stderr = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
        var reach = bind.IsWildcard ? "every network interface" : $"{bind.Value}";
        stderr.MarkupLine(
            $"[yellow]warning:[/] bound to {reach} on port {port}. The API is unauthenticated and has " +
            "mutators — anyone who can reach this port can edit the repo. Use 127.0.0.1 unless you mean to share it.");
    }

    private static int GetFreePort(IPAddress on)
    {
        var listener = new TcpListener(on, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool IsFree(IPAddress on, int port)
    {
        try
        {
            var listener = new TcpListener(on, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

}
