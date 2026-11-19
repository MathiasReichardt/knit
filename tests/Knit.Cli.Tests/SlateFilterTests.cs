using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SlateFilterTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var args = parent is null
            ? new[] { "add", type, title, "--json" }
            : new[] { "add", type, title, "--parent", parent, "--json" };
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        var m = Regex.Match(r.Stdout, "\"id\": \"([^\"]+)\"");
        Assert.True(m.Success, $"no id in: {r.Stdout}");
        return m.Groups[1].Value;
    }

    [Fact]
    public void Set_slate_validates_against_registry_with_force_forward_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Add(root, "task", "t");

        var unknown = root.Run("set", id, "slate=nope");
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Contains("knit slate add", unknown.Stderr);

        var forced = root.Run("set", id, "slate=Forward Ref", "--force", "--json");
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("\"slate\": \"forward-ref\"", forced.Stdout);

        Assert.Equal(0, root.Run("slate", "add", "q1-push").ExitCode);
        var known = root.Run("set", id, "slate=q1-push", "--json");
        Assert.Equal(0, known.ExitCode);
        Assert.Contains("\"slate\": \"q1-push\"", known.Stdout);

        var cleared = root.Run("set", id, "slate=", "--json");
        Assert.Equal(0, cleared.ExitCode);
        Assert.DoesNotContain("\"slate\"", cleared.Stdout);
    }

    [Fact]
    public void Slate_filter_inherits_to_descendants_and_no_slate_shows_the_rest()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        var epic = Add(root, "epic", "epic in q1");
        var feature = Add(root, "feature", "inherits q1", epic);
        var task = Add(root, "task", "inherits q1 too", feature);
        var loose = Add(root, "task", "no slate");
        Assert.Equal(0, root.Run("set", epic, "slate=q1").ExitCode);

        var filtered = root.Run("list", "--slate", "q1", "--json");
        Assert.Equal(0, filtered.ExitCode);
        Assert.Contains($"\"id\": \"{epic}\"", filtered.Stdout);
        Assert.Contains($"\"id\": \"{feature}\"", filtered.Stdout);
        Assert.Contains($"\"id\": \"{task}\"", filtered.Stdout);
        Assert.DoesNotContain($"\"id\": \"{loose}\"", filtered.Stdout);

        var noSlate = root.Run("list", "--no-slate", "--json");
        Assert.Equal(0, noSlate.ExitCode);
        Assert.Contains($"\"id\": \"{loose}\"", noSlate.Stdout);
        Assert.DoesNotContain($"\"id\": \"{epic}\"", noSlate.Stdout);
        Assert.DoesNotContain($"\"id\": \"{task}\"", noSlate.Stdout);
    }

    /// <summary>
    /// Items matched through inheritance carried
    /// <c>slate: null</c>, so a <c>--slate</c> query's own JSON couldn't say
    /// why those rows were in the result.
    /// </summary>
    [Fact]
    public void Json_carries_the_effective_slate_alongside_the_written_one()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        var epic = Add(root, "epic", "epic in q1");
        var task = Add(root, "task", "inherits q1", epic);
        var loose = Add(root, "task", "no slate");
        Assert.Equal(0, root.Run("set", epic, "slate=q1").ExitCode);

        var listed = root.Run("list", "--json");
        Assert.Equal(0, listed.ExitCode);
        var items = JsonDocument.Parse(listed.Stdout)
            .RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(e => e.GetProperty("id").GetString()!);

        Assert.Equal("q1", Str(items[epic], "slate"));
        Assert.Equal("q1", Str(items[epic], "effective_slate"));
        // Inheritance writes nothing to the child — the query result says it anyway.
        Assert.Null(Str(items[task], "slate"));
        Assert.Equal("q1", Str(items[task], "effective_slate"));
        Assert.Null(Str(items[loose], "effective_slate"));

        // Same field on show --json (hand-written writer) and on a mutator.
        Assert.Contains("\"effective_slate\": \"q1\"", root.Run("show", task, "--json").Stdout);
        Assert.Contains("\"effective_slate\": \"q1\"", root.Run("start", task, "--json").Stdout);

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.GetString() : null;
    }

    [Fact]
    public void Explicit_child_slate_overrides_and_reroots_its_subtree()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "slate-1").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "slate-2").ExitCode);
        var epic = Add(root, "epic", "in slate-1");
        var pulled = Add(root, "feature", "pulled to slate-2", epic);
        var pulledChild = Add(root, "task", "follows the pull", pulled);
        Assert.Equal(0, root.Run("set", epic, "slate=slate-1").ExitCode);
        Assert.Equal(0, root.Run("set", pulled, "slate=slate-2").ExitCode);

        var one = root.Run("list", "--slate", "slate-1", "--json");
        Assert.Contains($"\"id\": \"{epic}\"", one.Stdout);
        Assert.DoesNotContain($"\"id\": \"{pulled}\"", one.Stdout);
        Assert.DoesNotContain($"\"id\": \"{pulledChild}\"", one.Stdout);

        var two = root.Run("list", "--slate", "slate-2", "--json");
        Assert.DoesNotContain($"\"id\": \"{epic}\"", two.Stdout);
        Assert.Contains($"\"id\": \"{pulled}\"", two.Stdout);
        Assert.Contains($"\"id\": \"{pulledChild}\"", two.Stdout);
    }

    [Fact]
    public void Slate_filter_composes_with_other_filters()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        var epic = Add(root, "epic", "container");
        var task = Add(root, "task", "the only task", epic);
        Assert.Equal(0, root.Run("set", epic, "slate=q1").ExitCode);

        var r = root.Run("list", "--slate", "q1", "--type", "task", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains($"\"id\": \"{task}\"", r.Stdout);
        Assert.DoesNotContain($"\"id\": \"{epic}\"", r.Stdout);
    }

    [Fact]
    public void Tree_slate_filter_hides_other_subtrees()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        var inSlate = Add(root, "epic", "in the slate");
        var child = Add(root, "feature", "inherited", inSlate);
        var outside = Add(root, "epic", "outside");
        Assert.Equal(0, root.Run("set", inSlate, "slate=q1").ExitCode);

        var r = root.Run("tree", "--slate", "q1", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains($"\"id\": \"{inSlate}\"", r.Stdout);
        Assert.Contains($"\"id\": \"{child}\"", r.Stdout);
        Assert.DoesNotContain($"\"id\": \"{outside}\"", r.Stdout);
    }

    [Fact]
    public void Slate_and_no_slate_flags_are_mutually_exclusive()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("list", "--slate", "x", "--no-slate");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--no-slate", r.Stderr);
    }
}
