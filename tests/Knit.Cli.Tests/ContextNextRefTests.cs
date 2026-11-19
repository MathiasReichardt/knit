namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class ContextNextRefTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Context_emits_summary_acceptance_files_in_json()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "do the thing").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path);
        content = content.Replace("state: todo\n", "state: todo\nsummary: short summary\nfeature:\n  - user-mgmt\n");
        content += "\n## Acceptance criteria\n- ac one\n- ac two\n\n## Code pointers\n- src/foo.cs:42 — why\n";
        File.WriteAllText(path, content);

        var r = root.Run("context", id, "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"summary\": \"short summary\"", r.Stdout);
        Assert.Contains("\"acceptance_criteria\"", r.Stdout);
        Assert.Contains("ac one", r.Stdout);
        Assert.Contains("\"code_pointers\"", r.Stdout);
        Assert.Contains("src/foo.cs:42", r.Stdout);
        Assert.Contains("\"feature\"", r.Stdout);
    }

    [Fact]
    public void Context_keeps_checked_state_and_wrapped_text_of_acceptance_criteria()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "do the thing").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("edit", id, "--set-section", "Acceptance Criteria", "--value",
            "- [x] first, long enough to wrap\n  onto a continuation line\n- [ ] second").ExitCode);

        var r = root.Run("context", id, "--json");
        Assert.Equal(0, r.ExitCode);
        var criteria = System.Text.Json.JsonDocument.Parse(r.Stdout).RootElement
            .GetProperty("acceptance_criteria").EnumerateArray()
            .Select(a => (a.GetProperty("text").GetString(), a.GetProperty("checked").GetBoolean()))
            .ToList();
        Assert.Equal([("first, long enough to wrap onto a continuation line", true), ("second", false)], criteria);

        var human = root.Run("context", id);
        Assert.Contains("[x] first, long enough to wrap onto a continuation line", human.Stdout);
        Assert.Contains("[ ] second", human.Stdout);
    }

    /// <summary>
    /// `feature:` (KB feature-doc pointers) and `tags:` (free-form labels) are
    /// distinct fields; the human view once rendered `feature:` under a "Tags"
    /// label and dropped the real tags entirely.
    /// </summary>
    [Fact]
    public void Context_separates_feature_docs_from_tags()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "labelled").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var path = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("state: todo\n", "state: todo\nfeature:\n  - user-mgmt\n"));
        Assert.Equal(0, root.Run("set", id, "tags=infra,urgent").ExitCode);

        var json = root.Run("context", id, "--json");
        Assert.Equal(0, json.ExitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(json.Stdout);
        Assert.Equal("user-mgmt", doc.RootElement.GetProperty("feature")[0].GetString());
        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.Equal(["infra", "urgent"], tags);

        var human = root.Run("context", id);
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Feature docs", human.Stdout);
        Assert.Contains("user-mgmt", human.Stdout);
        Assert.Contains("infra", human.Stdout);
        Assert.Contains("urgent", human.Stdout);
    }

    [Fact]
    public void Context_kb_refs_resolve_and_unresolved_warned()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt", "--summary", "mgmt").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "login").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path);
        content += "\n## Links\n\n" +
            "| Rel    | Target                                       | Notes |\n" +
            "|--------|----------------------------------------------|-------|\n" +
            "| KB ref | [feature:user-mgmt](../kb/features/user-mgmt.md) |       |\n" +
            "| KB ref | [feature:ghost](../kb/features/ghost.md)         |       |\n";
        File.WriteAllText(path, content);

        var r = root.Run("context", id, "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"kb_refs\"", r.Stdout);
        Assert.Contains("feature:user-mgmt", r.Stdout);
        Assert.Contains("\"summary\": \"mgmt\"", r.Stdout);
        Assert.Contains("\"unresolved_refs\"", r.Stdout);
        Assert.Contains("feature:ghost", r.Stdout);
        Assert.Contains("warning:", r.Stderr);
    }

    [Fact]
    public void Context_kb_refs_inherited_from_parent()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "epic", "epic title").ExitCode);
        var epicId = ItemFixtures.FirstItemId(root);

        // Patch epic to add a KB ref row.
        var epicPath = ItemFixtures.FirstItemFile(root);
        var epicContent = File.ReadAllText(epicPath) +
            "\n## Links\n\n" +
            "| Rel    | Target                                          | Notes |\n" +
            "|--------|-------------------------------------------------|-------|\n" +
            "| KB ref | [doc:architecture](../kb/docs/architecture.md)  |       |\n";
        File.WriteAllText(epicPath, epicContent);

        var addChild = root.Run("add", "task", "child", "--parent", epicId);
        Assert.True(addChild.ExitCode == 0, $"exit={addChild.ExitCode}\nstderr={addChild.Stderr}");

        var files = ItemFixtures.AllItemFiles(root);
        var taskFile = files.First(f => !ItemFixtures.EntryName(f).EndsWith("epic-title"));
        var taskId = ItemFixtures.IdFromEntry(ItemFixtures.EntryName(taskFile));

        var r = root.Run("context", taskId, "--json");
        Assert.True(r.ExitCode == 0, $"exit={r.ExitCode} stderr={r.Stderr}");
        Assert.Contains("\"inherited_from\":", r.Stdout);
        Assert.Contains("doc:architecture", r.Stdout);
    }

    [Fact]
    public void Context_renders_blocked_with_origin()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "blocked task").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path).Replace("state: todo\n", "state: todo\nblocked: waiting for vendor\n");
        File.WriteAllText(path, content);

        var r = root.Run("context", id, "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"blocked\"", r.Stdout);
        Assert.Contains("waiting for vendor", r.Stdout);
    }

    /// <summary>
    /// The Description panel used to print the raw body, so the acceptance
    /// criteria showed up there *and* in the structured list below it.
    /// </summary>
    [Fact]
    public void Context_description_panel_does_not_repeat_the_structured_lists()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "do the thing").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var path = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(path, File.ReadAllText(path) +
            "\n## Acceptance criteria\n- crisp\n\n## Code pointers\n- pointy\n");

        var r = root.Run("context", id);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(1, Occurrences(r.Stdout, "crisp"));
        Assert.Equal(1, Occurrences(r.Stdout, "pointy"));
        // The scaffolded `- [ ]` no longer arrives as a criterion named "[ ]".
        Assert.DoesNotContain("[ ]", r.Stdout);
    }

    [Fact]
    public void Context_description_panel_keeps_the_prose_sections()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "do the thing").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var path = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(path, File.ReadAllText(path) + "\n## Notes\n\nthe prose\n");

        var r = root.Run("context", id);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("the prose", r.Stdout);
        // The body h1 duplicates the title the rule already shows.
        Assert.Equal(1, Occurrences(r.Stdout, "do the thing"));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void Next_picks_ready_item()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "first").ExitCode);

        var r = root.Run("next", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"title\": \"first\"", r.Stdout);
    }

    [Fact]
    public void Next_skips_externally_blocked_item()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "blocked").ExitCode);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path).Replace("state: todo\n", "state: todo\nblocked: vendor\n");
        File.WriteAllText(path, content);

        var r = root.Run("next", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("null" + Environment.NewLine, r.Stdout);
    }

    [Fact]
    public void Next_rejects_feature_and_subtree_together()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "x").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var r = root.Run("next", id, "--feature", "x");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void RefShow_dispatches_kb()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);

        var r = root.Run("ref", "show", "doc:arch", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"ref\": \"doc:arch\"", r.Stdout);
    }

    [Fact]
    public void RefShow_dispatches_item()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var r = root.Run("ref", "show", $"item:{id}", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"title\":", r.Stdout);
    }

    [Fact]
    public void Context_design_inherited_from_parent()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui", "--title", "Web UI").ExitCode);
        Assert.Equal(0, root.Run("add", "epic", "epic title").ExitCode);
        var epicId = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("set", epicId, "design=web-ui").ExitCode);

        var addChild = root.Run("add", "task", "child", "--parent", epicId);
        Assert.True(addChild.ExitCode == 0, $"exit={addChild.ExitCode}\nstderr={addChild.Stderr}");

        var files = ItemFixtures.AllItemFiles(root);
        var taskFile = files.First(f => !ItemFixtures.EntryName(f).EndsWith("epic-title"));
        var taskId = ItemFixtures.IdFromEntry(ItemFixtures.EntryName(taskFile));

        var r = root.Run("context", taskId, "--json");
        Assert.True(r.ExitCode == 0, $"exit={r.ExitCode} stderr={r.Stderr}");
        Assert.Contains("\"slug\": \"web-ui\"", r.Stdout);
        Assert.Contains($"\"inherited_from\": \"item:{epicId}\"", r.Stdout);
    }

    [Fact]
    public void Set_design_rejects_unknown_slug()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("set", id, "design=ghost");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no design 'ghost'", r.Stderr);

        // The dangling slug must not have been written.
        var show = root.Run("context", id, "--json");
        Assert.Equal(0, show.ExitCode);
        Assert.DoesNotContain("\"design\"", show.Stdout);
    }

    [Fact]
    public void Set_design_clears_with_empty_value()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui", "--title", "Web UI").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "t").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        Assert.Equal(0, root.Run("set", id, "design=web-ui").ExitCode);

        var r = root.Run("set", id, "design=");
        Assert.Equal(0, r.ExitCode);
        var show = root.Run("context", id, "--json");
        Assert.DoesNotContain("\"design\"", show.Stdout);
    }

    [Fact]
    public void RefShow_dispatches_design()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui", "--title", "Web UI").ExitCode);

        var r = root.Run("ref", "show", "design:web-ui", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"slug\": \"web-ui\"", r.Stdout);
        Assert.Contains("web-ui-design.md", r.Stdout);
    }
}
