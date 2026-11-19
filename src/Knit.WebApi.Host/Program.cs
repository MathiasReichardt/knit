using System.Text.Json;
using System.Text.Json.Serialization;
using Knit.Core.Discovery;
using Knit.WebApi;
using Microsoft.Extensions.Logging;

// Config first, args second: whatever is written last wins, so an explicit
// flag always overrides the file without per-flag bookkeeping.
string? rootPath = null;
int? port = null;
bool? openBrowser = null;
LogLevel? logLevel = null;

var configPath = ArgValue(args, "--config");
if (configPath != null)
{
    var resolved = Path.GetFullPath(configPath);
    if (!File.Exists(resolved))
    {
        Console.Error.WriteLine($"error: --config file not found: {resolved}");
        return 2;
    }
    var cfg = JsonSerializer.Deserialize(File.ReadAllText(resolved), HostConfigContext.Default.HostConfig);
    if (cfg != null)
    {
        if (!string.IsNullOrEmpty(cfg.RootPath)) rootPath = cfg.RootPath;
        port = cfg.Port ?? port;
        openBrowser = cfg.OpenBrowser ?? openBrowser;
    }
    Console.WriteLine($"loaded config: {resolved}");
}

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--root" when i + 1 < args.Length:
            rootPath = args[++i];
            break;
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;
        case "--log-level" when i + 1 < args.Length:
            if (!TryParseLogLevel(args[++i], out var lvl))
            {
                Console.Error.WriteLine($"error: invalid --log-level '{args[i]}'. Use trace|debug|info|warn|error|critical.");
                return 2;
            }
            logLevel = lvl;
            break;
    }
}

// Resolved like `knit web`: a root path is `--root` (taken as given), none is
// the walk from the working directory with its knit root redirect.
var resolution = RepoDiscovery.ResolveDetailed(Environment.CurrentDirectory, rootPath);
if (resolution.Root is null)
{
    Console.Error.WriteLine(
        $"error: no knit repo at {(rootPath is null ? $"or above '{Environment.CurrentDirectory}'" : $"'{rootPath}'")}" +
        (resolution.Redirected ? $" — its knit root '{resolution.OverridePath}' does not resolve" : ""));
    return 2;
}

var options = WebApiOptions.ForRoot(resolution);
options.Port = port ?? 5174;
options.OpenBrowser = openBrowser ?? false;
options.EnableStaticFiles = true;
if (logLevel is { } level) options.MinLogLevel = level;
Console.WriteLine($"knit root: {options.RootPath}" + (options.RedirectedFrom is { } from ? $" (redirected from {from})" : ""));

WebApiBuilder.Build(args, options).Run();
return 0;

static string? ArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

static bool TryParseLogLevel(string raw, out LogLevel level)
{
    level = LogLevel.Warning;
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

internal sealed class HostConfig
{
    [JsonPropertyName("rootPath")] public string? RootPath { get; set; }
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("openBrowser")] public bool? OpenBrowser { get; set; }
}

[JsonSerializable(typeof(HostConfig))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HostConfigContext : JsonSerializerContext;
