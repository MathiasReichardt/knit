using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `add` took no field flags, so every create needed a
/// follow-up `set` per field. The flags route through the same
/// <c>FieldSet.Apply</c> as `set`, so these also pin that the two paths
/// validate identically.
/// </summary>
[Collection(nameof(CliCollection))]
public class AddFieldFlagTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Add_applies_every_field_flag_in_one_command()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "widget", "--title", "Widget").ExitCode);

        var r = root.Run(
            "add", "task", "wire the widget",
            "--assignee", "bob",
            "--due", "2026-09-01",
            "--priority", "important",
            "--effort", "2d",
            "--tags", "backend,api",
            "--slate", "q1",
            "--design", "widget",
            "--agent-skills", "knit-cli,knit-kb",
            "--json");
        Assert.Equal(0, r.ExitCode);

        var item = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal("bob", item.GetProperty("assignee").GetString());
        Assert.Equal("2026-09-01", item.GetProperty("due").GetString());
        Assert.Equal("important", item.GetProperty("priority").GetString());
        Assert.Equal("2d", item.GetProperty("effort").GetString());
        Assert.Equal("q1", item.GetProperty("slate").GetString());
        Assert.Equal("widget", item.GetProperty("design").GetString());
        Assert.Equal(
            new[] { "api", "backend" },
            item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Order().ToArray());
    }

    [Fact]
    public void A_rejected_field_flag_creates_no_item()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("add", "task", "t", "--due", "not-a-date");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("YYYY-MM-DD", r.Stderr);

        // The item is written only after every flag validates — a half-created
        // item with the good fields applied would be worse than the refusal.
        var listed = root.Run("list", "--json");
        Assert.Equal(0, listed.ExitCode);
        Assert.Empty(JsonDocument.Parse(listed.Stdout).RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Add_slate_validates_against_the_registry_and_force_forward_refs()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var unknown = root.Run("add", "task", "t", "--slate", "nope");
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Contains("knit slate add", unknown.Stderr);

        var forced = root.Run("add", "task", "t", "--slate", "Forward Ref", "--force", "--json");
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("\"slate\": \"forward-ref\"", forced.Stdout);
    }

    [Fact]
    public void Add_health_is_not_a_flag_but_project_only_rules_still_hold_on_set()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("set", id, "health=at-risk");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("project-only", r.Stderr);
    }
}
