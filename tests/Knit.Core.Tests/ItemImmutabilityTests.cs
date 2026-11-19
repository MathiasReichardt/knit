using System.Reflection;
using System.Runtime.CompilerServices;
using Knit.Core.Domain;

namespace Knit.Core.Tests;

/// <summary>
/// The server's item cache hands one parsed <see cref="Item"/> to every
/// request, so an item must not be changeable in place: a setter or a mutable
/// collection on it would let one request leak into the next.
/// </summary>
public class ItemImmutabilityTests
{
    public static TheoryData<Type> Reachable()
    {
        var data = new TheoryData<Type>();
        foreach (var t in ReachableTypes(typeof(Item))) data.Add(t);
        return data;
    }

    [Theory]
    [MemberData(nameof(Reachable))]
    public void Has_no_setter_public_field_or_mutable_collection(Type type)
    {
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.SetMethod is { IsPublic: true } set)
                Assert.True(IsInit(set), $"{type.Name}.{p.Name} has a public setter; make it init");
            Assert.True(IsReadOnlyShape(p.PropertyType), $"{type.Name}.{p.Name} is a mutable {p.PropertyType.Name}");
        }
    }

    private static bool IsInit(MethodInfo set) =>
        set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    private static readonly Type[] ReadOnlyCollections =
    [
        typeof(IEnumerable<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>),
        typeof(IReadOnlyDictionary<,>),
    ];

    private static bool IsReadOnlyShape(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(object)) return true;
        if (t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime)) return true;
        if (t.IsGenericType && ReadOnlyCollections.Contains(t.GetGenericTypeDefinition()))
            return t.GetGenericArguments().All(IsReadOnlyShape);
        // Knit's own types are checked by their own row of the theory.
        return t.Namespace?.StartsWith("Knit.", StringComparison.Ordinal) == true && !t.IsArray;
    }

    private static IEnumerable<Type> ReachableTypes(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var t))
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments()) queue.Enqueue(arg);
                continue;
            }
            if (t.IsEnum || t.Namespace?.StartsWith("Knit.", StringComparison.Ordinal) != true || !seen.Add(t)) continue;
            yield return t;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) queue.Enqueue(p.PropertyType);
        }
    }
}
