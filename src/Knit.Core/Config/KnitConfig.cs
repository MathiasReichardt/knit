using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Knit.Core.Config;

/// <summary>
/// Repo-local settings (<c>.knit/config.json</c>).
///
/// Positional on purpose: property initializers do <b>not</b> survive
/// source-generated deserialization, so a config predating a field loaded as
/// <c>0</c> and "key absent" became indistinguishable from "key set to 0".
/// Constructor parameter defaults are honored for missing members, which keeps
/// an explicit <c>due_soon_days: 0</c> (warn only on the due day itself) a
/// setting a user can actually express.
/// </summary>
/// <param name="FormatVersion">
/// On-disk format version — see <see cref="Config.FormatVersion"/>. Absent in
/// repos initialized before the stamp existed; those are format 1.
/// </param>
/// <param name="DueSoonDays">
/// Window (in days from today) within which a dated item counts as "due soon" —
/// drives the amber emphasis in list/tree and the timeline warning band.
/// </param>
/// <param name="InitialState">
/// State a newly created item starts in — <c>todo</c> (default) or
/// <c>draft</c>, for repos that want new work reviewed before anyone picks it
/// up. `knit add --draft` / `--todo` overrides it per call.
/// </param>
/// <param name="Mounts">
/// Named read-only directories outside the repo that KB sources may point
/// into, keyed by mount name — see <see cref="MountResolver"/>. Values are
/// repo-root-relative so a committed config stays portable. Absent means "no
/// mounts"; a same-named entry in <c>config.local.json</c> wins.
/// </param>
/// <param name="Handle">
/// Null when unset — never <c>""</c>, which <see cref="ConfigLoader.Load"/>
/// normalises away. Commands that write a name refuse without one.
/// </param>
/// <param name="Requires">
/// Local-only settings this project expects every checkout to fill in,
/// keyed by setting (<c>knit_root</c>, <c>mounts.&lt;name&gt;</c>) with a
/// one-line hint as the value. Declares the requirement, never the value —
/// see <see cref="Requirements"/>.
/// </param>
/// <param name="RepoName">
/// What the web UI calls this repo. Null when unset — the UI then falls back to
/// the repo's folder name.
/// </param>
/// <param name="KnitRoot">
/// The root redirect — honoured only from the checkout's
/// <c>config.local.json</c> and read by discovery, not through this record
/// (see <c>RepoDiscovery.KnitRootKey</c>). Here so the key is listed.
/// </param>
/// <param name="StatusReportPath">
/// Where <c>knit status report</c> and the web UI write the status page,
/// relative to the knit root and inside the repo.
/// </param>
/// <param name="StatusReportStaleDays">
/// How many days old the status page may get before <c>knit status</c>,
/// <c>knit verify</c> and the web UI say so. Only a page that exists is checked.
/// </param>
/// <param name="Forges">
/// Which forge runs an origin host, keyed by host: a kind string or
/// <c>{ "kind", "web" }</c> — see <see cref="Sharing.Forges"/>. Read raw and
/// validated where a link is built, so a bad entry is reported, not dropped.
/// </param>
/// <param name="TreeSort">
/// How siblings are ordered in <c>tree</c>, <c>list --sort tree</c> and the web
/// tree — <c>rank</c> (default) or <c>state</c> (done on top, dismissed last).
/// <c>tree --sort</c> and the web UI toggle override it per view.
/// </param>
public sealed record KnitConfig(
    [property: JsonPropertyName("format_version")] int FormatVersion = Config.FormatVersion.Current,
    [property: JsonPropertyName("handle")] string? Handle = null,
    [property: JsonPropertyName("display")] string Display = "",
    [property: JsonPropertyName("due_soon_days")] int DueSoonDays = 3,
    [property: JsonPropertyName("initial_state")] string InitialState = "todo",
    [property: JsonPropertyName("mounts")] Dictionary<string, string>? Mounts = null,
    [property: JsonPropertyName("knit_root")] string? KnitRoot = null,
    [property: JsonPropertyName("requires")] Dictionary<string, string>? Requires = null,
    [property: JsonPropertyName("repo_name")] string? RepoName = null,
    [property: JsonPropertyName("status_report.path")] string StatusReportPath = "project-status.md",
    [property: JsonPropertyName("status_report.stale_days")] int StatusReportStaleDays = 1,
    [property: JsonPropertyName("forges")] Dictionary<string, JsonNode?>? Forges = null,
    [property: JsonPropertyName("tree_sort")] string TreeSort = "rank");

[JsonSerializable(typeof(KnitConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class KnitConfigJsonContext : JsonSerializerContext;
