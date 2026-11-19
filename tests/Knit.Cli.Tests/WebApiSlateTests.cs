using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiSlateTests
{
    [Fact]
    public async Task Slate_endpoints_cover_list_create_patch_reorder_rename_delete_union()
    {
        var root = CreateRepo(out var store);
        // 18-char NanoID shape — parent-link parsing rejects anything else.
        var itemId = "SLATETESTITEM000A1";
        store.Write(new Item
        {
            Id = itemId,
            Title = "Carries a slate",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Slate = "q1-push",
            Body = "# Carries a slate\n",
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Union before any registry exists: the item-only value shows.
                var derived = await http.GetFromJsonAsync<SlateDto[]>("/api/slates", opts);
                Assert.Contains(derived!, s => s is { Name: "q1-push", InRegistry: false, ItemCount: 1 });

                // Create (name slugified) → 201; duplicate → 409.
                var create = await http.PostAsJsonAsync("/api/slates",
                    new SlateCreateRequestDto("Q1 Push", null, "current focus"), opts);
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                var created = await create.Content.ReadFromJsonAsync<SlateDto>(opts);
                Assert.Equal("q1-push", created!.Name);
                Assert.Equal("planned", created.Status);
                Assert.Equal(1, created.ItemCount);
                var dup = await http.PostAsJsonAsync("/api/slates",
                    new SlateCreateRequestDto("q1-push", null, null), opts);
                Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

                var create2 = await http.PostAsJsonAsync("/api/slates",
                    new SlateCreateRequestDto("backlog", "active", null), opts);
                Assert.Equal(HttpStatusCode.Created, create2.StatusCode);

                // Status/notes patch; unknown status → 400.
                var patch = await http.PatchAsJsonAsync("/api/slates/q1-push",
                    new SlatePatchRequestDto(null, "active", "started", null, null), opts);
                Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
                var patched = await patch.Content.ReadFromJsonAsync<SlatePatchResultDto>(opts);
                Assert.Equal("active", patched!.Slate.Status);
                Assert.Equal("started", patched.Slate.Notes);
                var badStatus = await http.PatchAsJsonAsync("/api/slates/q1-push",
                    new SlatePatchRequestDto(null, "sprinting", null, null, null), opts);
                Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);

                // Reorder: backlog before q1-push.
                var move = await http.PatchAsJsonAsync("/api/slates/backlog",
                    new SlatePatchRequestDto(null, null, null, "q1-push", null), opts);
                Assert.Equal(HttpStatusCode.OK, move.StatusCode);
                var ordered = await http.GetFromJsonAsync<SlateDto[]>("/api/slates", opts);
                Assert.Equal(["backlog", "q1-push"], ordered!.Select(s => s.Name).ToArray());

                // Rename walks items.
                var rename = await http.PatchAsJsonAsync("/api/slates/q1-push",
                    new SlatePatchRequestDto("q2-push", null, null, null, null), opts);
                Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
                var renamed = await rename.Content.ReadFromJsonAsync<SlatePatchResultDto>(opts);
                Assert.Equal("q2-push", renamed!.Slate.Name);
                Assert.Equal(1, renamed.ItemsUpdated);
                Assert.Equal("q2-push", store.Read(itemId)!.Slate);

                // Delete drops the row and clears the value from the item.
                var del = await http.DeleteAsync("/api/slates/q2-push");
                Assert.Equal(HttpStatusCode.OK, del.StatusCode);
                var deleted = await del.Content.ReadFromJsonAsync<SlateDeleteResultDto>(opts);
                Assert.Equal(1, deleted!.ItemsCleared);
                Assert.Null(store.Read(itemId)!.Slate);
                var after = await http.GetFromJsonAsync<SlateDto[]>("/api/slates", opts);
                Assert.DoesNotContain(after!, s => s.Name == "q2-push");
                var delAgain = await http.DeleteAsync("/api/slates/q2-push");
                Assert.Equal(HttpStatusCode.NotFound, delAgain.StatusCode);

                // An item-only slate (no registry row) is cleared too.
                store.Write(store.Read(itemId)! with { Slate = "hand-typed" });
                var verify = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", opts);
                var unregistered = Assert.Single(verify!.UnregisteredSlates);
                Assert.Equal("hand-typed", unregistered.Slate);
                Assert.StartsWith(".knit/items/", Assert.Single(unregistered.Paths));
                var stray = await http.DeleteAsync("/api/slates/hand-typed");
                Assert.Equal(HttpStatusCode.OK, stray.StatusCode);
                Assert.Null(store.Read(itemId)!.Slate);
                var delMissing = await http.DeleteAsync("/api/slates/nope");
                Assert.Equal(HttpStatusCode.NotFound, delMissing.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Items_filter_on_effective_slate_and_patch_assigns()
    {
        var root = CreateRepo(out var store);
        // 18-char NanoID shape — parent-link parsing rejects anything else.
        var epicId = "SLATETESTEPIC000B1";
        var childId = "SLATETESTTASK000B2";
        var looseId = "SLATETESTTASK000B3";
        store.Write(new Item
        {
            Id = epicId, Title = "Epic in q1", Type = ItemType.Epic, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Slate = "q1", Body = "# Epic in q1\n",
        });
        store.Write(new Item
        {
            Id = childId, Title = "Inherits q1", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Parent = epicId, Body = "# Inherits q1\n",
            // Parent round-trips via the Links table row, not the bare field.
            LinkRows = [new LinkRow(LinkRel.Parent, "Epic in q1", $"../{epicId}-epic-in-q1.md", null)],
        });
        store.Write(new Item
        {
            Id = looseId, Title = "No slate", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# No slate\n",
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                http.DefaultRequestHeaders.Add("If-Match", "*");
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Inherited match: the child rides its parent's slate.
                var q1 = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/items?slate=q1", opts);
                Assert.Equal(2, q1!.Length);
                Assert.Contains(q1, i => i.Id == epicId && i.Slate == "q1");
                Assert.Contains(q1, i => i.Id == childId && i.Slate is null);

                var none = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/items?noSlate=1", opts);
                Assert.Equal([looseId], none!.Select(i => i.Id).ToArray());

                var both = await http.GetAsync("/api/items?slate=q1&noSlate=1");
                Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

                // /api/ready applies the parsed slate filter, inherited too.
                var ready = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/ready?slate=q1", opts);
                Assert.Equal([epicId, childId], ready!.Select(i => i.Id).Order().ToArray());
                var readyNone = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/ready?noSlate=1", opts);
                Assert.Equal([looseId], readyNone!.Select(i => i.Id).ToArray());
                var readyTask = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/ready?slate=q1&type=task", opts);
                Assert.Equal([childId], readyTask!.Select(i => i.Id).ToArray());

                // PATCH assigns (slugified) and "" clears.
                var assign = await http.PatchAsJsonAsync($"/api/items/{looseId}",
                    new { slate = "Q1" }, opts);
                Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
                var assigned = await assign.Content.ReadFromJsonAsync<ItemDto>(opts);
                Assert.Equal("q1", assigned!.Slate);

                var clear = await http.PatchAsJsonAsync($"/api/items/{looseId}",
                    new { slate = "" }, opts);
                Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
                var cleared = await clear.Content.ReadFromJsonAsync<ItemDto>(opts);
                Assert.Null(cleared!.Slate);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient http)> Start(string root)
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
        return (app, TestApi.Client(port));
    }

    private static string CreateRepo(out ItemStore store)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-slates-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
        store = new ItemStore(root, new SimpleFrontmatterSerializer());
        return root;
    }

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
