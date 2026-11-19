using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// What closing items leaves behind, for every surface that closes one (CLI
/// `done` / `dismiss` / `set state=`, the web PATCH and dismiss endpoints).
/// </summary>
public static class CloseEffects
{
    /// <summary>
    /// Open items still blocked by each item the call closed (open → closed
    /// only; `done` on a dismissed item closes nothing). Nothing is resolved
    /// for them: a blocker can be done and the impediment still stand. Call
    /// after every write, so a cascade that also closed a dependent doesn't
    /// report it.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<BlockedDependent>> BlockedDependents(
        ItemStore store, IEnumerable<(Item Before, Item After)> changes)
    {
        var closed = changes
            .Where(c => LinkGraph.IsOpen(c.Before) && !LinkGraph.IsOpen(c.After))
            .Select(c => c.After.Id)
            .ToList();
        if (closed.Count == 0) return [];
        var graph = new LinkGraph(store.AllItems());
        return closed.ToDictionary(id => id, graph.BlockedDependentsOf);
    }
}
