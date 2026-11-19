using Knit.Core.Domain;

namespace Knit.Data;

public static class TreeOrder
{
    /// <summary>
    /// Depth-first hierarchy order, each sibling group sorted by
    /// <paramref name="sort"/>, unparented items (parent set but unresolved)
    /// appended last. Maps item id to its position — what <c>--sort tree</c>
    /// sorts a flat list by, so list and tree read in the same order.
    /// </summary>
    public static Dictionary<string, int> Index(LinkGraph graph, TreeSort sort)
    {
        var cmp = SiblingOrder.Comparer(sort);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var counter = 0;
        void Walk(Item item)
        {
            order[item.Id] = counter++;
            foreach (var c in graph.ChildrenOf(item.Id)
                .Select(graph.Get)
                .OfType<Item>()
                .OrderBy(c => c, cmp))
            {
                Walk(c);
            }
        }
        foreach (var root in graph.Roots().OrderBy(r => r, cmp))
            Walk(root);
        foreach (var orphan in graph.Items
            .Where(i => !string.IsNullOrEmpty(i.Parent) && graph.Get(i.Parent!) is null)
            .OrderBy(i => i, cmp))
        {
            order.TryAdd(orphan.Id, counter++);
        }
        return order;
    }
}
