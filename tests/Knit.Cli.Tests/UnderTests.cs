using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `--under <id>` on `tree` and `list`: scope to the subtree rooted at
/// the given item (item itself + all transitive descendants).
/// </summary>
[Collection(nameof(CliCollection))]
public class UnderTests
{
    private static (TempKnitRoot root, string epicId, string featId, string taskId, string otherEpicId) BuildTree()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var epic = root.Run("add", "epic", "main epic", "--json");
        var epicId = JsonDocument.Parse(epic.Stdout).RootElement.GetProperty("id").GetString()!;

        var feat = root.Run("add", "feature", "f1", "--parent", epicId, "--json");
        var featId = JsonDocument.Parse(feat.Stdout).RootElement.GetProperty("id").GetString()!;

        var task = root.Run("add", "task", "t1", "--parent", featId, "--json");
        var taskId = JsonDocument.Parse(task.Stdout).RootElement.GetProperty("id").GetString()!;

        var otherEpic = root.Run("add", "epic", "unrelated", "--json");
        var otherEpicId = JsonDocument.Parse(otherEpic.Stdout).RootElement.GetProperty("id").GetString()!;

        return (root, epicId, featId, taskId, otherEpicId);
    }

    [Fact]
    public void List_under_includes_root_and_descendants_only()
    {
        var (root, epicId, featId, taskId, otherEpicId) = BuildTree();
        using (root)
        {
            var listed = root.Run("list", "--under", epicId, "--json");
            Assert.Equal(0, listed.ExitCode);
            using var doc = JsonDocument.Parse(listed.Stdout);
            var ids = doc.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(e => e.GetProperty("id").GetString())
                .ToHashSet();

            Assert.Contains(epicId, ids);
            Assert.Contains(featId, ids);
            Assert.Contains(taskId, ids);
            Assert.DoesNotContain(otherEpicId, ids);
        }
    }

    [Fact]
    public void List_under_combines_with_type_filter()
    {
        var (root, epicId, _, taskId, _) = BuildTree();
        using (root)
        {
            var listed = root.Run("list", "--under", epicId, "--type", "task", "--json");
            Assert.Equal(0, listed.ExitCode);
            using var doc = JsonDocument.Parse(listed.Stdout);
            var ids = doc.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(e => e.GetProperty("id").GetString())
                .ToList();

            Assert.Single(ids);
            Assert.Equal(taskId, ids[0]);
        }
    }

    [Fact]
    public void Tree_under_emits_single_subtree_root_in_json()
    {
        var (root, epicId, featId, taskId, otherEpicId) = BuildTree();
        using (root)
        {
            var tree = root.Run("tree", "--under", epicId, "--json");
            Assert.Equal(0, tree.ExitCode);
            using var doc = JsonDocument.Parse(tree.Stdout);
            var roots = doc.RootElement.GetProperty("roots");
            Assert.Equal(1, roots.GetArrayLength());

            var rootNode = roots[0];
            Assert.Equal(epicId, rootNode.GetProperty("item").GetProperty("id").GetString());
            var children = rootNode.GetProperty("children");
            Assert.Equal(1, children.GetArrayLength());
            Assert.Equal(featId, children[0].GetProperty("item").GetProperty("id").GetString());
            var grand = children[0].GetProperty("children");
            Assert.Equal(1, grand.GetArrayLength());
            Assert.Equal(taskId, grand[0].GetProperty("item").GetProperty("id").GetString());

            // unrelated epic is not in the subtree
            Assert.DoesNotContain(otherEpicId, tree.Stdout);
        }
    }

    [Fact]
    public void Tree_under_human_output_includes_root_title()
    {
        var (root, epicId, _, _, _) = BuildTree();
        using (root)
        {
            var tree = root.Run("tree", "--under", epicId);
            Assert.Equal(0, tree.ExitCode);
            Assert.Contains("main epic", tree.Stdout);
            Assert.Contains("f1", tree.Stdout);
            Assert.Contains("t1", tree.Stdout);
            Assert.DoesNotContain("unrelated", tree.Stdout);
        }
    }

    [Fact]
    public void Under_unknown_id_returns_not_found()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var bogus = "00000000-0000-0000-0000-000000000000";

        var l = root.Run("list", "--under", bogus, "--json");
        Assert.Equal(3, l.ExitCode);

        var t = root.Run("tree", "--under", bogus, "--json");
        Assert.Equal(3, t.ExitCode);
    }

    [Fact]
    public void Tree_under_rejects_combination_with_unparented()
    {
        var (root, epicId, _, _, _) = BuildTree();
        using (root)
        {
            var r = root.Run("tree", "--under", epicId, "--unparented", "--json");
            Assert.Equal(2, r.ExitCode);
        }
    }
}
