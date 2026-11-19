using System.Text.Json.Nodes;

namespace Knit.Core.Config;

/// <summary>
/// Result of turning a command-line string into a config value: either a JSON
/// node ready to write into a layer, or the reason it was refused.
/// </summary>
public sealed record ConfigParse(JsonNode? Value, string? Error);

/// <summary>
/// The metadata about one config key that <see cref="KnitConfig"/> cannot
/// express — how to render it, how to parse a string into it, whether it may
/// be set at all. <see cref="KnitConfig"/> stays the typed record every caller
/// reads a value off; this sits beside it, and
/// <c>ConfigKeysCoverRecordTests</c> asserts the two never drift.
/// </summary>
/// <param name="Scalar">
/// False for a key whose value is not a single line — only <c>mounts</c>, which
/// is reported as rows (name → path → resolves) rather than squeezed into a
/// value column.
/// </param>
/// <param name="NotSettable">
/// Why <c>knit config set</c> refuses the key, or null when it is settable.
/// </param>
/// <param name="Scope">
/// Whether the key may be set once for the person, or only ever per repo.
/// It has no default on purpose: a key added without a scope must
/// fail to compile rather than quietly inherit into every repo on the machine.
/// </param>
public sealed record ConfigKey(
    string Name,
    string Description,
    bool Scalar,
    string? NotSettable,
    ConfigScope Scope,
    Func<KnitConfig, string> Render,
    Func<string, ConfigParse> Parse)
{
    public bool Settable => NotSettable is null;

    public bool Inheritable => Scope != ConfigScope.RepoOnly;

    /// <summary>What the key reads as in a repo that never set it.</summary>
    public string Default => Render(ConfigKeys.Defaults);
}

/// <summary>
/// Whether a key may live in the user-level config. Repo-only means the
/// value describes <i>this</i> repo, so inheriting it from a machine-wide file
/// would state something false about every other one.
/// </summary>
public enum ConfigScope
{
    Inheritable,
    RepoOnly,
    /// <summary>
    /// Describes the person: inheritable, and a write that names no layer
    /// goes to the uncommitted <c>config.local.json</c> (as <c>git config</c>
    /// does) — in the committed <c>config.json</c> it would beat every cloner's
    /// own value and make them record as you.
    /// </summary>
    Personal,
}

/// <summary>
/// Every key knit models, in the order <c>knit config list</c> prints them.
/// A new config key lands with its entry here — that is what makes it visible
/// the day it ships instead of years later.
/// </summary>
public static class ConfigKeys
{
    internal static readonly KnitConfig Defaults = new();

    /// <summary>
    /// Why a write that records who made it is refused. Named once, for
    /// the CLI and the web API alike: identity is the person's, so the fix is
    /// the machine-wide one.
    /// </summary>
    public const string NoHandle =
        "no handle set — knit needs your name for this; set it once for every repo on this machine with " +
        "'knit config set --global handle <you>', or run 'knit config setup'";

    public static readonly IReadOnlyList<ConfigKey> All =
    [
        new(
            "format_version",
            "On-disk format this repo was written for",
            Scalar: true,
            NotSettable: "it is the on-disk contract, stamped by `knit init` and by migrations — not a preference",
            Scope: ConfigScope.RepoOnly,
            c => c.FormatVersion.ToString(),
            _ => new ConfigParse(null, null)),
        new(
            "handle",
            "Your short name in assignee fields (`knit add --mine`, `--assignee`)",
            Scalar: true,
            NotSettable: null,
            Scope: ConfigScope.Personal,
            c => c.Handle ?? "",
            ParseHandle),
        new(
            "display",
            "Your display name, shown where a handle would read as a login",
            Scalar: true,
            NotSettable: null,
            Scope: ConfigScope.Personal,
            c => c.Display,
            ParseNonEmpty),
        new(
            "repo_name",
            "Name the web UI shows for this repo — the repo's folder name when unset",
            Scalar: true,
            NotSettable: null,
            Scope: ConfigScope.RepoOnly,
            c => c.RepoName ?? "",
            ParseNonEmpty),
        new(
            "status_report.path",
            "Where `knit status report` writes the status page, relative to the repo root",
            Scalar: true,
            NotSettable: null,
            // The page links into this repo's .knit/; another repo's location means nothing here.
            Scope: ConfigScope.RepoOnly,
            c => c.StatusReportPath,
            ParseRepoRelativePath),
        new(
            "status_report.stale_days",
            "Days old the status page may get before status, verify and the web UI flag it",
            Scalar: true,
            NotSettable: null,
            // How old the team's page may get is the team's agreement.
            Scope: ConfigScope.RepoOnly,
            c => c.StatusReportStaleDays.ToString(),
            ParseNonNegativeInt),
        new(
            "due_soon_days",
            "Days from today within which a dated item counts as due soon",
            Scalar: true,
            NotSettable: null,
            Scope: ConfigScope.Inheritable,
            c => c.DueSoonDays.ToString(),
            ParseNonNegativeInt),
        new(
            "initial_state",
            "State a newly created item starts in (`todo` or `draft`)",
            Scalar: true,
            NotSettable: null,
            // Inheritable so "work I create is proposed, not committed" can be
            // set once per machine — the motivating case is an agent, whose
            // caution is a property of who is typing, not of the repo. A repo
            // that states its own rule still outranks the machine default.
            Scope: ConfigScope.Inheritable,
            c => c.InitialState,
            ParseInitialState),
        new(
            "tree_sort",
            "Sibling order in tree views: `rank` (the plan) or `state` (done on top, dismissed last)",
            Scalar: true,
            NotSettable: null,
            // A reading preference — set once per person, a repo may still state its own.
            Scope: ConfigScope.Inheritable,
            c => c.TreeSort,
            ParseTreeSort),
        new(
            "mounts",
            "Named read-only directories KB sources may point into",
            Scalar: false,
            NotSettable: "mounts are declared with `knit mount add <name> <path>` / `knit mount set`",
            // Inheritable, with a restriction the global layer enforces: only
            // absolute or `~`-anchored paths there. A repo-relative mount
            // (the portable form) would resolve somewhere different in every
            // repo — that, not the key itself, was the reason to ban it.
            Scope: ConfigScope.Inheritable,
            _ => "",
            _ => new ConfigParse(null, null)),
        new(
            "knit_root",
            "Where this checkout's knit state lives — another repo's root, or `.` for its own on purpose",
            Scalar: true,
            NotSettable: "it is set with `knit root <path>` / `--worktree` / `--local` / `--off`, which checks the target first",
            // A path on this machine for this checkout: false for every other
            // repo, and only honoured from config.local.json.
            Scope: ConfigScope.RepoOnly,
            c => c.KnitRoot ?? "",
            _ => new ConfigParse(null, null)),
        new(
            Requirements.Key,
            "Local-only settings every checkout must fill in, each with a hint — `knit config setup` asks for them",
            Scalar: false,
            NotSettable: $"declare one setting at a time: `knit config set {Requirements.Key}.<setting> \"<hint>\"`",
            // The project's contract with its checkouts; a machine-wide one
            // would demand the setting of every repo.
            Scope: ConfigScope.RepoOnly,
            _ => "",
            _ => new ConfigParse(null, null)),
        new(
            Sharing.Forges.Key,
            "Which forge runs an origin host, for share links — github.com, gitlab.com, codeberg.org, gitea.com and Azure DevOps are built in",
            Scalar: false,
            NotSettable: $"set one host at a time: `knit config set {Sharing.Forges.Key}.<host> <kind> [--web <url>]`",
            // A self-hosted forge is per company, so answering once per machine
            // is the point; a team that commits it spares everyone the question.
            Scope: ConfigScope.Inheritable,
            _ => "",
            _ => new ConfigParse(null, null)),
    ];

    /// <summary>
    /// A name addressing one entry of a map key — <c>requires.&lt;setting&gt;</c>,
    /// <c>forges.&lt;host&gt;</c> — split at the first dot only, so a dotted
    /// host needs no quoting (git's <c>url.&lt;base&gt;.insteadOf</c> is the
    /// same convention). Null for every other name.
    /// </summary>
    public static (string Key, string Entry)? MapEntry(string name)
    {
        foreach (var key in (string[])[Requirements.Key, Sharing.Forges.Key])
            if (name.Length > key.Length + 1 && name.StartsWith(key + ".", StringComparison.Ordinal))
                return (key, name[(key.Length + 1)..]);
        return null;
    }

    public static ConfigKey? Find(string name) =>
        All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.Ordinal));

    public static string NameList => string.Join(", ", All.Select(k => k.Name));

    /// <summary>Keys that must never arrive from the user-level config.</summary>
    public static bool IsRepoOnly(string name) => Find(name) is { Scope: ConfigScope.RepoOnly };

    public static string RepoOnlyNames =>
        string.Join(", ", All.Where(k => k.Scope == ConfigScope.RepoOnly).Select(k => k.Name));

    private static ConfigParse ParseHandle(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0) return new ConfigParse(null, "must not be empty — use `knit config unset handle` to clear it");
        return value.Any(char.IsWhiteSpace)
            ? new ConfigParse(null, "must not contain whitespace — it is matched against assignee fields")
            : new ConfigParse(JsonValue.Create(value), null);
    }

    private static ConfigParse ParseNonEmpty(string raw)
    {
        var value = raw.Trim();
        return value.Length == 0
            ? new ConfigParse(null, "must not be empty — use `knit config unset` to clear it")
            : new ConfigParse(JsonValue.Create(value), null);
    }

    private static ConfigParse ParseRepoRelativePath(string raw)
    {
        var value = raw.Trim().Replace('\\', '/');
        if (value.Length == 0) return new ConfigParse(null, "must not be empty — use `knit config unset` to clear it");
        if (Path.IsPathRooted(value)) return new ConfigParse(null, "must be relative to the repo root");
        var anchor = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));
        var full = Path.GetFullPath(Path.Combine(anchor, value));
        return Paths.PathContainment.IsContained(anchor, full) && full != anchor
            ? new ConfigParse(JsonValue.Create(value), null)
            : new ConfigParse(null, "must stay inside the repo — the page links into .knit/ from its own folder");
    }

    private static ConfigParse ParseInitialState(string raw)
    {
        var v = raw.Trim().ToLowerInvariant();
        return v is "todo" or "draft"
            ? new ConfigParse(JsonValue.Create(v), null)
            : new ConfigParse(null, "must be `todo` or `draft`");
    }

    private static ConfigParse ParseTreeSort(string raw) =>
        Domain.SiblingOrder.TryParse(raw, out var sort)
            ? new ConfigParse(JsonValue.Create(Domain.SiblingOrder.ToWire(sort)), null)
            : new ConfigParse(null, "must be `rank` or `state`");

    private static ConfigParse ParseNonNegativeInt(string raw) =>
        int.TryParse(raw.Trim(), out var n) && n >= 0
            ? new ConfigParse(JsonValue.Create(n), null)
            : new ConfigParse(null, "must be a whole number of days, 0 or more");
}
