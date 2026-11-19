using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Designs;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Kb;
using Knit.Core.Projects;
using Knit.Core.Refs;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// Landing-row feeds: the three by-type rows that back the
/// resume-work page. Covers mtime ordering, offset/limit paging via the
/// X-Total-Count / X-Has-More headers, the items row's in-progress-first
/// ordering, and the `mine` assignee filter.
/// </summary>
public class WebApiLandingTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Designs_row_sorts_by_mtime_and_pages_with_headers()
    {
        var root = NewRepo();
        try
        {
            var store = new DesignStore(root);
            // Create oldest→newest, then stamp mtimes so gamma is newest.
            foreach (var slug in new[] { "alpha", "beta", "gamma" })
                store.New(slug, slug, force: false).GetValueOrThrow();
            SetMtime(DesignsLayout.DesignDocPath(root, "alpha"), Base.AddMinutes(1));
            SetMtime(DesignsLayout.DesignDocPath(root, "beta"), Base.AddMinutes(2));
            SetMtime(DesignsLayout.DesignDocPath(root, "gamma"), Base.AddMinutes(3));

            await WithApi(root, async http =>
            {
                var first = await http.GetAsync("/api/designs?open=1&sort=mtime:desc&limit=2&offset=0");
                var firstPage = await ReadJson<List<DesignSummaryDto>>(first);
                Assert.Equal(new[] { "gamma", "beta" }, firstPage.Select(d => d.Slug));
                Assert.Equal("3", Header(first, "X-Total-Count"));
                Assert.Equal("true", Header(first, "X-Has-More"));

                var second = await http.GetAsync("/api/designs?sort=mtime:desc&limit=2&offset=2");
                var secondPage = await ReadJson<List<DesignSummaryDto>>(second);
                Assert.Equal(new[] { "alpha" }, secondPage.Select(d => d.Slug));
                Assert.Equal("false", Header(second, "X-Has-More"));

                // No paging params → full list, unchanged contract.
                var all = await http.GetFromJsonAsync<List<DesignSummaryDto>>("/api/designs", Web);
                Assert.Equal(3, all!.Count);
            });
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Kb_row_returns_recent_first_with_mtime_field()
    {
        var root = NewRepo();
        try
        {
            var kb = new KbStore(root);
            var p1 = kb.Write(new Ref(Bucket.Doc, "one"), Fm("One"), "# One\n");
            var p2 = kb.Write(new Ref(Bucket.Doc, "two"), Fm("Two"), "# Two\n");
            var p3 = kb.Write(new Ref(Bucket.Doc, "three"), Fm("Three"), "# Three\n");
            SetMtime(p1, Base.AddMinutes(1));
            SetMtime(p2, Base.AddMinutes(2));
            SetMtime(p3, Base.AddMinutes(3));

            await WithApi(root, async http =>
            {
                var resp = await http.GetAsync("/api/kb/list?recent=1&limit=2&offset=0");
                var page = await ReadJson<List<KbSummaryDto>>(resp);
                Assert.Equal(new[] { "three", "two" }, page.Select(k => k.Slug));
                Assert.All(page, k => Assert.False(string.IsNullOrEmpty(k.Modified)));
                Assert.Equal("3", Header(resp, "X-Total-Count"));
                Assert.Equal("true", Header(resp, "X-Has-More"));
            });
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Items_landing_floats_in_progress_leaves_then_recency()
    {
        var root = NewRepo();
        try
        {
            // C is a leaf under in-progress parent B (so B is not a leaf);
            // A is an independent in-progress leaf; D/E backfill by recency.
            var a = WriteItem(root, "a", ItemState.InProgress, assignee: "alice");
            var b = WriteItem(root, "b", ItemState.InProgress);
            var c = WriteItem(root, "c", ItemState.InProgress, parent: b);
            var d = WriteItem(root, "d", ItemState.Todo);
            var e = WriteItem(root, "e", ItemState.Done);

            StampItem(root, c, Base.AddMinutes(5));
            StampItem(root, a, Base.AddMinutes(4));
            StampItem(root, b, Base.AddMinutes(3));
            StampItem(root, d, Base.AddMinutes(2));
            StampItem(root, e, Base.AddMinutes(1));

            await WithApi(root, async http =>
            {
                // Leaves first (C, A by mtime), then rest (B, D, E by mtime).
                var resp = await http.GetAsync("/api/items/landing?limit=2&offset=0");
                var page = await ReadJson<List<ItemSummaryDto>>(resp);
                Assert.Equal(new[] { c, a }, page.Select(i => i.Id));
                Assert.Equal("5", Header(resp, "X-Total-Count"));
                Assert.Equal("true", Header(resp, "X-Has-More"));
                Assert.All(page, i => Assert.False(string.IsNullOrEmpty(i.Modified)));

                var rest = await ReadJson<List<ItemSummaryDto>>(
                    await http.GetAsync("/api/items/landing?limit=5&offset=2"));
                Assert.Equal(new[] { b, d, e }, rest.Select(i => i.Id));

                // mine=1 scopes to alice's assignee — only A survives.
                var mine = await ReadJson<List<ItemSummaryDto>>(
                    await http.GetAsync("/api/items/landing?mine=1"));
                Assert.Equal(new[] { a }, mine.Select(i => i.Id));
            });
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Projects_row_joins_folder_item_and_rollup_attention_first()
    {
        var root = NewRepo();
        try
        {
            // `alpha` is healthy with a nearer milestone; `beta` is at-risk with a
            // later one — attention must outrank proximity.
            NewProject(root, "alpha", "Alpha programme");
            NewProject(root, "beta", "Beta programme");
            NewMilestone(root, "alpha", "pilot", "Pilot line", "2026-03-01");
            NewMilestone(root, "beta", "launch", "Launch", "2026-09-01");
            // A reached milestone must not be offered as "next".
            NewMilestone(root, "beta", "kickoff", "Kickoff", "2026-02-01", reached: true);

            WriteItem(root, "alpha", ItemState.Todo, type: ItemType.Project, project: "alpha");
            var betaRoot = WriteItem(root, "beta", ItemState.Todo, type: ItemType.Project,
                project: "beta", health: Health.AtRisk);
            WriteItem(root, "betakid", ItemState.Done, parent: betaRoot);

            await WithApi(root, async http =>
            {
                var resp = await http.GetAsync("/api/projects/landing?limit=5");
                var page = await ReadJson<List<ProjectLandingDto>>(resp);
                Assert.Equal(new[] { "beta", "alpha" }, page.Select(p => p.Slug));
                Assert.Equal("2", Header(resp, "X-Total-Count"));

                var beta = page[0];
                Assert.Equal("Item beta", beta.Title);
                Assert.Equal("at-risk", beta.Health);
                Assert.Equal(betaRoot, beta.ItemId);
                Assert.Equal(2, beta.Total);
                Assert.Equal(1, beta.Done);
                Assert.Equal(50, beta.Percent);
                Assert.Equal("launch", beta.NextMilestoneSlug);
                Assert.Equal("2026-09-01", beta.NextMilestoneTarget);

                Assert.Null(page[1].Health);
                Assert.Equal("pilot", page[1].NextMilestoneSlug);
            });
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Projects_row_falls_back_to_a_synthesized_all_items_card()
    {
        var root = NewRepo();
        try
        {
            WriteItem(root, "a", ItemState.Done);
            WriteItem(root, "b", ItemState.Todo);

            await WithApi(root, async http =>
            {
                var page = await ReadJson<List<ProjectLandingDto>>(
                    await http.GetAsync("/api/projects/landing?limit=5"));
                var only = Assert.Single(page);
                Assert.Null(only.Slug);
                Assert.Equal("All items", only.Title);
                // No health chip, no milestone line — it must not read as a project.
                Assert.Null(only.Health);
                Assert.Null(only.ItemId);
                Assert.Null(only.NextMilestoneSlug);
                Assert.Equal(2, only.Total);
                Assert.Equal(50, only.Percent);
            });
        }
        finally { TryDelete(root); }
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string NewRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-landing-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
        return root;
    }

    private static Dictionary<string, object?> Fm(string title) =>
        new(StringComparer.Ordinal) { ["title"] = title };

    private static void NewProject(string root, string slug, string title)
    {
        ProjectScaffold.NewProject(root, slug, title, force: false).GetValueOrThrow();
        ProjectScaffold.NewRoadmap(root, slug, title, force: false).GetValueOrThrow();
    }

    private static void NewMilestone(
        string root, string project, string slug, string title, string target, bool reached = false)
    {
        var yaml = new SimpleFrontmatterSerializer();
        ProjectScaffold.NewMilestone(root, project, slug, title, DateOnly.Parse(target), force: false, yaml)
            .GetValueOrThrow();
        if (reached)
            MilestoneEdit.Apply(root, project, slug, new MilestoneEdits(State: MilestoneState.Reached), yaml)
                .GetValueOrThrow();
    }

    private static string WriteItem(string root, string suffix, ItemState state,
        string? assignee = null, string? parent = null,
        ItemType type = ItemType.Task, string? project = null, Health? health = null)
    {
        // 18-char NanoID-shaped id (upper+digits) so BuildPathMap / IdShortener
        // recognise it — distinct per suffix.
        var id = $"ITEM{suffix.ToUpperInvariant()}".PadRight(18, '0');
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        // The body links table is the authoritative parent — a bare Parent
        // property is dropped on write.
        List<LinkRow> links = parent is null
            ? []
            : [new LinkRow(LinkRel.Parent, "parent", LinkPath.Relative(store.ItemsDir, store.FindItemFile(parent)!), null)];
        store.Write(new Item
        {
            LinkRows = links,
            Id = id,
            Title = $"Item {suffix}",
            Type = type,
            State = state,
            Assignee = assignee ?? "",
            Parent = parent ?? "",
            Project = project ?? "",
            Health = health,
            Created = DateTimeOffset.UtcNow,
            Body = $"# Item {suffix}\n",
        });
        return id;
    }

    private static void StampItem(string root, string id, DateTime when)
    {
        var path = ItemLayout.FindFile(RepoDiscovery.ItemsDir(root), id)!;
        SetMtime(path, when);
    }

    private static void SetMtime(string path, DateTime utc) =>
        File.SetLastWriteTimeUtc(path, utc);

    private static async Task WithApi(string root, Func<HttpClient, Task> body)
    {
        var port = GetFreePort();
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
        try
        {
            using var http = TestApi.Client(port);
            await body(http);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<T> ReadJson<T>(HttpResponseMessage resp)
    {
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(Web))!;
    }

    private static string? Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
