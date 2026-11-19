using System.Text.Json.Nodes;

namespace Knit.Core.Config;

public enum ConfigWriteFailure
{
    None,
    /// <summary>An unknown, unsettable or repo-only-in-global key, or a value the key refuses.</summary>
    Invalid,
    /// <summary>The layer file exists and does not parse; writing would replace it.</summary>
    Unreadable,
    /// <summary>An unset of a key the layer does not hold.</summary>
    NotSet,
}

public sealed record ConfigWriteResult(ConfigWriteFailure Failure, string? Error)
{
    public static readonly ConfigWriteResult Ok = new(ConfigWriteFailure.None, null);

    public bool Succeeded => Failure == ConfigWriteFailure.None;
}

/// <summary>
/// One key into or out of one config layer — the rules <c>knit config
/// set</c>/<c>unset</c> and the web API's rename both go through, so the two
/// cannot disagree about what a valid write is.
/// </summary>
/// <remarks>
/// Raw layer, never a deserialized <see cref="KnitConfig"/>: saving the record
/// materialises every defaulted key, and the overlay would then override
/// settings the user never touched.
/// </remarks>
public static class ConfigWriter
{
    /// <param name="path">The layer file.</param>
    /// <param name="label">How messages name the layer.</param>
    /// <param name="global">The layer is the user-level config, which refuses repo-only keys.</param>
    public static ConfigWriteResult Set(string path, string label, bool global, string name, string value)
    {
        if (Lookup(name) is { } unknown) return unknown;
        var key = ConfigKeys.Find(name)!;
        // Refused rather than written and then ignored on read — but the read
        // side still reports a hand-edited one, since nothing stops a text
        // editor.
        if (global && !key.Inheritable)
            return Invalid(
                $"'{name}' is repo-only — it describes one repo, so it cannot be set for the machine " +
                $"(repo-only keys: {ConfigKeys.RepoOnlyNames})");
        if (!key.Settable) return Invalid($"'{name}' cannot be set — {key.NotSettable}");

        var parsed = key.Parse(value);
        if (parsed.Error is not null) return Invalid($"invalid value for '{name}' — {parsed.Error}");

        if (ConfigLoader.ReadForWrite(path) is not { } node) return UnreadableResult(label);
        node[name] = parsed.Value;
        ConfigLoader.SaveRawFile(path, node);
        return ConfigWriteResult.Ok;
    }

    public static ConfigWriteResult Unset(string path, string label, string name)
    {
        if (Lookup(name) is { } unknown) return unknown;
        if (ConfigLoader.ReadForWrite(path) is not { } node) return UnreadableResult(label);
        if (!node.Remove(name)) return new ConfigWriteResult(ConfigWriteFailure.NotSet, $"'{name}' is not set in {label}");
        ConfigLoader.SaveRawFile(path, node);
        return ConfigWriteResult.Ok;
    }

    /// <summary>One <c>forges.&lt;host&gt;</c> entry (SL2); the kind and web base are validated here.</summary>
    public static ConfigWriteResult SetForge(string path, string label, string host, string kind, string? web)
    {
        if (HostError(host) is { } bad) return Invalid(bad);
        if (Sharing.Forges.TryParse(kind, web, out var error) is not { } entry)
            return Invalid($"invalid value for '{Sharing.Forges.Key}.{host}' — {error}");
        if (ConfigLoader.ReadForWrite(path) is not { } node) return UnreadableResult(label);
        if (node[Sharing.Forges.Key] is not JsonObject map)
        {
            map = new JsonObject();
            node[Sharing.Forges.Key] = map;
        }
        // A host differing only in case is the same host; keep one entry.
        foreach (var other in map.Select(kv => kv.Key).Where(k => string.Equals(k, host, StringComparison.OrdinalIgnoreCase)).ToList())
            map.Remove(other);
        map[host.ToLowerInvariant()] = Sharing.Forges.ToJson(entry);
        ConfigLoader.SaveRawFile(path, node);
        return ConfigWriteResult.Ok;
    }

    public static ConfigWriteResult UnsetForge(string path, string label, string host)
    {
        if (ConfigLoader.ReadForWrite(path) is not { } node) return UnreadableResult(label);
        var match = node[Sharing.Forges.Key] is JsonObject map
            ? map.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, host, StringComparison.OrdinalIgnoreCase))
            : null;
        if (match is null)
            return new ConfigWriteResult(ConfigWriteFailure.NotSet, $"'{Sharing.Forges.Key}.{host}' is not set in {label}");
        var forges = (JsonObject)node[Sharing.Forges.Key]!;
        forges.Remove(match);
        if (forges.Count == 0) node.Remove(Sharing.Forges.Key);
        ConfigLoader.SaveRawFile(path, node);
        return ConfigWriteResult.Ok;
    }

    private static string? HostError(string host) =>
        host.Length == 0 || host.Any(c => char.IsWhiteSpace(c) || c is '/' or ':' or '@')
            ? $"'{host}' is not a host — name it as the origin does, e.g. git.example.com"
            : null;

    private static ConfigWriteResult? Lookup(string name) =>
        ConfigKeys.Find(name) is null
            ? Invalid($"unknown config key '{name}' — known keys: {ConfigKeys.NameList}")
            : null;

    private static ConfigWriteResult Invalid(string error) => new(ConfigWriteFailure.Invalid, error);

    private static ConfigWriteResult UnreadableResult(string label) =>
        new(ConfigWriteFailure.Unreadable, ConfigLoader.Unwritable(label));
}
