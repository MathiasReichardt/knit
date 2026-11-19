using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit handles` lists the spellings already in the repo so an
/// agent doesn't invent a new one for an existing person.
/// </summary>
[Collection(nameof(CliCollection))]
public class HandlesTests
{
    private static void Init(TempKnitRoot root, string handle = "me") =>
        Assert.Equal(0, root.Run("init", "--handle", handle, "--display", "Me", "--non-interactive").ExitCode);

    private static string AddTask(TempKnitRoot root, string title, params string[] flags)
    {
        var r = root.Run(["add", "task", title, .. flags, "--json"]);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static Dictionary<string, JsonElement> Handles(TempKnitRoot root)
    {
        var r = root.Run("handles", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("handle").GetString()!, e => e.Clone());
    }

    [Fact]
    public void Counts_assignees_and_comment_authors_separately()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "a", "--assignee", "bob");
        AddTask(root, "b", "--assignee", "bob");
        var c = AddTask(root, "c", "--assignee", "alice");
        Assert.Equal(0, root.Run("comment", c, "looks fine").ExitCode);

        var h = Handles(root);
        Assert.Equal(2, h["bob"].GetProperty("assigned").GetInt32());
        Assert.Equal(0, h["bob"].GetProperty("comments").GetInt32());
        Assert.Equal(1, h["alice"].GetProperty("assigned").GetInt32());
        Assert.Equal(0, h["me"].GetProperty("assigned").GetInt32());
        Assert.Equal(1, h["me"].GetProperty("comments").GetInt32());
    }

    [Fact]
    public void Spellings_are_distinct_rows_so_the_near_duplicate_shows()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "a", "--assignee", "bob");
        AddTask(root, "b", "--assignee", "Bob");

        var h = Handles(root);
        Assert.True(h.ContainsKey("bob"));
        Assert.True(h.ContainsKey("Bob"));
    }

    [Fact]
    public void Own_handle_is_listed_and_marked_even_before_it_is_used()
    {
        using var root = new TempKnitRoot();
        Init(root, "newcomer");
        AddTask(root, "a", "--assignee", "bob");

        var h = Handles(root);
        Assert.True(h["newcomer"].GetProperty("me").GetBoolean());
        Assert.Equal(0, h["newcomer"].GetProperty("assigned").GetInt32());
        Assert.False(h["bob"].GetProperty("me").GetBoolean());
    }

    [Fact]
    public void Most_used_first_then_ordinal()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddTask(root, "a", "--assignee", "zed");
        AddTask(root, "b", "--assignee", "zed");
        AddTask(root, "c", "--assignee", "amy");

        var r = root.Run("handles", "--json");
        var order = JsonDocument.Parse(r.Stdout).RootElement.EnumerateArray()
            .Select(e => e.GetProperty("handle").GetString()).ToList();
        Assert.Equal(["zed", "amy", "me"], order);
    }

    [Fact]
    public void Human_output_marks_you()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var r = root.Run("handles");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("me (you)", r.Stdout);
    }
}
