using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Mapping;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiMappingTests
{
    [Fact]
    public void Item_round_trips_through_DTO_with_short_id_sections_and_links()
    {
        var root = NewTempRoot();
        try
        {
            var store = new ItemStore(root, new SimpleFrontmatterSerializer());
            var id = "PICKALOGO000000001";
            var otherId = "OTHERITEM000000002";
            var item = new Item
            {
                Id = id,
                Title = "Pick a logo",
                Type = ItemType.Task,
                State = ItemState.InProgress,
                Assignee = "alice",
                Parent = null,
                Rank = "m",
                Effort = "1h",
                Created = DateTimeOffset.UtcNow,
                Body = $"# Pick a logo\n\nSome prose.\n\n## Acceptance criteria\n\n- looks good\n- works on dark\n\n## Links\n\n| Rel    | Target              | Notes |\n|--------|---------------------|-------|\n| Blocks | [other](../{otherId}-x.md) |  |\n",
                LinkRows = LinksTable.Parse($"## Links\n\n| Rel | Target | Notes |\n|---|---|---|\n| Blocks | [other](../{otherId}-x.md) |  |\n"),
            };
            store.Write(item);

            var shortener = IdShortener.FromItemsDir(root);
            var dto = ItemMapping.ToDto(item, shortener, item.Created);

            Assert.Equal(id, dto.Id);
            Assert.True(dto.ShortId.Length >= IdShortener.Floor);
            Assert.StartsWith(dto.ShortId, id.ToLowerInvariant());
            Assert.Equal("task", dto.Type);
            Assert.Equal("in-progress", dto.State);
            Assert.Contains(dto.Sections, s => s.Key == "acceptance_criteria");
            Assert.Single(dto.Links);
            Assert.Equal("Blocks", dto.Links[0].Rel);
            Assert.Equal(otherId, dto.Links[0].TargetItemId);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Etag_tracks_every_edit_to_the_item_file()
    {
        var root = NewTempRoot();
        try
        {
            var store = new ItemStore(root, new SimpleFrontmatterSerializer());
            var item = new Item { Id = "ETAGITEM0000000001", Title = "t", Type = ItemType.Task, State = ItemState.Todo, Created = DateTimeOffset.UnixEpoch };
            store.Write(item);
            var before = Etag.OfItem(store, item.Id);
            Assert.Equal(before, Etag.OfItem(store, item.Id));

            store.Write(item with { LinkRows = [new LinkRow(LinkRel.Related, "o", "OTHERITEM000000002-o.md", null)] });
            var linked = Etag.OfItem(store, item.Id);
            Assert.NotEqual(before, linked);

            File.AppendAllText(store.FindItemFile(item.Id)!, "\n");
            Assert.NotEqual(linked, Etag.OfItem(store, item.Id));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Etag_of_a_missing_file_is_zero() =>
        Assert.Equal("\"0\"", Etag.OfFile(Path.Combine(Path.GetTempPath(), $"knit-missing-{Guid.NewGuid():N}.md")));

    [Fact]
    public void Summary_carries_project_health_and_folder_pointer()
    {
        // The Projects nav roots on project items + joins them to their doc
        // folder via the `project` pointer, so both must ride the summary.
        // Non-project rows keep them null (omitted on the wire).
        var root = NewTempRoot();
        try
        {
            var project = new Item
            {
                Id = "01968000-0000-7000-8000-0000000000a1", Title = "Relaunch",
                Type = ItemType.Project, State = ItemState.InProgress,
                Health = Health.AtRisk, Project = "relaunch", Created = DateTimeOffset.UnixEpoch,
            };
            var task = new Item
            {
                Id = "01968000-0000-7000-8000-0000000000a2", Title = "plain",
                Type = ItemType.Task, State = ItemState.Todo, Created = DateTimeOffset.UnixEpoch,
            };
            var graph = new LinkGraph([project, task]);
            var shortener = IdShortener.FromItemsDir(root);

            var pdto = ItemMapping.ToSummary(project, shortener, graph);
            Assert.Equal("at-risk", pdto.Health);
            Assert.Equal("relaunch", pdto.Project);

            var tdto = ItemMapping.ToSummary(task, shortener, graph);
            Assert.Null(tdto.Health);
            Assert.Null(tdto.Project);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Tree_groups_children_under_parents()
    {
        var parent = new Item { Id = "01968000-0000-7000-8000-000000000001", Title = "feature", Type = ItemType.Feature, State = ItemState.Todo, Created = DateTimeOffset.UnixEpoch };
        var child = new Item { Id = "01968000-0000-7000-8000-000000000002", Title = "task", Type = ItemType.Task, State = ItemState.Todo, Created = DateTimeOffset.UnixEpoch, Parent = parent.Id };
        var root = NewTempRoot();
        try
        {
            var graph = new LinkGraph([parent, child]);
            var shortener = IdShortener.FromItemsDir(root); // empty -> falls back to the floor prefix
            var tree = ItemMapping.ToTree(graph, shortener, SiblingOrder.Comparer(TreeSort.Rank));
            Assert.Single(tree);
            Assert.Equal(parent.Id, tree[0].Item.Id);
            Assert.Single(tree[0].Children);
            Assert.Equal(child.Id, tree[0].Children[0].Item.Id);
        }
        finally { TryDelete(root); }
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-mapping-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".knit", "items"));
        return root;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
