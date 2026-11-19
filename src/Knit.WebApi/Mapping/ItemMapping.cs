using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Data;
using Knit.WebApi.Contracts;

namespace Knit.WebApi.Mapping;

public static class ItemMapping
{
    /// <summary>
    /// Build a one-shot id → absolute file path map for the whole items
    /// directory. O(N) disk enumeration; reuse it across many ToSummary
    /// calls to avoid quadratic FindFile cost.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildPathMap(string rootPath)
    {
        // Case-insensitive: NanoIDs are stored uppercase but callers look up by
        // the raw frontmatter id, and prefixes/ids resolve case-folded.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var itemsDir = RepoDiscovery.ItemsDir(rootPath);
        foreach (var file in ItemLayout.EnumerateFiles(itemsDir))
        {
            var name = ItemLayout.EntryName(file);
            var id = LinkPath.IdFromFolderName(name);
            if (id is not null && NanoId.IsValid(id))
                map[id] = Path.GetFullPath(file);
        }
        return map;
    }

    public static ItemSummaryDto ToSummary(
        Item item, IdShortener shortener, LinkGraph graph,
        IReadOnlyDictionary<string, string>? pathMap = null,
        string? modified = null)
        => new(
            Id: item.Id,
            ShortId: shortener.Short(item.Id),
            Title: item.Title,
            Type: item.Type.ToWire(),
            State: item.State.ToWire(),
            Assignee: NullIfEmpty(item.Assignee),
            Parent: NullIfEmpty(item.Parent),
            Rank: item.Rank,
            Effort: item.Effort,
            Due: item.Due is { } d ? DueDates.ToWire(d) : null,
            Priority: item.Priority.ToWire(),
            Created: item.Created,
            Blocked: item.IsBlocked,
            BlockedReason: item.BlockedSummary,
            HasChildren: graph.ChildrenOf(item.Id).Count > 0,
            Tags: item.Tags,
            FilePath: pathMap is not null && pathMap.TryGetValue(item.Id, out var p) ? p : string.Empty,
            Modified: modified,
            Slate: NullIfEmpty(item.Slate),
            Health: item.Health?.ToWire(),
            Project: NullIfEmpty(item.Project),
            HeldBy: HeldBy(graph.HeldBy(item.Id), shortener));

    /// <param name="lookup">Resolves ancestors for <see cref="ItemDto.HeldBy"/>;
    /// without one the field stays empty.</param>
    public static ItemDto ToDto(Item item, IdShortener shortener, string rootPath, Func<string, Item?>? lookup = null)
    {
        var path = ResolveFilePath(rootPath, item);
        var modified = path is null
            ? item.Created
            : SafeMtime(path) ?? item.Created;
        return ToDto(item, shortener, modified, path ?? string.Empty) with
        {
            HeldBy = lookup is null ? null : HeldBy(LinkGraph.HeldBy(item, lookup), shortener),
        };
    }

    private static HeldByDto? HeldBy(Item? holder, IdShortener shortener) =>
        holder is null ? null : new HeldByDto(holder.Id, shortener.Short(holder.Id), holder.Title);

    /// <summary>
    /// Overload with precomputed modified timestamp + file path — used by
    /// tests and any caller that already has them in hand.
    /// </summary>
    public static ItemDto ToDto(Item item, IdShortener shortener, DateTimeOffset modified, string filePath = "")
    {
        var sections = BodySections.Split(item.Body)
            .Select(kv => new BodySectionDto(kv.Key, kv.Value))
            .ToList();

        var links = item.LinkRows
            .Select(r => new LinkRowDto(
                Rel: LinkRels.ToWire(r.Rel),
                TargetText: r.TargetText,
                TargetPath: r.TargetPath,
                TargetItemId: LinkPath.ItemIdFromTarget(r.TargetPath),
                Notes: r.Notes))
            .ToList();

        var comments = item.Comments
            .Select(c => new CommentDto(c.Id, c.Author, c.Created, c.Body))
            .ToList();

        return new ItemDto(
            Id: item.Id,
            ShortId: shortener.Short(item.Id),
            Title: item.Title,
            Type: item.Type.ToWire(),
            State: item.State.ToWire(),
            Assignee: NullIfEmpty(item.Assignee),
            Parent: NullIfEmpty(item.Parent),
            Rank: item.Rank,
            Effort: item.Effort,
            Due: item.Due is { } d ? DueDates.ToWire(d) : null,
            Priority: item.Priority.ToWire(),
            Created: item.Created,
            Modified: modified,
            Blocked: item.IsBlocked,
            BlockedReason: NullIfEmpty(item.BlockedReason),
            Summary: item.Summary,
            Feature: item.Feature,
            AgentSkills: item.AgentSkills,
            Tags: item.Tags,
            Design: item.Design,
            Body: item.Body,
            Sections: sections,
            Links: links,
            Comments: comments,
            FilePath: filePath,
            Slate: NullIfEmpty(item.Slate),
            Health: item.Health?.ToWire(),
            Project: NullIfEmpty(item.Project));
    }

    /// <summary>
    /// Build the nested tree from a <see cref="LinkGraph"/>. Items whose
    /// <c>Parent</c> is unset (or points outside the graph) hang under a
    /// synthetic "Unparented" bucket — modelled by returning them as
    /// root-level nodes with no synthetic wrapper; the SPA renders the
    /// Unparented group from <c>Parent == null</c> rows.
    /// </summary>
    public static IReadOnlyList<TreeNodeDto> ToTree(
        LinkGraph graph, IdShortener shortener, IComparer<Item> siblings,
        IReadOnlyDictionary<string, string>? pathMap = null)
    {
        var roots = graph.Roots().OrderBy(i => i, siblings).ToList();
        return roots.Select(r => BuildNode(r, graph, shortener, siblings, pathMap)).ToList();
    }

    private static TreeNodeDto BuildNode(
        Item item, LinkGraph graph, IdShortener shortener, IComparer<Item> siblings,
        IReadOnlyDictionary<string, string>? pathMap)
    {
        var children = graph.ChildrenOf(item.Id)
            .Select(id => graph.Get(id))
            .Where(x => x is not null)
            .Cast<Item>()
            .OrderBy(i => i, siblings)
            .Select(c => BuildNode(c, graph, shortener, siblings, pathMap))
            .ToList();
        return new TreeNodeDto(ToSummary(item, shortener, graph, pathMap), children, Match: true);
    }

    /// <summary>
    /// Build a tree filtered by <paramref name="matchIds"/>:
    /// in <c>context</c> mode every node on the path to a match is
    /// emitted (with <c>Match=false</c> on the ancestors); in
    /// <c>flat</c> mode only matches are emitted, with their closest
    /// matching descendants as children (today's promotion behaviour).
    /// When <paramref name="rootItem"/> is non-null the result is
    /// scoped to that subtree.
    /// </summary>
    public static IReadOnlyList<TreeNodeDto> ToFilteredTree(
        LinkGraph graph,
        IdShortener shortener,
        HashSet<string> matchIds,
        bool flat,
        Item? rootItem,
        IComparer<Item> siblings,
        IReadOnlyDictionary<string, string>? pathMap = null)
    {
        if (flat)
            return rootItem is not null
                ? BuildFlat(rootItem, graph, shortener, matchIds, rootIsScope: true, siblings, pathMap)
                : graph.Roots()
                    .OrderBy(r => r, siblings)
                    .SelectMany(r => BuildFlat(r, graph, shortener, matchIds, rootIsScope: false, siblings, pathMap))
                    .ToList();

        // Context mode: include any node whose subtree contains a match,
        // even if the node itself doesn't match.
        var includeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in matchIds) MarkAncestors(id, graph, includeIds);

        if (rootItem is not null)
        {
            if (!includeIds.Contains(rootItem.Id))
            {
                return BuildFlat(rootItem, graph, shortener, matchIds, rootIsScope: true, siblings, pathMap);
            }
            return [BuildContextNode(rootItem, graph, shortener, matchIds, includeIds, siblings, pathMap)];
        }

        return graph.Roots()
            .Where(r => includeIds.Contains(r.Id))
            .OrderBy(r => r, siblings)
            .Select(r => BuildContextNode(r, graph, shortener, matchIds, includeIds, siblings, pathMap))
            .ToList();
    }

    private static void MarkAncestors(string id, LinkGraph graph, HashSet<string> acc)
    {
        var current = graph.Get(id);
        while (current is not null && acc.Add(current.Id))
        {
            if (string.IsNullOrEmpty(current.Parent)) break;
            current = graph.Get(current.Parent!);
        }
    }

    private static TreeNodeDto BuildContextNode(
        Item item, LinkGraph graph, IdShortener shortener,
        HashSet<string> matchIds, HashSet<string> includeIds, IComparer<Item> siblings,
        IReadOnlyDictionary<string, string>? pathMap)
    {
        var children = graph.ChildrenOf(item.Id)
            .Select(id => graph.Get(id))
            .Where(c => c is not null && includeIds.Contains(c!.Id))
            .Cast<Item>()
            .OrderBy(c => c, siblings)
            .Select(c => BuildContextNode(c, graph, shortener, matchIds, includeIds, siblings, pathMap))
            .ToList();
        return new TreeNodeDto(ToSummary(item, shortener, graph, pathMap), children, Match: matchIds.Contains(item.Id));
    }

    /// <summary>
    /// Flat (promotion) build: include only matches; non-matches expose
    /// their matching descendants in their slot.
    /// </summary>
    private static List<TreeNodeDto> BuildFlat(
        Item item, LinkGraph graph, IdShortener shortener,
        HashSet<string> matchIds, bool rootIsScope, IComparer<Item> siblings,
        IReadOnlyDictionary<string, string>? pathMap)
    {
        if (matchIds.Contains(item.Id))
        {
            var children = graph.ChildrenOf(item.Id)
                .Select(id => graph.Get(id))
                .Where(c => c is not null)
                .Cast<Item>()
                .OrderBy(c => c, siblings)
                .SelectMany(c => BuildFlat(c, graph, shortener, matchIds, rootIsScope: false, siblings, pathMap))
                .ToList();
            return [new TreeNodeDto(ToSummary(item, shortener, graph, pathMap), children, Match: true)];
        }
        return PromotedMatchingDescendants(item, graph, matchIds)
            .OrderBy(i => i, siblings)
            .SelectMany(c => BuildFlat(c, graph, shortener, matchIds, rootIsScope: false, siblings, pathMap))
            .ToList();
    }

    private static IEnumerable<Item> PromotedMatchingDescendants(
        Item nonMatching, LinkGraph graph, HashSet<string> matchIds)
    {
        foreach (var childId in graph.ChildrenOf(nonMatching.Id))
        {
            var child = graph.Get(childId);
            if (child is null) continue;
            if (matchIds.Contains(child.Id))
                yield return child;
            else
                foreach (var deeper in PromotedMatchingDescendants(child, graph, matchIds))
                    yield return deeper;
        }
    }

    public static ContextDto ToContextDto(
        ResolvedItemContext ctx,
        LinkGraph graph,
        IdShortener shortener,
        IReadOnlyDictionary<string, string>? pathMap = null)
    {
        var parentChain = ctx.ParentChain
            .Select(p => ToSummary(p, shortener, graph, pathMap))
            .ToList();

        var kbRefs = ctx.KbRefs
            .Select(k => new KbRefDto(
                k.TargetText,
                k.TargetPath,
                k.Note,
                string.IsNullOrEmpty(k.InheritedFrom) ? null : k.InheritedFrom))
            .ToList();

        var externalRefs = ctx.ExternalRefs
            .Select(x => new ExternalRefDto(
                LinkRels.ToWire(x.Rel),
                x.Text,
                x.Url,
                x.Note,
                string.IsNullOrEmpty(x.InheritedFrom) ? null : x.InheritedFrom))
            .ToList();

        var acceptance = BodySections.ExtractList(ctx.Item.Body, BodySections.AcceptanceHeading)
            .Select(a => new AcceptanceCriterionDto(a.Text, a.Checked))
            .ToList();
        var codePointers = BodySections.ExtractList(ctx.Item.Body, BodySections.PointersHeading)
            .Select(p => p.Text)
            .ToList();

        var blockers = graph.BlockersOf(ctx.Item.Id)
            .Select(id => graph.Get(id))
            .Where(x => x is not null)
            .Cast<Item>()
            .Select(b => new BlockerDto(b.Id, shortener.Short(b.Id), b.Title, b.State.ToWire()))
            .ToList();

        var blocked = ctx.Blocked
            .Select(b =>
            {
                var source = string.IsNullOrEmpty(b.InheritedFrom) ? ctx.Item : graph.Get(b.InheritedFrom) ?? ctx.Item;
                return new BlockedStatusDto(
                    b.Reason,
                    string.IsNullOrEmpty(b.InheritedFrom) ? null : b.InheritedFrom,
                    NullIfEmpty(source.BlockedReason),
                    source.LinkRows.Where(r => r.Rel == LinkRel.BlockedBy)
                        .Select(r => new BlockedByRefDto(LinkPath.ItemIdFromTarget(r.TargetPath), r.TargetText, r.Notes))
                        .ToList());
            })
            .ToList();

        var agentSkills = ctx.AgentSkills
            .Select(s => new AgentSkillRefDto(s.Skill,
                string.IsNullOrEmpty(s.InheritedFrom) ? null : s.InheritedFrom))
            .ToList();

        var design = ctx.Design is null
            ? null
            : new DesignRefDto(
                ctx.Design.Slug,
                Path.Combine(".knit", "designs", $"{ctx.Design.Slug}-design.md"),
                string.IsNullOrEmpty(ctx.Design.InheritedFrom) ? null : ctx.Design.InheritedFrom);

        return new ContextDto(
            Id: ctx.Item.Id,
            ParentChain: parentChain,
            AgentSkills: agentSkills,
            KbRefs: kbRefs,
            ExternalRefs: externalRefs,
            Acceptance: acceptance,
            CodePointers: codePointers,
            Blockers: blockers,
            Blocked: blocked,
            Design: design);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>
    /// Resolve the on-disk file for an item to an absolute path. Returns
    /// null when the file can't be located (mid-rename window, tests).
    /// </summary>
    private static string? ResolveFilePath(string rootPath, Item item)
    {
        try
        {
            var itemsDir = RepoDiscovery.ItemsDir(rootPath);
            var filePath = ItemLayout.FindFile(itemsDir, item.Id);
            return filePath is null ? null : Path.GetFullPath(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeMtime(string path)
    {
        try { return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero); }
        catch { return null; }
    }
}
