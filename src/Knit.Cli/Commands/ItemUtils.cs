using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Refs;
using Knit.Data;

namespace Knit.Cli.Commands;

internal static class ItemUtils
{
    public static (Item? item, string? error) Resolve(CliContext ctx, string idOrPrefix)
    {
        var stripped = Ref.StripItemPrefix(idOrPrefix) ?? idOrPrefix;
        var item = ctx.Store.Read(stripped);
        if (item is null) return (null, $"item not found or prefix is ambiguous: {idOrPrefix}");
        return (item, null);
    }

    public static void Save(CliContext ctx, Item item)
    {
        ctx.Store.Write(item);
    }

    /// <summary>
    /// Add an item↔item link row. Resolves the target into a relative
    /// path from the source item's directory. Idempotent on (rel, target).
    /// </summary>
    public static Item WithLink(CliContext ctx, Item item, LinkRel rel, string targetId, string? notes = null)
    {
        var sourceFile = ctx.Store.FindItemFile(item.Id);
        var targetFile = ctx.Store.FindItemFile(targetId);
        if (sourceFile is null || targetFile is null) return item;
        var sourceDir = Path.GetDirectoryName(sourceFile)!;
        var path = LinkPath.Relative(sourceDir, targetFile);
        var target = ctx.Store.Read(targetId);
        var text = target?.Title ?? ItemLayout.EntryName(targetFile);
        var rows = item.LinkRows.ToList();
        var existing = rows.FindIndex(r => r.Rel == rel && LinkPath.ItemIdFromTarget(r.TargetPath) == targetId);
        if (existing >= 0)
        {
            if (notes is null) return item;
            rows[existing] = rows[existing] with { Notes = Note(notes, null) };
        }
        else rows.Add(new LinkRow(rel, text, path, Note(notes, null)));
        return item with { LinkRows = rows };
    }

    /// <summary>
    /// A link's note after a re-link: <c>null</c> keeps <paramref name="current"/>,
    /// empty clears it, anything else replaces it.
    /// </summary>
    public static string? Note(string? requested, string? current) =>
        requested is null ? current : requested.Length == 0 ? null : requested;

    /// <summary>
    /// Remove every link row whose target resolves to the given item id.
    /// </summary>
    public static Item WithoutLink(Item item, string targetId)
    {
        var rows = item.LinkRows
            .Where(r => LinkPath.ItemIdFromTarget(r.TargetPath) != targetId)
            .ToList();
        return item with { LinkRows = rows };
    }

    /// <summary>
    /// Set or replace the Parent row in LinkRows so the writer emits a
    /// <c>Parent</c> entry pointing at the target item file. Also keeps
    /// the denormalized <see cref="Item.Parent"/> in sync.
    /// </summary>
    public static Item WithParent(CliContext ctx, Item item, string targetId, string? notes = null)
    {
        var sourceFile = ctx.Store.FindItemFile(item.Id);
        var targetFile = ctx.Store.FindItemFile(targetId);
        if (targetFile is null) return item;
        // New items have no source file yet — fall back to the items dir.
        var sourceDir = sourceFile is not null ? Path.GetDirectoryName(sourceFile)! : ctx.Store.ItemsDir;
        var path = LinkPath.Relative(sourceDir, targetFile);
        var target = ctx.Store.Read(targetId);
        var text = target?.Title ?? ItemLayout.EntryName(targetFile);
        var kept = item.Parent == targetId ? item.LinkRows.FirstOrDefault(r => r.Rel == LinkRel.Parent)?.Notes : null;
        var rows = item.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList();
        rows.Insert(0, new LinkRow(LinkRel.Parent, text, path, Note(notes, kept)));
        return item with { LinkRows = rows, Parent = targetId };
    }

    public static Item WithoutParent(Item item)
    {
        var rows = item.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList();
        return item with { LinkRows = rows, Parent = null };
    }

    /// <summary>
    /// Add or update a <c>KB ref</c> row pointing at a resolved KB
    /// artifact. Idempotent on target path: re-adding the same ref
    /// updates the Notes column in place rather than duplicating.
    /// </summary>
    public static Item WithKbRef(CliContext ctx, Item item, Ref kbRef, string targetFile, string? notes)
    {
        var sourceFile = ctx.Store.FindItemFile(item.Id);
        var sourceDir = sourceFile is not null ? Path.GetDirectoryName(sourceFile)! : ctx.Store.ItemsDir;
        var path = LinkPath.Relative(sourceDir, targetFile);
        var text = kbRef.ToString();

        var rows = item.LinkRows.ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Rel == LinkRel.KbRef
                && string.Equals(rows[i].TargetPath, path, StringComparison.OrdinalIgnoreCase))
            {
                rows[i] = new LinkRow(LinkRel.KbRef, text, path, Note(notes, rows[i].Notes));
                return item with { LinkRows = rows };
            }
        }
        rows.Add(new LinkRow(LinkRel.KbRef, text, path, Note(notes, null)));
        return item with { LinkRows = rows };
    }

    /// <summary>
    /// Remove every <c>KB ref</c> row whose target path matches the
    /// resolved file for the given ref. Used by <c>knit unlink</c>
    /// when the target argument is a <c>bucket:slug</c>.
    /// </summary>
    public static Item WithoutKbRef(CliContext ctx, Item item, string targetFile)
    {
        var sourceFile = ctx.Store.FindItemFile(item.Id);
        var sourceDir = sourceFile is not null ? Path.GetDirectoryName(sourceFile)! : ctx.Store.ItemsDir;
        var path = LinkPath.Relative(sourceDir, targetFile);
        var rows = item.LinkRows
            .Where(r => !(r.Rel == LinkRel.KbRef
                          && string.Equals(r.TargetPath, path, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return item with { LinkRows = rows };
    }

    /// <summary>
    /// Set the item's <c>Milestone</c> row, replacing any existing one —
    /// one milestone per item is the convention, so the write path
    /// enforces it (hand-edited multiples surface via verify).
    /// </summary>
    public static Item WithMilestone(CliContext ctx, Item item, string targetFile, string text, string? notes)
    {
        var sourceFile = ctx.Store.FindItemFile(item.Id);
        var sourceDir = sourceFile is not null ? Path.GetDirectoryName(sourceFile)! : ctx.Store.ItemsDir;
        var path = LinkPath.Relative(sourceDir, targetFile);
        var kept = item.LinkRows.FirstOrDefault(r => r.Rel == LinkRel.Milestone
            && string.Equals(r.TargetPath, path, StringComparison.OrdinalIgnoreCase))?.Notes;
        var rows = item.LinkRows.Where(r => r.Rel != LinkRel.Milestone).ToList();
        rows.Add(new LinkRow(LinkRel.Milestone, text, path, Note(notes, kept)));
        return item with { LinkRows = rows };
    }

    public static Item WithComment(Item item, Comment comment)
    {
        var comments = item.Comments.Concat([comment]).ToList();
        return item with { Comments = comments };
    }
}
