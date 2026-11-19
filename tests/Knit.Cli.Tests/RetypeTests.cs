namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class RetypeTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Retype_task_to_feature_succeeds_and_changes_type()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "candidate").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("retype", id, "--to", "feature", "--yes");
        Assert.Equal(0, r.ExitCode);

        var show = root.Run("show", id, "--json");
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("\"type\": \"feature\"", show.Stdout);
    }

    [Fact]
    public void Retype_unknown_type_returns_bad_argument()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "x").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("retype", id, "--to", "nonsense", "--yes", "--json");
        Assert.Equal((int)Knit.Cli.Output.ErrorCode.BadArgument, r.ExitCode);
        Assert.Contains("unknown type", r.Stderr);
    }

    [Fact]
    public void Retype_invalid_parent_combo_is_rejected()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // task → epic is rejected because an epic cannot be a child of a task.
        Assert.Equal(0, root.Run("add", "task", "parent task").ExitCode);
        var parentId = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("add", "task", "child", "--parent", parentId).ExitCode);
        var childId = ItemFixtures.AllItemFiles(root)
            .Select(f => ItemFixtures.IdFromEntry(ItemFixtures.EntryName(f)))
            .First(i => i != parentId);

        var r = root.Run("retype", childId, "--to", "epic", "--yes", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("invalid_parent", r.Stderr);
    }

    [Fact]
    public void Retype_would_orphan_children_is_rejected()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // Build feature with a task child; retyping the feature → task would
        // leave the existing child task allowed (task → task is valid), so
        // construct a case that actually orphans: epic with a feature child,
        // retype epic → task (feature cannot be a child of task).
        Assert.Equal(0, root.Run("add", "epic", "top").ExitCode);
        var epicId = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("add", "feature", "feat", "--parent", epicId).ExitCode);

        var r = root.Run("retype", epicId, "--to", "task", "--yes", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("would_orphan_children", r.Stderr);
    }

    [Fact]
    public void Retype_with_parent_change_moves_under_new_parent()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "epic", "epic a").ExitCode);
        var epicAId = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("add", "epic", "epic b").ExitCode);
        var epicBId = ItemFixtures.AllItemFiles(root)
            .Select(f => ItemFixtures.IdFromEntry(ItemFixtures.EntryName(f)))
            .First(i => i != epicAId);
        Assert.Equal(0, root.Run("add", "task", "movable", "--parent", epicAId).ExitCode);
        var taskId = ItemFixtures.AllItemFiles(root)
            .Select(f => ItemFixtures.IdFromEntry(ItemFixtures.EntryName(f)))
            .First(i => i != epicAId && i != epicBId);

        var r = root.Run("retype", taskId, "--to", "feature", "--parent", epicBId, "--yes", "--json");
        Assert.Equal(0, r.ExitCode);

        var show = root.Run("show", taskId, "--json");
        Assert.Contains("\"type\": \"feature\"", show.Stdout);
        Assert.Contains(epicBId, show.Stdout);
    }
}
