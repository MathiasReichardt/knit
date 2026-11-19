using Knit.Core.Domain;

namespace Knit.WebApi.Queries;

public enum SortKey { Tree, Rank, Type, State, Assignee, Created, Due, Priority }
public enum SortDir { Asc, Desc }
public enum TreeFilterMode { Context, Flat }
public enum BlockedFilter { Any, Only, Hide }

internal sealed class ItemQuery
{
    public HashSet<ItemType>? Types;
    public HashSet<ItemType>? ExcludeTypes;
    public HashSet<ItemState>? States;
    public HashSet<ItemState>? ExcludeStates;
    public bool Open;
    public string? Assignee;
    public bool Mine;
    public bool Unassigned;
    public string? Under;
    public bool Ready;
    public string? Query;
    public bool Unparented;
    public BlockedFilter Blocked = BlockedFilter.Any;
    public bool Overdue;
    public DateOnly? DueBefore;
    public DateOnly? DueAfter;
    public Priority? MinPriority;
    // Item must carry every tag (AND), case-insensitive — mirrors the CLI
    // `--tag` semantics.
    public HashSet<string>? Tags;
    // Matched on the *effective* (inherited) slate — own value, else the
    // nearest ancestor's. Mirrors the CLI `--slate` / `--no-slate`.
    public string? Slate;
    public bool NoSlate;
    // Milestone ref, raw (<slug> or <project>/<slug>) — resolved by the
    // runner via MilestoneResolver so ambiguity errors surface as 400s.
    public string? Milestone;
    // Project doc-folder slug — containment scope (item's nearest
    // project-type ancestor points here), not a link. Validated by the
    // runner against .knit/projects/ so an unknown slug 400s.
    public string? Project;
    public SortKey SortKey = SortKey.Rank;
    public SortDir SortDir = SortDir.Asc;
    public TreeFilterMode FilterMode = TreeFilterMode.Context;
    // `treeSort=` as sent; null = the `tree_sort` config value. Resolved into
    // TreeSort by ItemQueryRunner.ResolveTreeSort before a tree is built.
    public string? TreeSortParam;
    public TreeSort TreeSort = TreeSort.Rank;

    public bool HasAnyMatchFilter =>
        (Types?.Count > 0) || (ExcludeTypes?.Count > 0) ||
        (States?.Count > 0) || (ExcludeStates?.Count > 0) ||
        Open || !string.IsNullOrEmpty(Assignee) || Mine || Unassigned ||
        Ready || !string.IsNullOrEmpty(Query) || Unparented ||
        Blocked != BlockedFilter.Any ||
        Overdue || DueBefore is not null || DueAfter is not null ||
        MinPriority is not null || (Tags?.Count > 0) ||
        !string.IsNullOrEmpty(Slate) || NoSlate || !string.IsNullOrEmpty(Milestone) ||
        !string.IsNullOrEmpty(Project);
}
