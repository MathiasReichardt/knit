using Knit.Core.Domain;
using Knit.Core.Ids;
using Knit.Core.Projects;
using Knit.Core.Result;
using Knit.Core.Templates;

namespace Knit.Data;

/// <summary>
/// Atomically co-creates the two halves of a project: the doc folder
/// (<c>.knit/projects/&lt;slug&gt;/readme.md</c>) <em>and</em> a tracker item of
/// type <c>project</c> whose <c>project:</c> field points back at that folder —
/// or links the folder to an existing project item. Tightens the item↔folder
/// join so neither half can orphan:
/// creating one half creates/links the other in a single action. Shared by the
/// CLI (<c>knit project new</c>) and, later, the web "New project" flow so both
/// surfaces produce a joined project.
/// </summary>
public static class ProjectCreator
{
    public sealed record Result(
        string Slug,
        string FolderPath,
        Item Item,
        bool CreatedItem,
        bool CreatedRoadmap);

    /// <summary>
    /// Scaffold the folder and create-or-link the project item. When
    /// <paramref name="linkItemId"/> is null a fresh project item is minted;
    /// otherwise that item is linked (must be type <c>project</c> and not
    /// already point at a different slug). The link target is validated
    /// <em>before</em> any file is written so a bad <c>--item</c> never leaves a
    /// folder behind — both halves land or neither does.
    /// </summary>
    public static OperationResult<Result> Create(ItemStore store, string root, string author,
        string slug, string title, bool roadmap, bool force, string? linkItemId, string? goal = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return OperationResult<Result>.Error(KnitError.BadArgument("slug must not be empty"));

        Item? existing = null;
        if (linkItemId is not null)
        {
            existing = store.Read(linkItemId);
            if (existing is null)
                return OperationResult<Result>.Error(KnitError.NotFound($"no item '{linkItemId}'"));
            if (existing.Type != ItemType.Project)
                return OperationResult<Result>.Error(KnitError.BadArgument(
                    $"only a project item can own a doc folder — item {existing.Id} \"{existing.Title}\" is a {existing.Type.ToWire()} (retype it first)"));
            if (!string.IsNullOrEmpty(existing.Project) && !string.Equals(existing.Project, slug, StringComparison.Ordinal))
                return OperationResult<Result>.Error(KnitError.Conflict(
                    $"item {existing.Id} \"{existing.Title}\" already points at project '{existing.Project}'"));
        }

        var folder = ProjectScaffold.NewProject(root, slug, title, force, goal);
        if (folder.IsError) return OperationResult<Result>.Error(folder.GetErrorOrDefault()!);
        var folderPath = folder.GetValueOrThrow();

        var createdRoadmap = false;
        if (roadmap)
        {
            var rm = ProjectScaffold.NewRoadmap(root, slug, title, force);
            if (rm.IsError) return OperationResult<Result>.Error(rm.GetErrorOrDefault()!);
            createdRoadmap = true;
        }

        Item item;
        bool createdItem;
        if (existing is not null)
        {
            item = existing with { Project = slug };
            createdItem = false;
        }
        else
        {
            var id = store.NewItemId();
            var template = TemplateEngine.Load(root, ItemType.Project, null);
            var body = TemplateEngine.Render(
                template, TemplateEngine.StandardValues(id, title, author, ItemType.Project));
            item = new Item
            {
                Id = id,
                Title = title,
                Type = ItemType.Project,
                State = ItemState.Todo,
                Rank = NextRank(store),
                Project = slug,
                Created = DateTimeOffset.UtcNow,
                Body = body,
            };
            createdItem = true;
        }

        store.Write(item);
        return OperationResult<Result>.Ok(new Result(slug, folderPath, item, createdItem, createdRoadmap));
    }

    private static string NextRank(ItemStore store)
    {
        var maxRank = store.AllItems()
            .Select(i => i.Rank)
            .Where(r => Rank.IsValid(r))
            .DefaultIfEmpty()
            .Max(StringComparer.Ordinal);
        return string.IsNullOrEmpty(maxRank) ? Rank.Initial() : Rank.Between(maxRank, "");
    }
}
