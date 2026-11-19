namespace Knit.Core.Domain;

public static class Hierarchy
{
    public static IReadOnlyList<ItemType> AllTypes =>
        [ItemType.Project, ItemType.Epic, ItemType.Feature, ItemType.Bug, ItemType.Task, ItemType.Todo];

    public static bool ParentAllowed(ItemType child, ItemType? parent) => (child, parent) switch
    {
        // Project is the top altitude — root only, no sub-projects.
        (ItemType.Project, null) => true,
        (ItemType.Project, _) => false,
        (ItemType.Epic, ItemType.Project) => true,
        (ItemType.Epic, null) => true,
        (ItemType.Epic, _) => false,
        (ItemType.Feature, ItemType.Project) => true,
        (ItemType.Feature, ItemType.Epic) => true,
        (ItemType.Feature, null) => true,
        (ItemType.Feature, _) => false,
        (ItemType.Bug, ItemType.Project) => true,
        (ItemType.Bug, ItemType.Epic) => true,
        (ItemType.Bug, null) => true,
        (ItemType.Bug, _) => false,
        (ItemType.Task, ItemType.Project) => true,
        (ItemType.Task, ItemType.Feature) => true,
        (ItemType.Task, ItemType.Bug) => true,
        (ItemType.Task, ItemType.Epic) => true,
        (ItemType.Task, ItemType.Task) => true,
        (ItemType.Task, null) => true,
        // No loose todos: every todo sits under a project (directly, or
        // through a todo chain that itself roots at a project).
        (ItemType.Todo, ItemType.Project) => true,
        (ItemType.Todo, ItemType.Todo) => true,
        _ => false,
    };

    /// <summary>
    /// Every parent a <paramref name="child"/> may sit under, in altitude
    /// order. Excludes the root case — see <see cref="AllowedAtRoot"/>.
    /// </summary>
    public static IReadOnlyList<ItemType> AllowedParents(ItemType child) =>
        AllTypes.Where(p => ParentAllowed(child, p)).ToList();

    public static bool AllowedAtRoot(ItemType child) => ParentAllowed(child, null);

    /// <summary>
    /// Why a placement was refused, plus the rule that refused it. Lives here
    /// rather than at the call sites so the four refusal points (add, retype,
    /// and the two API mutation endpoints) can't drift apart or invent their
    /// own phrasing for the same rule.
    /// </summary>
    public static string ExplainRefusal(ItemType child, ItemType? parent)
    {
        var what = parent is { } p
            ? $"a {child.ToWire()} cannot be a child of a {p.ToWire()}"
            : $"a {child.ToWire()} cannot sit at the repo root";

        var allowed = AllowedParents(child).Select(t => t.ToWire()).ToList();
        if (AllowedAtRoot(child)) allowed.Add("the repo root");

        return allowed.Count == 0
            ? what
            : $"{what} — a {child.ToWire()} belongs under {Join(allowed)}";
    }

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " or " + parts[^1];
}
