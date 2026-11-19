using Knit.Core.Discovery;

namespace Knit.Core.Config;

/// <param name="Setting">What is required — <c>knit_root</c> or <c>mounts.&lt;name&gt;</c>.</param>
/// <param name="Hint">The project's one line on what to obtain and where it goes.</param>
public sealed record Requirement(string Setting, string Hint, bool Met);

/// <summary>
/// The committed half of a local-only setting: <c>config.json</c> says
/// <i>that</i> this project expects a value on every checkout, and the value
/// itself only ever comes from <c>config.local.json</c>. Without the
/// declaration a fresh clone walks to its own <c>.knit/</c> and nobody is told.
/// </summary>
public static class Requirements
{
    public const string Key = "requires";
    public const string MountPrefix = "mounts.";

    public const string SettingRule = "`knit_root` or `mounts.<name>`";

    public static bool IsKnownSetting(string setting) =>
        setting == RepoDiscovery.KnitRootKey
        || (setting.StartsWith(MountPrefix, StringComparison.Ordinal)
            && MountResolver.IsValidName(setting[MountPrefix.Length..]));

    /// <summary>
    /// What the checkout declares, and whether each is met. <c>knit_root</c> is
    /// the checkout's own (a path, or <c>.</c> for local on purpose; an override
    /// answers it for the call); a mount is met when the effective root declares
    /// it in any layer. A setting knit does not know is left out — it can never
    /// be filled, and refusing on it would lock the repo.
    /// </summary>
    public static IReadOnlyList<Requirement> Check(RepoDiscovery.RootResolution r)
    {
        if (r.Checkout is null) return [];
        var declared = ConfigLoader.Load(RepoDiscovery.KnitDir(r.Checkout)).Requires;
        if (declared is null || declared.Count == 0) return [];

        var mounts = r.Root is null ? null : ConfigLoader.Load(RepoDiscovery.KnitDir(r.Root)).Mounts;
        var result = new List<Requirement>();
        foreach (var (setting, hint) in declared.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!IsKnownSetting(setting)) continue;
            var met = setting == RepoDiscovery.KnitRootKey
                ? r.KnitRoot is not null || r.Origin is RepoDiscovery.RootOrigin.Explicit or RepoDiscovery.RootOrigin.Environment
                : mounts?.ContainsKey(setting[MountPrefix.Length..]) == true;
            result.Add(new Requirement(setting, hint, met));
        }
        return result;
    }

    /// <summary>A declared <c>knit_root</c> answered with <c>.</c> — local, and visibly so.</summary>
    public static bool KnitRootOverriddenLocally(RepoDiscovery.RootResolution r) =>
        r.KnitRoot == RepoDiscovery.LocalKnitRoot
        && Check(r).Any(q => q.Setting == RepoDiscovery.KnitRootKey);
}
