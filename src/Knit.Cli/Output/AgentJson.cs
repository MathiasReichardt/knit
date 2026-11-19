using System.Text.Json;
using System.Text.Json.Serialization;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Cli.Output;

/// <summary>
/// JSON shapes emitted by --json modes. Uses source-gen for AOT.
/// </summary>
public sealed record JsonItem(
    string Id,
    string Type,
    string State,
    string Title,
    string? Assignee,
    string? Parent,
    string? Rank,
    string? Effort,
    string? Due,
    string Priority,
    string Created,
    string Modified,
    IReadOnlyList<JsonLink> Links,
    IReadOnlyList<string> Tags,
    string? Folder,
    string? Blocked = null,
    string? ReplacedReason = null,
    // Kept last so positional callers stay stable.
    string? Slate = null,
    string? Health = null,
    string? Project = null,
    string? Design = null,
    // Derived, never written: the slate the item actually belongs to once
    // read-time inheritance is applied (own `slate:` or nearest ancestor's).
    string? EffectiveSlate = null,
    // Only on a call that closed the item: open items still blocked by it.
    IReadOnlyList<JsonBlockedDependent>? BlockedDependents = null,
    // The blocked ancestor holding this item (list / tree / show); absent
    // when nothing above holds it.
    string? HeldBy = null,
    // Derived like EffectiveSlate: `project/slug` of the own Milestone link,
    // else the nearest ancestor's.
    string? EffectiveMilestone = null);

/// <summary>
/// An open item still blocked by the item just closed. <c>remaining</c> is what
/// else blocks it; <c>null</c> ⇔ the closed item was its only impediment.
/// </summary>
public sealed record JsonBlockedDependent(string Id, string Title, string? Remaining);

/// <summary>
/// Shape returned by <c>knit dismiss --cascade --json</c>: the parent that
/// was dismissed plus every open descendant the cascade swept along.
/// </summary>
public sealed record JsonDismissResult(JsonItem Item, IReadOnlyList<JsonItem> Cascaded);

/// <summary>
/// Shape returned by <c>knit delete --json</c>: the ids that were removed
/// from disk. <c>cascaded</c> contains any descendants the cascade swept
/// along (empty when no children existed or --cascade wasn't passed).
/// </summary>
public sealed record JsonDeleteResult(string Id, IReadOnlyList<string> Cascaded);

public sealed record JsonComment(
    string Id,
    string Author,
    string Created,
    string Body);

public sealed record JsonCommentResult(
    string ItemId,
    JsonComment Comment);

public sealed record JsonLink(string Rel, string TargetText, string TargetPath, string? Notes);

public sealed record JsonError(string Message, int Code, string? ErrorCode);

public sealed record JsonItemList(IReadOnlyList<JsonItem> Items);

public sealed record JsonTreeNode(JsonItem Item, IReadOnlyList<JsonTreeNode> Children, bool Match = true);

public sealed record JsonTree(IReadOnlyList<JsonTreeNode> Roots, IReadOnlyList<JsonItem> Unparented);

/// <summary>
/// Shape returned by <c>knit timeline --json</c>: the full schedule graph —
/// nodes (with per-item risk + rollup span), blocker edges, and the resolved
/// window — so agents get the dependency data without ASCII parsing.
/// A <c>ghost</c> node is an at-risk blocker outside the rendered set
/// (dropped by a filter or the window) kept so the at-risk signal never
/// silently vanishes.
/// </summary>
public sealed record JsonTimeline(
    string From,
    string? To,
    IReadOnlyList<JsonTimelineNode> Nodes,
    IReadOnlyList<JsonTimelineEdge> Edges,
    IReadOnlyList<JsonTimelineMilestone> Milestones);

/// <summary>
/// A dated milestone rendered as a vertical guide + flag on the timeline
/// overlay. Only milestones with a target date appear; the
/// <c>--milestone</c> filter narrows the set to one.
/// </summary>
public sealed record JsonTimelineMilestone(
    string Project,
    string Slug,
    string Title,
    string Target,
    string State);

public sealed record JsonTimelineNode(
    string Id,
    string Title,
    string Type,
    string State,
    string? Due,
    string Priority,
    string? Effort,
    string? Assignee,
    string? Blocked,
    string? Milestone,
    string? ParentPath,
    bool AtRisk,
    string? ImpliedDeadline,
    IReadOnlyList<string> AtRiskBlockers,
    string? SpanStart,
    string? SpanEnd,
    bool SubtreeAtRisk,
    IReadOnlyList<JsonTimelineRolled> RolledUp,
    bool Ghost);

/// <summary>
/// A descendant folded into a coarser timeline node. Carried so the
/// SPA / an agent can list what a rollup anchor hides without a second query.
/// </summary>
public sealed record JsonTimelineRolled(
    string Id,
    string Title,
    string Type,
    string? Due);

public sealed record JsonTimelineEdge(string From, string To, string Kind, bool AtRisk);

/// <summary>
/// One search hit. <c>Kind</c> is <c>item | kb | design | project</c> and says
/// how to read <c>Id</c>: a full item uuid, a <c>bucket:slug</c> KB ref, a
/// <c>design:slug</c> ref, or a <c>&lt;project&gt;/&lt;doc-path&gt;</c> address.
/// <c>Type</c>/<c>State</c> are item-only and omitted for doc hits.
/// </summary>
public sealed record JsonSearchHit(
    string Kind, string Id, string? Type, string? State, string Title, string Snippet);

public sealed record JsonSearchResults(IReadOnlyList<JsonSearchHit> Hits);

/// <summary>
/// `status --json`. <c>Percent</c> is done over non-dismissed, and
/// <c>blocked</c>/<c>overdue</c> carry the full attention lists (the human
/// rendering truncates them; the machine shape never does).
/// </summary>
public sealed record JsonStatus(
    string Scope,
    int Total,
    int Done,
    int InProgress,
    int Review,
    int Todo,
    int Draft,
    int Dismissed,
    int Blocked,
    int Overdue,
    int Unassigned,
    int Percent,
    string? Health,
    IReadOnlyList<JsonAssigneeLoad> ByAssignee,
    IReadOnlyList<JsonItem> BlockedItems,
    IReadOnlyList<JsonItem> OverdueItems,
    string? RedirectedFrom,
    bool LocalOverride,
    JsonStatusPage? StatusPage);

/// <summary>The generated status page's age; present only when the page exists.</summary>
public sealed record JsonStatusPage(string Path, string? Generated, bool Stale);

public sealed record JsonAssigneeLoad(
    string? Assignee, int Total, int Done, int InProgress, int Todo, int Blocked, int Overdue);

/// <summary>`reorder --by --json`: the items whose rank changes; <c>Total</c> counts every item in scope.</summary>
public sealed record JsonReorderResult(bool DryRun, int Moved, int Total, int Groups, IReadOnlyList<JsonRankMove> Items);

public sealed record JsonRankMove(string Id, string Title, string? From, string To);

/// <summary>`status report --json`: the absolute file it wrote.</summary>
public sealed record JsonStatusReport(string Path, bool Written);

/// <summary>`handles --json`: one row per distinct spelling; <c>Me</c> marks the configured handle.</summary>
public sealed record JsonHandle(string Handle, int Assigned, int Comments, bool Me);

/// <summary>`conventions --json`: one row per scope, missing files included so the path is known.</summary>
public sealed record JsonConventions(string Scope, string Path, bool Exists, string? Text);

[JsonSerializable(typeof(JsonItem))]
[JsonSerializable(typeof(JsonReorderResult))]
[JsonSerializable(typeof(JsonBlockedDependent))]
[JsonSerializable(typeof(JsonItemList))]
[JsonSerializable(typeof(JsonTree))]
[JsonSerializable(typeof(JsonSearchResults))]
[JsonSerializable(typeof(JsonTimeline))]
[JsonSerializable(typeof(JsonError))]
[JsonSerializable(typeof(JsonCommentResult))]
[JsonSerializable(typeof(JsonDismissResult))]
[JsonSerializable(typeof(JsonDeleteResult))]
[JsonSerializable(typeof(JsonStatus))]
[JsonSerializable(typeof(JsonStatusReport))]
[JsonSerializable(typeof(List<JsonHandle>))]
[JsonSerializable(typeof(JsonConventions))]
[JsonSerializable(typeof(List<JsonConventions>))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
public partial class AgentJsonContext : JsonSerializerContext;

public static class AgentMapper
{
    public static JsonItem Map(
        Item item,
        CliContext ctx,
        string? folder = null,
        string? replacedReason = null,
        string? effectiveSlate = null,
        IReadOnlyList<BlockedDependent>? blockedDependents = null,
        string? heldBy = null) => new(
        Id: item.Id,
        Type: item.Type.ToWire(),
        State: item.State.ToWire(),
        Title: item.Title,
        Assignee: string.IsNullOrEmpty(item.Assignee) ? null : item.Assignee,
        Parent: string.IsNullOrEmpty(item.Parent) ? null : item.Parent,
        Rank: item.Rank,
        Effort: string.IsNullOrEmpty(item.Effort) ? null : item.Effort,
        Due: item.Due is { } due ? DueDates.ToWire(due) : null,
        Priority: item.Priority.ToWire(),
        Created: item.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        Modified: ctx.Modified(item).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        Links: item.LinkRows.Select(r => new JsonLink(
            LinkRels.ToWire(r.Rel), r.TargetText, r.TargetPath, r.Notes)).ToList(),
        Tags: item.Tags,
        Folder: folder,
        Blocked: item.BlockedSummary,
        ReplacedReason: replacedReason,
        Slate: string.IsNullOrEmpty(item.Slate) ? null : item.Slate,
        Health: item.Health?.ToWire(),
        Project: string.IsNullOrEmpty(item.Project) ? null : item.Project,
        Design: string.IsNullOrEmpty(item.Design) ? null : item.Design,
        EffectiveSlate: string.IsNullOrEmpty(effectiveSlate) ? null : effectiveSlate,
        BlockedDependents: blockedDependents?
            .Select(d => new JsonBlockedDependent(d.Item.Id, d.Item.Title, d.Remaining))
            .ToList(),
        HeldBy: heldBy,
        EffectiveMilestone: ctx.EffectiveMilestone(item.Id));

    /// <summary>
    /// Mtime of the item file on disk. Falls back to <c>Created</c> if the
    /// file can't be located (e.g. mid-rename window or tests).
    /// </summary>
    internal static DateTimeOffset ResolveModified(string rootPath, Item item)
    {
        try
        {
            var itemsDir = RepoDiscovery.ItemsDir(rootPath);
            var filePath = ItemLayout.FindFile(itemsDir, item.Id);
            if (filePath is null) return item.Created;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
        }
        catch
        {
            return item.Created;
        }
    }
}
