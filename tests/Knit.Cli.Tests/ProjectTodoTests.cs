using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `project` + `todo` item types: add/retype hierarchy rules,
/// `set health=` (project-only), list/tree rendering, and the verify
/// `health_on_non_project` class.
/// </summary>
[Collection(nameof(CliCollection))]
public class ProjectTodoTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var args = parent is null
            ? new[] { "add", type, title, "--json" }
            : new[] { "add", type, title, "--parent", parent, "--json" };
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public void Add_project_at_root_and_children_under_it()
    {
        using var root = Init();
        var project = Add(root, "project", "Website relaunch");
        Add(root, "epic", "Content", project);
        Add(root, "task", "Set up CDN", project);
        Add(root, "todo", "Book launch venue", project);
    }

    [Fact]
    public void Add_todo_requires_project_or_todo_parent()
    {
        using var root = Init();

        var atRoot = root.Run("add", "todo", "loose", "--json");
        Assert.Equal(2, atRoot.ExitCode);
        Assert.Contains("cannot sit at the repo root", atRoot.Stdout + atRoot.Stderr);

        var epic = Add(root, "epic", "e");
        var underEpic = root.Run("add", "todo", "misplaced", "--parent", epic, "--json");
        Assert.Equal(2, underEpic.ExitCode);

        var project = Add(root, "project", "p");
        var todo = Add(root, "todo", "parent todo", project);
        Add(root, "todo", "nested todo", todo);
    }

    [Fact]
    public void Add_project_under_anything_is_rejected()
    {
        using var root = Init();
        var project = Add(root, "project", "p");
        var r = root.Run("add", "project", "sub", "--parent", project, "--json");
        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public void Set_health_on_project_sets_clears_and_round_trips()
    {
        using var root = Init();
        var project = Add(root, "project", "p");

        var set = root.Run("set", project, "health=at-risk", "--json");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal("at-risk", JsonDocument.Parse(set.Stdout).RootElement.GetProperty("health").GetString());

        var shown = root.Run("show", project, "--json");
        Assert.Equal(0, shown.ExitCode);
        Assert.Equal("at-risk", JsonDocument.Parse(shown.Stdout).RootElement.GetProperty("health").GetString());

        // Empty clears; the field is omitted from JSON when unset.
        var clear = root.Run("set", project, "health=", "--json");
        Assert.Equal(0, clear.ExitCode);
        Assert.False(JsonDocument.Parse(clear.Stdout).RootElement.TryGetProperty("health", out _));
    }

    [Fact]
    public void Set_health_rejects_unknown_value_and_non_project_target()
    {
        using var root = Init();
        var project = Add(root, "project", "p");
        var task = Add(root, "task", "t");

        var unknown = root.Run("set", project, "health=green", "--json");
        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("on-track|at-risk|off-track", unknown.Stdout + unknown.Stderr);

        var wrongType = root.Run("set", task, "health=on-track", "--json");
        Assert.Equal(2, wrongType.ExitCode);
        Assert.Contains("project-only", wrongType.Stdout + wrongType.Stderr);
    }

    [Fact]
    public void List_and_tree_render_project_and_todo()
    {
        using var root = Init();
        var project = Add(root, "project", "Relaunch");
        Add(root, "todo", "Venue", project);

        var list = root.Run("list");
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("project", list.Stdout);
        Assert.Contains("todo", list.Stdout);

        var tree = root.Run("tree");
        Assert.Equal(0, tree.ExitCode);
        Assert.Contains("Relaunch", tree.Stdout);
        Assert.Contains("Venue", tree.Stdout);
    }

    [Fact]
    public void Retype_epic_to_project_requires_root()
    {
        using var root = Init();
        var project = Add(root, "project", "p");
        var epic = Add(root, "epic", "e", project);

        // Nested epic → project would violate root-only; detaching fixes it.
        var nested = root.Run("retype", epic, "--to", "project", "--yes", "--json");
        Assert.Equal(2, nested.ExitCode);

        var detached = root.Run("retype", epic, "--to", "project", "--parent", "", "--yes", "--json");
        Assert.Equal(0, detached.ExitCode);
        Assert.Equal("project", JsonDocument.Parse(detached.Stdout).RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Verify_flags_health_on_non_project()
    {
        using var root = Init();
        var task = Add(root, "task", "hand-edited");

        // Sneak the field past the CLI guard the way a hand-edit would.
        var file = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace("state: todo", "state: todo\nhealth: off-track"));

        var r = root.Run("verify", "--json");
        Assert.Equal(4, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var findings = doc.RootElement.GetProperty("health_on_non_project").EnumerateArray().ToList();
        var finding = Assert.Single(findings);
        Assert.Equal(task, finding.GetProperty("item_id").GetString());
        Assert.Equal("task", finding.GetProperty("type").GetString());
        Assert.Equal("off-track", finding.GetProperty("health").GetString());

        var human = root.Run("verify");
        Assert.Equal(4, human.ExitCode);
        Assert.Contains("health on non-project", human.Stdout);
    }

    [Fact]
    public void Verify_flags_hand_edited_todo_at_root_as_hierarchy_violation()
    {
        using var root = Init();
        var task = Add(root, "task", "will become a loose todo");

        var file = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(file, File.ReadAllText(file).Replace("type: task", "type: todo"));

        var r = root.Run("verify", "--json");
        Assert.Equal(4, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var violation = Assert.Single(doc.RootElement.GetProperty("hierarchy").EnumerateArray().ToList());
        Assert.Equal(task, violation.GetProperty("child_id").GetString());
        Assert.Equal("todo", violation.GetProperty("child_type").GetString());
    }
}
