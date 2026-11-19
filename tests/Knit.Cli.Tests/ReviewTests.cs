using System.Text.Json;
using Knit.Cli.Output;

namespace Knit.Cli.Tests;

/// <summary>
/// The `review` state — implemented, a final review pending. Entered
/// by `knit submit`, left by the reviewer's `done` or `start` (sent back).
/// </summary>
[Collection(nameof(CliCollection))]
public class ReviewTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, params string[] args)
    {
        var r = root.Run([.. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static string StateOf(TempKnitRoot root, string id) =>
        JsonDocument.Parse(root.Run("show", id, "--json").Stdout).RootElement.GetProperty("state").GetString()!;

    private static string InReview(TempKnitRoot root, string title = "Checkout")
    {
        var id = Add(root, "add", "feature", title);
        Assert.Equal(0, root.Run("start", id).ExitCode);
        Assert.Equal(0, root.Run("submit", id).ExitCode);
        return id;
    }

    [Fact]
    public void Submit_moves_in_progress_to_review_and_returns_the_item()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "feature", "Checkout");
        Assert.Equal(0, root.Run("start", id).ExitCode);

        var r = root.Run("submit", id, "--json");

        Assert.Equal(0, r.ExitCode);
        var item = JsonDocument.Parse(r.Stdout).RootElement;
        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal("review", item.GetProperty("state").GetString());
        Assert.Equal("review", StateOf(root, id));
    }

    [Fact]
    public void Submit_refuses_work_that_is_not_in_progress()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Untouched");

        var r = root.Run("submit", id);

        Assert.Equal((int)ErrorCode.BadArgument, r.ExitCode);
        Assert.Contains("is todo", r.Stderr);
        Assert.Contains("knit start", r.Stderr);
        Assert.Equal("todo", StateOf(root, id));
    }

    [Theory]
    [InlineData("start", "in-progress")]
    [InlineData("done", "done")]
    [InlineData("reopen", "todo")]
    [InlineData("dismiss", "dismissed")]
    public void A_review_ends_through_the_usual_verbs(string verb, string expected)
    {
        using var root = NewRepo();
        var id = InReview(root);

        Assert.Equal(0, root.Run(verb, id).ExitCode);
        Assert.Equal(expected, StateOf(root, id));
    }

    [Fact]
    public void Set_state_review_works_like_any_state()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Direct");

        Assert.Equal(0, root.Run("set", id, "state=review").ExitCode);
        Assert.Equal("review", StateOf(root, id));
    }

    [Fact]
    public void Next_and_ready_skip_review()
    {
        using var root = NewRepo();
        InReview(root, "Waiting on reviewer");
        Add(root, "add", "task", "Open work");

        var ready = root.Run("ready", "--json");
        var titles = JsonDocument.Parse(ready.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Open work"], titles);
        Assert.DoesNotContain("Waiting on reviewer", root.Run("next").Stdout);
    }

    [Fact]
    public void A_child_in_review_leaves_its_parent_alone()
    {
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Container");
        var child = Add(root, "add", "task", "Child", "--parent", epic);
        Assert.Equal(0, root.Run("start", child).ExitCode);
        Assert.Equal(0, root.Run("submit", child).ExitCode);

        Assert.Equal("todo", StateOf(root, epic));
    }

    [Fact]
    public void Sort_state_desc_puts_review_between_in_progress_and_done()
    {
        using var root = NewRepo();
        var done = Add(root, "add", "task", "Ccc done");
        Assert.Equal(0, root.Run("done", done).ExitCode);
        InReview(root, "Bbb review");
        var started = Add(root, "add", "task", "Aaa started");
        Assert.Equal(0, root.Run("start", started).ExitCode);

        var stdout = root.Run("list", "--sort", "state:desc").Stdout;

        var a = stdout.IndexOf("Aaa started", StringComparison.Ordinal);
        var b = stdout.IndexOf("Bbb review", StringComparison.Ordinal);
        var c = stdout.IndexOf("Ccc done", StringComparison.Ordinal);
        Assert.True(a < b && b < c, stdout);
    }

    [Fact]
    public void State_filter_and_status_count_review()
    {
        using var root = NewRepo();
        InReview(root, "Under review");
        Add(root, "add", "task", "Other");

        var list = root.Run("list", "--state", "review", "--json");
        var titles = JsonDocument.Parse(list.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Under review"], titles);

        var status = JsonDocument.Parse(root.Run("status", "--json").Stdout).RootElement;
        Assert.Equal(1, status.GetProperty("review").GetInt32());
        Assert.Contains("1 in review", root.Run("status").Stdout);
    }
}
