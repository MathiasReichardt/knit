using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Core.Discovery;
using Knit.Core.Paths;

namespace Knit.Core.Config;

/// <summary>Which layer a config value came from.</summary>
public enum ConfigValueSource
{
    Default,
    Global,
    Repo,
    Local,
}

/// <param name="Value">Effective value, rendered — one rendering for the table, `--json` and the SPA.</param>
/// <param name="Known">False for a key knit does not model: listed, never dropped.</param>
public sealed record ConfigEntry(
    string Name,
    string Value,
    string Default,
    ConfigValueSource Source,
    bool IsSet,
    bool Known,
    bool Settable,
    string Description)
{
    public string SourceLabel => Label(Source);

    public static string Label(ConfigValueSource source) => source switch
    {
        ConfigValueSource.Local => ConfigLoader.LocalFileName,
        ConfigValueSource.Repo => ConfigLoader.FileName,
        ConfigValueSource.Global => ConfigSnapshot.GlobalLabel,
        _ => "default",
    };
}

/// <param name="Label">What the <c>source</c> column prints for this layer.</param>
/// <param name="Path">The file to edit — a source name alone leaves "so where do I change it?" unanswered.</param>
public sealed record ConfigLayerPath(string Label, string Path);

/// <param name="Mounts">
/// The <c>mounts</c> key as rows, since a map does not fit a value column —
/// the same shape the doctor view already renders.
/// </param>
/// <param name="Unreadable">
/// Layer file names that exist but do not parse. Their keys are missing from
/// every value above, so nothing may be reported as fact until this is empty.
/// </param>
/// <param name="IgnoredGlobal">
/// Keys the user-level config declares that apply to no repo — a
/// repo-only key or a relative mount path. Beside the table rather than in it:
/// the row states the effective value, and this states a written value that
/// has none.
/// </param>
/// <param name="Forges">The <c>forges</c> key as rows, one per host, from the layer that wins it.</param>
public sealed record ConfigSnapshot(
    IReadOnlyList<ConfigEntry> Keys,
    IReadOnlyList<MountInfo> Mounts,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<ConfigLayerPath> Layers,
    IReadOnlyList<IgnoredGlobalKey> IgnoredGlobal,
    IReadOnlyList<ForgeRow> Forges)
{
    public const string GlobalLabel = "global";
}

/// <param name="Value">The entry rendered — <c>gitea</c>, or <c>github (web https://…)</c>.</param>
/// <param name="Error">Why the entry doesn't parse, or null.</param>
public sealed record ForgeRow(string Host, string Value, ConfigValueSource Source, string? Error)
{
    public string SourceLabel => ConfigEntry.Label(Source);
}

/// <summary>
/// Config with its provenance attached — the whole point of <c>knit config
/// list</c>. Source detection runs over the raw JSON of the two layers, never
/// over a deserialized <see cref="KnitConfig"/>: the record's constructor
/// defaults make "key absent" and "key set to the default" the same value, so
/// it can never answer "did I set that, or is it the default?".
/// </summary>
public static class ConfigReport
{
    public static ConfigSnapshot Build(string root)
    {
        var knitDir = RepoDiscovery.KnitDir(root);
        var (repoNode, repoBroken) = ConfigLoader.ReadLayer(knitDir, ConfigLoader.FileName);
        var (localNode, localBroken) = ConfigLoader.ReadLayer(knitDir, ConfigLoader.LocalFileName);
        var global = ConfigLoader.ReadGlobal();
        var effective = ConfigLoader.Load(knitDir);

        var unreadable = new List<string>();
        if (global.Unreadable) unreadable.Add(UserPaths.GlobalConfigFile);
        if (repoBroken) unreadable.Add(ConfigLoader.FileName);
        if (localBroken) unreadable.Add(ConfigLoader.LocalFileName);

        var entries = new List<ConfigEntry>();
        foreach (var key in ConfigKeys.All)
        {
            if (!key.Scalar) continue;
            var source = SourceOf(key.Name, global.Node, repoNode, localNode);
            entries.Add(new ConfigEntry(
                key.Name, key.Render(effective), key.Default, source,
                source != ConfigValueSource.Default, Known: true, key.Settable, key.Description));
        }

        // A typo'd `due_soon_day` is inert, not an error — so it has to be
        // visible, or the user reads the table, sees the default, and concludes
        // knit ignores the setting.
        foreach (var (name, node, source) in Raw(localNode, ConfigValueSource.Local)
                     .Concat(Raw(repoNode, ConfigValueSource.Repo))
                     .Concat(Raw(global.Node, ConfigValueSource.Global)))
        {
            if (ConfigKeys.Find(name) is not null) continue;
            if (entries.Any(e => e.Name == name)) continue;
            entries.Add(new ConfigEntry(
                name, node?.ToJsonString() ?? "null", "", source,
                IsSet: true, Known: false, Settable: false,
                "not a key knit knows — a typo, or written by a newer knit"));
        }

        var mounts = MountResolver.Describe(
            root,
            global.Node?.Deserialize(KnitConfigJsonContext.Default.KnitConfig),
            ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.FileName),
            ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.LocalFileName));

        List<ConfigLayerPath> layers =
        [
            new(ConfigSnapshot.GlobalLabel, UserPaths.GlobalConfigFile),
            new(ConfigLoader.FileName, Path.Combine(knitDir, ConfigLoader.FileName)),
            new(ConfigLoader.LocalFileName, Path.Combine(knitDir, ConfigLoader.LocalFileName)),
        ];

        return new ConfigSnapshot(entries, mounts, unreadable, layers, global.Ignored,
            ForgeRows(global.Node, repoNode, localNode));
    }

    /// <summary>Every <c>forges</c> host any layer declares, valued and sourced from the highest one.</summary>
    private static IReadOnlyList<ForgeRow> ForgeRows(JsonObject? global, JsonObject? repo, JsonObject? local)
    {
        var rows = new Dictionary<string, ForgeRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var (layer, source) in new[]
                 {
                     (global, ConfigValueSource.Global), (repo, ConfigValueSource.Repo), (local, ConfigValueSource.Local),
                 })
        {
            if (layer?[Sharing.Forges.Key] is not JsonObject map) continue;
            foreach (var (host, node) in map)
            {
                var entry = Sharing.Forges.FromJson(node, out var error);
                rows[host] = new ForgeRow(host, entry is null ? node?.ToJsonString() ?? "null" : Render(entry), source, error);
            }
        }
        return [.. rows.Values.OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase)];
    }

    private static string Render(Sharing.ForgeEntry e) => e.Web is null ? e.KindText : $"{e.KindText} (web {e.Web})";

    private static IEnumerable<(string Name, JsonNode? Node, ConfigValueSource Source)> Raw(
        JsonObject? layer, ConfigValueSource source) =>
        layer is null
            ? []
            : layer.Select(kv => (kv.Key, kv.Value, source));

    private static ConfigValueSource SourceOf(string name, JsonObject? global, JsonObject? repo, JsonObject? local) =>
        local is not null && local.ContainsKey(name) ? ConfigValueSource.Local
        : repo is not null && repo.ContainsKey(name) ? ConfigValueSource.Repo
        : global is not null && global.ContainsKey(name) ? ConfigValueSource.Global
        : ConfigValueSource.Default;
}
