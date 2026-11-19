using System.Text.Json;
using System.Text.Json.Nodes;
using Knit.Core.IO;
using Knit.Core.Paths;

namespace Knit.Core.Config;

/// <param name="Name">The key as written in the global file — <c>mounts.docs</c> for one mount.</param>
public sealed record IgnoredGlobalKey(string Name, string Reason);

/// <param name="Node">The global layer with everything unusable already removed.</param>
/// <param name="Ignored">
/// What was removed and why. Never silently dropped: a key the user wrote into
/// their own config and that applies nowhere has to be visible, or they will
/// keep believing it applies (the "nothing vanishes" rule, applied to config).
/// </param>
public sealed record GlobalLayer(JsonObject? Node, bool Unreadable, IReadOnlyList<IgnoredGlobalKey> Ignored);

public static class ConfigLoader
{
    public const string FileName = "config.json";

    /// <summary>
    /// Uncommitted per-machine overlay beside <see cref="FileName"/>, same
    /// schema, merged over it. Listed in
    /// <c>RepoDiscovery.KnitGitignoreEntries</c>.
    /// </summary>
    public const string LocalFileName = "config.local.json";

    /// <summary>
    /// The user-level layer under the repo's own — one place to set
    /// <c>handle</c> / <c>display</c> and a shared docs mount for every repo on
    /// the machine. Sanitized, so repo-only keys and non-absolute mount paths
    /// written there apply nowhere and are reported instead.
    /// </summary>
    public static GlobalLayer ReadGlobal()
    {
        var path = UserPaths.GlobalConfigFile;
        if (!File.Exists(path)) return new GlobalLayer(null, false, []);
        var node = ReadNode(path);
        return node is null ? new GlobalLayer(null, true, []) : SanitizeGlobal(node);
    }

    /// <summary>
    /// What of a user-level layer may apply to a repo — the rule itself,
    /// separated from reading the file so it can be stated and tested without
    /// a machine-wide path in the way.
    /// </summary>
    public static GlobalLayer SanitizeGlobal(JsonObject node)
    {
        var ignored = new List<IgnoredGlobalKey>();
        var clean = (JsonObject)node.DeepClone();
        foreach (var name in node.Select(kv => kv.Key).ToList())
        {
            if (!ConfigKeys.IsRepoOnly(name)) continue;
            clean.Remove(name);
            ignored.Add(new IgnoredGlobalKey(name,
                "repo-only — it describes one repo's on-disk contract, so a machine-wide value would be false everywhere else"));
        }

        if (clean["mounts"] is JsonObject mounts)
        {
            foreach (var (mountName, value) in mounts.ToList())
            {
                var declared = value?.GetValue<string>() ?? "";
                if (MountResolver.IsGlobalMountPath(declared)) continue;
                mounts.Remove(mountName);
                ignored.Add(new IgnoredGlobalKey($"mounts.{mountName}",
                    $"'{declared}' is relative — a global mount path must be absolute or `~`-anchored, " +
                    "or it resolves to a different directory in every repo"));
            }
            if (mounts.Count == 0) clean.Remove("mounts");
        }

        return new GlobalLayer(clean, false, ignored);
    }

    /// <summary>
    /// The **effective** config: <see cref="LocalFileName"/> merged over
    /// <see cref="FileName"/>, and the all-defaults record when neither file
    /// exists — "no config" means defaults everywhere, in one place, instead of
    /// a nullable every caller re-decides. Callers that need to know
    /// which file a value came from (the mount report, <c>knit config list</c>)
    /// read the layers instead — see <see cref="TryLoadLayer"/> and
    /// <see cref="ReadLayer"/>.
    /// </summary>
    public static KnitConfig Load(string knitDir)
    {
        // defaults -> global -> repo -> repo-local: a committed repo setting
        // beats a personal global (the team's policy wins in the team's repo),
        // and the uncommitted overlay still beats everything.
        var merged = Merge(
            Merge(WithoutEmptyHandle(ReadGlobal().Node), WithoutEmptyHandle(ReadNode(Path.Combine(knitDir, FileName)))),
            WithoutEmptyHandle(ReadNode(Path.Combine(knitDir, LocalFileName))));
        if (merged is null) return new KnitConfig();
        try
        {
            return merged.Deserialize(KnitConfigJsonContext.Default.KnitConfig) ?? new KnitConfig();
        }
        catch
        {
            return new KnitConfig();
        }
    }

    /// <summary>
    /// One layer as raw JSON, telling absent from broken — <see cref="Load"/>
    /// cannot: a typo'd <c>config.json</c> would otherwise read as a confident
    /// table of defaults, which is the "nothing under <c>.knit/</c> is silently
    /// hidden" rule broken for config. <c>Unreadable</c> is true when the file
    /// is there and its JSON is not.
    /// </summary>
    public static (JsonObject? Node, bool Unreadable) ReadLayer(string knitDir, string fileName)
        => ReadFile(Path.Combine(knitDir, fileName));

    /// <summary>The same, for a layer that is not inside a repo — the global file.</summary>
    public static (JsonObject? Node, bool Unreadable) ReadFile(string path)
    {
        if (!File.Exists(path)) return (null, false);
        var node = ReadNode(path);
        return (node, node is null);
    }

    /// <summary>One layer on its own — <see cref="FileName"/> or <see cref="LocalFileName"/>.</summary>
    public static KnitConfig? TryLoadLayer(string knitDir, string fileName)
    {
        var node = ReadNode(Path.Combine(knitDir, fileName));
        if (node is null) return null;
        try
        {
            return node.Deserialize(KnitConfigJsonContext.Default.KnitConfig);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string knitDir, KnitConfig config) => Save(knitDir, config, FileName);

    public static void Save(string knitDir, KnitConfig config, string fileName)
    {
        Directory.CreateDirectory(knitDir);
        AtomicFile.WriteAllBytes(Path.Combine(knitDir, fileName),
            JsonSerializer.SerializeToUtf8Bytes(config, KnitConfigJsonContext.Default.KnitConfig));
    }

    /// <summary>
    /// One layer as raw JSON for a writer to change and save back — an empty
    /// object when the file is missing, null when it is there and does not
    /// parse: saving over it would replace everything in it with the one key
    /// just written. Report <see cref="Unwritable"/> instead.
    ///
    /// Raw rather than the record: saving a deserialized
    /// <see cref="KnitConfig"/> into <see cref="LocalFileName"/> would
    /// materialise every defaulted key, and the overlay would then override
    /// settings the user never touched locally. It also preserves keys knit
    /// does not model.
    /// </summary>
    public static JsonObject? ReadForWrite(string path)
    {
        var (node, unreadable) = ReadFile(path);
        return unreadable ? null : node ?? new JsonObject();
    }

    public static string Unwritable(string label) =>
        $"cannot write {label} — its JSON does not parse, and writing would replace it; fix the file first";

    public static void SaveRawLayer(string knitDir, string fileName, JsonObject obj)
        => SaveRawFile(Path.Combine(knitDir, fileName), obj);

    public static void SaveRawFile(string path, JsonObject obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            obj.WriteTo(writer);
        AtomicFile.WriteAllBytes(path, buffer.ToArray());
    }

    // Merging happens on the JSON, not on the record: the record's ctor
    // defaults make "key absent" and "key set to the default" the same value
    // once deserialized (see KnitConfig's own remark), so a local file could
    // never leave a repo value alone. Merging nodes also means a new setting
    // needs no merge code — only the maps `mounts` and `forges` have a per-key rule.
    private static JsonObject? Merge(JsonObject? repo, JsonObject? local)
    {
        if (local is null) return repo;
        if (repo is null) return local;

        var merged = (JsonObject)repo.DeepClone();
        foreach (var (key, value) in local)
        {
            if (key is "mounts" or Sharing.Forges.Key && value is JsonObject localMap
                && merged[key] is JsonObject repoMap)
            {
                // Merged per name — a local override repoints one mount (or
                // one forge host) without having to restate the others.
                foreach (var (name, entry) in localMap)
                    repoMap[name] = entry?.DeepClone();
                continue;
            }
            merged[key] = value?.DeepClone();
        }
        return merged;
    }

    // A hand-edited `"handle": ""` means unset, and must not shadow a handle
    // from a lower layer — `set` refuses it and `unset` is the way to clear.
    private static JsonObject? WithoutEmptyHandle(JsonObject? layer)
    {
        if (layer?["handle"] is JsonValue v && v.TryGetValue<string>(out var h) && h.Trim().Length == 0)
            layer.Remove("handle");
        return layer;
    }

    private static JsonObject? ReadNode(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(AtomicFile.ReadAllText(path)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }
}
