using System.Text.Json;
using Knit.Core.Discovery;

namespace Knit.Cli.Tests;

/// <summary>
/// JSON-output audit fixes: JsonItem now includes effort/created; search
/// hits include type/state; comment --json returns the new comment;
/// show --json drops the leading h1 (which duplicates frontmatter title).
/// </summary>
[Collection(nameof(CliCollection))]
public class JsonAuditTests
{
    [Fact]
    public void Json_item_includes_effort_and_created()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var added = root.Run("add", "task", "x", "--json");
        var id = JsonDocument.Parse(added.Stdout).RootElement.GetProperty("id").GetString()!;

        // Created should be present immediately after add.
        Assert.Contains("\"created\":", added.Stdout);

        // Set effort and verify the mutator response surfaces it.
        var setR = root.Run("set", id, "effort=L", "--json");
        Assert.Equal(0, setR.ExitCode);
        using var setDoc = JsonDocument.Parse(setR.Stdout);
        Assert.Equal("L", setDoc.RootElement.GetProperty("effort").GetString());
        Assert.True(setDoc.RootElement.TryGetProperty("created", out _));

        // List should also include created/effort.
        var listed = root.Run("list", "--json");
        using var listDoc = JsonDocument.Parse(listed.Stdout);
        var first = listDoc.RootElement.GetProperty("items")[0];
        Assert.True(first.TryGetProperty("created", out _));
        Assert.Equal("L", first.GetProperty("effort").GetString());
    }

    [Fact]
    public void Search_hit_includes_type_and_state()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "feature", "search me alpha").ExitCode);

        var hit = root.Run("search", "alpha", "--json");
        Assert.Equal(0, hit.ExitCode);
        using var doc = JsonDocument.Parse(hit.Stdout);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Equal(1, hits.GetArrayLength());
        Assert.Equal("feature", hits[0].GetProperty("type").GetString());
        Assert.Equal("todo", hits[0].GetProperty("state").GetString());
    }

    [Fact]
    public void Comment_json_returns_new_comment_with_id_and_body()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);
        var added = root.Run("add", "task", "x", "--json");
        var id = JsonDocument.Parse(added.Stdout).RootElement.GetProperty("id").GetString()!;

        var c = root.Run("comment", id, "hello world", "--json");
        Assert.Equal(0, c.ExitCode);
        using var doc = JsonDocument.Parse(c.Stdout);
        Assert.Equal(id, doc.RootElement.GetProperty("item_id").GetString());
        var comment = doc.RootElement.GetProperty("comment");
        Assert.False(string.IsNullOrEmpty(comment.GetProperty("id").GetString()));
        Assert.Equal("hello world", comment.GetProperty("body").GetString());
        Assert.Equal("alice", comment.GetProperty("author").GetString());
        Assert.True(comment.TryGetProperty("created", out _));
    }

    /// <summary>
    /// The `knit-cli` skill promises `created` + `modified` on every `--json` item, so
    /// each surface that emits one has to carry them — `show --json` is
    /// hand-written and drifted away from the shared mapper once already.
    /// </summary>
    [Fact]
    public void Every_json_item_surface_carries_created_and_modified()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var id = JsonDocument.Parse(root.Run("add", "task", "timestamped", "--json").Stdout)
            .RootElement.GetProperty("id").GetString()!;

        void AssertStamped(JsonElement item, string surface)
        {
            foreach (var key in new[] { "created", "modified" })
            {
                Assert.True(item.TryGetProperty(key, out var v), $"{surface} --json is missing '{key}'");
                Assert.False(string.IsNullOrEmpty(v.GetString()), $"{surface} --json has an empty '{key}'");
            }
        }

        using var shown = JsonDocument.Parse(root.Run("show", id, "--json").Stdout);
        AssertStamped(shown.RootElement, "show");

        using var mutated = JsonDocument.Parse(root.Run("set", id, "effort=M", "--json").Stdout);
        AssertStamped(mutated.RootElement, "set");

        foreach (var verb in new[] { "list", "ready" })
        {
            using var doc = JsonDocument.Parse(root.Run(verb, "--json").Stdout);
            var item = doc.RootElement.GetProperty("items").EnumerateArray()
                .Single(e => e.GetProperty("id").GetString() == id);
            AssertStamped(item, verb);
        }

        // `tree --json` nests the same JsonItem one level down under `roots`.
        using var tree = JsonDocument.Parse(root.Run("tree", "--json").Stdout);
        AssertStamped(tree.RootElement.GetProperty("roots")[0].GetProperty("item"), "tree");
    }

    [Fact]
    public void Show_json_drops_leading_h1_from_body_sections()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var added = root.Run("add", "feature", "Todo list CRUD", "--json");
        var id = JsonDocument.Parse(added.Stdout).RootElement.GetProperty("id").GetString()!;

        var shown = root.Run("show", id, "--json");
        using var doc = JsonDocument.Parse(shown.Stdout);
        // The default feature template starts with "# {{title}}" — that h1 must
        // not surface as a "body" section, since it's the same as the title.
        Assert.False(doc.RootElement.TryGetProperty("body", out _),
            "show --json should not emit a 'body' key duplicating the title");
        Assert.Equal("Todo list CRUD", doc.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("tree")]
    [InlineData("ready")]
    [InlineData("status")]
    public void Json_output_does_not_list_the_items_folder_per_item(string command)
    {
        const int items = 25; // overdue, so status maps them too
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        for (var i = 0; i < items; i++) Assert.Equal(0, root.Run("add", "task", $"t{i}", "--due", "2026-01-01").ExitCode);

        var listings = ItemLayout.CountListings();
        var r = root.Run(command, "--json");

        Assert.Equal(0, r.ExitCode);
        Assert.True(listings.Value < items, $"{command} --json listed the items folder {listings.Value} times");
    }

    [Fact]
    public void Json_modified_is_the_files_mtime()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "x").ExitCode);
        var file = Assert.Single(ItemLayout.EnumerateFiles(Path.Combine(root.Path, ".knit", "items")));
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        using var doc = JsonDocument.Parse(root.Run("list", "--json").Stdout);

        Assert.Equal("2026-03-04T05:06:07Z", doc.RootElement.GetProperty("items")[0].GetProperty("modified").GetString());
    }
}
