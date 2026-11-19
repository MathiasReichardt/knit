using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.WebApi;
using Knit.WebApi.Contracts;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>tree --sort</c>, <c>tree_sort</c>, <c>list --sort state|tree</c>
/// and their web API twins (<c>/api/tree?treeSort=</c>, <c>/api/items?sort=</c>) on the shared sibling order;
/// <c>reorder --by state</c> and <c>/api/items/apply-sort</c> bake it into rank.
/// </summary>
[Collection(nameof(CliCollection))]
public class TreeSortTests
{
    // Children are added against the state order, so rank alone gives the reverse.
    private static readonly string[] RankOrder = ["dismissed", "draft", "todo-1", "todo-2", "prog", "review", "done"];
    private static readonly string[] StateOrder = ["done", "review", "prog", "todo-1", "todo-2", "draft", "dismissed"];

    private static TempKnitRoot Seed()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var epic = Add(root, "epic", "alpha");
        Run(root, "dismiss", Add(root, "task", "dismissed", "--parent", epic));
        Add(root, "task", "draft", "--parent", epic, "--draft");
        Run(root, "block", Add(root, "task", "todo-1", "--parent", epic), "--reason", "vendor");
        Add(root, "task", "todo-2", "--parent", epic);
        Run(root, "start", Add(root, "task", "prog", "--parent", epic));
        var review = Add(root, "task", "review", "--parent", epic);
        Run(root, "start", review);
        Run(root, "submit", review);
        Run(root, "done", Add(root, "task", "done", "--parent", epic));

        var beta = Add(root, "epic", "beta");
        Add(root, "task", "beta-todo", "--parent", beta);
        Run(root, "done", Add(root, "task", "beta-done", "--parent", beta));
        return root;
    }

    private static string Add(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["add", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static void Run(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(args);
        Assert.True(r.ExitCode == 0, $"{string.Join(' ', args)} failed: {r.Stderr}");
    }

    private static string Title(JsonElement item) => item.GetProperty("title").GetString()!;

    private static string[] AlphaChildren(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["tree", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, r.Stderr);
        var alpha = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("roots").EnumerateArray()
            .Single(n => Title(n.GetProperty("item")) == "alpha");
        return alpha.GetProperty("children").EnumerateArray().Select(c => Title(c.GetProperty("item"))).ToArray();
    }

    private static string[] List(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["list", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, r.Stderr);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items").EnumerateArray().Select(Title).ToArray();
    }

    [Fact]
    public void Tree_sorts_each_sibling_group_by_state_and_keeps_rank_within_a_band()
    {
        using var root = Seed();
        Assert.Equal(RankOrder, AlphaChildren(root));
        Assert.Equal(StateOrder, AlphaChildren(root, "--sort", "state"));
    }

    [Fact]
    public void Text_tree_follows_the_same_order()
    {
        using var root = Seed();
        var stdout = root.Run("tree", "--sort", "state").Stdout;
        var positions = StateOrder.Select(t => stdout.IndexOf($" {t}", StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0, stdout));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void Text_tree_orders_roots_by_rank_not_type()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Add(root, "task", "loose-task");
        Add(root, "project", "late-project");

        var stdout = root.Run("tree").Stdout;
        Assert.True(stdout.IndexOf("loose-task", StringComparison.Ordinal) < stdout.IndexOf("late-project", StringComparison.Ordinal), stdout);
    }

    [Fact]
    public void Config_sets_the_default_and_the_flag_overrides_it()
    {
        using var root = Seed();
        Run(root, "config", "set", "tree_sort", "state");
        Assert.Equal(StateOrder, AlphaChildren(root));
        Assert.Equal(RankOrder, AlphaChildren(root, "--sort", "rank"));
    }

    [Fact]
    public void Bad_values_are_refused()
    {
        using var root = Seed();
        var flag = root.Run("tree", "--sort", "priority");
        Assert.NotEqual(0, flag.ExitCode);
        Assert.Contains("rank|state", flag.Stderr);

        Assert.NotEqual(0, root.Run("config", "set", "tree_sort", "priority").ExitCode);
    }

    [Fact]
    public void List_sort_state_keeps_dismissed_last_both_ways()
    {
        using var root = Seed();
        var asc = List(root, "--sort", "state", "--type", "task").Where(StateOrder.Contains).ToArray();
        Assert.Equal(StateOrder, asc);

        var desc = List(root, "--sort", "state:desc", "--type", "task").Where(StateOrder.Contains).ToArray();
        Assert.Equal(["draft", "todo-1", "todo-2", "prog", "review", "done", "dismissed"], desc);
    }

    [Fact]
    public void List_sort_tree_follows_tree_sort_per_sibling_group()
    {
        using var root = Seed();
        Run(root, "config", "set", "tree_sort", "state");
        // beta-done stays under beta: the sort orders siblings, it doesn't flatten the tree.
        Assert.Equal(["alpha", .. StateOrder, "beta", "beta-done", "beta-todo"], List(root, "--sort", "tree"));
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<string[]> ApiAlphaChildren(HttpClient http, string query)
    {
        var tree = await http.GetFromJsonAsync<TreeNodeDto[]>($"/api/tree{query}", JsonOpts);
        return tree!.Single(n => n.Item.Title == "alpha").Children.Select(c => c.Item.Title).ToArray();
    }

    private static async Task<string[]> ApiList(HttpClient http, string query) =>
        (await http.GetFromJsonAsync<ItemSummaryDto[]>($"/api/items{query}", JsonOpts))!.Select(i => i.Title).ToArray();

    [Fact]
    public async Task Api_tree_takes_treeSort_and_falls_back_to_config()
    {
        using var root = Seed();
        await using var host = await Host.StartAsync(root.Path);

        Assert.Equal(RankOrder, await ApiAlphaChildren(host.Client, ""));
        Assert.Equal(StateOrder, await ApiAlphaChildren(host.Client, "?treeSort=state"));
        // A filtered tree goes through the filtered builder — same order.
        Assert.Equal(StateOrder, await ApiAlphaChildren(host.Client, "?treeSort=state&excludeType=todo"));
        Assert.Equal(StateOrder, await ApiAlphaChildren(host.Client, "?treeSort=state&excludeType=todo&filterMode=flat"));

        Run(root, "config", "set", "tree_sort", "state");
        Assert.Equal(StateOrder, await ApiAlphaChildren(host.Client, ""));
        Assert.Equal(RankOrder, await ApiAlphaChildren(host.Client, "?treeSort=rank"));

        var bad = await host.Client.GetAsync("/api/tree?treeSort=priority");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("rank|state", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_items_sort_state_and_tree_match_the_cli()
    {
        using var root = Seed();
        await using var host = await Host.StartAsync(root.Path);

        Assert.Equal(StateOrder, (await ApiList(host.Client, "?type=task&sort=state:asc")).Where(StateOrder.Contains));
        Assert.Equal(
            ["draft", "todo-1", "todo-2", "prog", "review", "done", "dismissed"],
            (await ApiList(host.Client, "?type=task&sort=state:desc")).Where(StateOrder.Contains));

        var tree = await ApiList(host.Client, "?sort=tree:asc&treeSort=state");
        Assert.Equal(["alpha", .. StateOrder, "beta", "beta-done", "beta-todo"], tree);
        Run(root, "config", "set", "tree_sort", "state");
        Assert.Equal(List(root, "--sort", "tree"), await ApiList(host.Client, "?sort=tree:asc"));
    }

    private static Dictionary<string, string> ItemFiles(TempKnitRoot root) =>
        Directory.EnumerateFiles(Path.Combine(root.Path, ".knit", "items"), "*.md", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);

    private static string[] Changed(Dictionary<string, string> before, Dictionary<string, string> after) =>
        after.Where(kv => !before.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key).ToArray();

    private static JsonElement ReorderJson(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["reorder", "--by", "state", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, r.Stderr);
        return JsonDocument.Parse(r.Stdout).RootElement;
    }

    [Fact]
    public void Reorder_by_state_bakes_the_view_into_rank_and_moves_only_what_is_out_of_order()
    {
        using var root = Seed();
        var before = ItemFiles(root);

        var result = ReorderJson(root, "--under", Id(root, "alpha"));

        var moved = result.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("title").GetString()).ToArray();
        // Rank order is the state order reversed but for todo-1 → todo-2: those two stay.
        Assert.Equal(5, result.GetProperty("moved").GetInt32());
        Assert.Equal(7, result.GetProperty("total").GetInt32());
        Assert.Equal(1, result.GetProperty("groups").GetInt32());
        Assert.Equal(StateOrder, AlphaChildren(root, "--sort", "rank"));
        Assert.Equal(5, Changed(before, ItemFiles(root)).Length);
        Assert.DoesNotContain("todo-1", moved);
        Assert.DoesNotContain("beta-done", moved);

        // Applied twice is a no-op.
        Assert.Equal(0, ReorderJson(root, "--under", Id(root, "alpha")).GetProperty("moved").GetInt32());
    }

    [Fact]
    public void Reorder_by_dry_run_writes_nothing()
    {
        using var root = Seed();
        var before = ItemFiles(root);

        var result = ReorderJson(root, "--all", "--dry-run");

        Assert.True(result.GetProperty("dry_run").GetBoolean());
        Assert.Equal(6, result.GetProperty("moved").GetInt32());
        Assert.Equal(2, result.GetProperty("groups").GetInt32());
        Assert.Empty(Changed(before, ItemFiles(root)));
        Assert.Contains("would reorder 6 of 11 items in 2 groups", root.Run("reorder", "--by", "state", "--all", "--dry-run").Stdout);
    }

    [Fact]
    public void Reorder_by_shallow_leaves_grandchildren_alone()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        var epic = Add(root, "epic", "e");
        var open = Add(root, "feature", "open", "--parent", epic);
        Add(root, "task", "child-todo", "--parent", open);
        Run(root, "done", Add(root, "task", "child-done", "--parent", open));
        Run(root, "done", Add(root, "feature", "closed", "--parent", epic));

        Assert.Equal(1, ReorderJson(root, "--under", epic, "--shallow").GetProperty("moved").GetInt32());
        Assert.Equal(1, ReorderJson(root, "--under", epic).GetProperty("moved").GetInt32());
        Assert.Equal(0, ReorderJson(root, "--all").GetProperty("moved").GetInt32());
    }

    [Theory]
    [InlineData("reorder", "--by", "state")]
    [InlineData("reorder", "--by", "state", "--all", "--under", "x")]
    [InlineData("reorder", "--by", "state", "--all", "--shallow")]
    [InlineData("reorder", "--by", "priority", "--all")]
    [InlineData("reorder", "x", "--by", "state", "--all")]
    [InlineData("reorder", "x", "--before", "y", "--dry-run")]
    public void Reorder_by_refuses_a_missing_or_mixed_scope(params string[] args)
    {
        using var root = Seed();
        var before = ItemFiles(root);
        var r = root.Run(args);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Empty(Changed(before, ItemFiles(root)));
    }

    private static string Id(TempKnitRoot root, string title)
    {
        var r = root.Run("list", "--json");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Single(i => Title(i) == title).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Api_apply_sort_dry_run_counts_then_writes()
    {
        using var root = Seed();
        await using var host = await Host.StartAsync(root.Path);
        var alpha = Id(root, "alpha");
        var before = ItemFiles(root);

        var dry = await Post(host.Client, $"/api/items/{alpha}/apply-sort?by=state&dryRun=1");
        Assert.Equal((true, 5, 7, 1), (dry.DryRun, dry.Moved, dry.Total, dry.Groups));
        Assert.Empty(Changed(before, ItemFiles(root)));

        Assert.Equal(1, (await Post(host.Client, $"/api/items/{alpha}/apply-sort?by=state&shallow=1&dryRun=1")).Groups);
        Assert.Equal(2, (await Post(host.Client, "/api/items/apply-sort?by=state&dryRun=1")).Groups);

        var wet = await Post(host.Client, $"/api/items/{alpha}/apply-sort?by=state");
        Assert.Equal(dry.MovedIds, wet.MovedIds);
        Assert.Equal(StateOrder, await ApiAlphaChildren(host.Client, "?treeSort=rank"));

        var bad = await host.Client.PostAsync("/api/items/apply-sort?by=priority", null);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    private static async Task<ApplySortResultDto> Post(HttpClient http, string url)
    {
        var r = await http.PostAsync(url, null);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<ApplySortResultDto>(JsonOpts))!;
    }

    private sealed class Host(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Host> StartAsync(string root)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            return new Host(app, TestApi.Client(port));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
