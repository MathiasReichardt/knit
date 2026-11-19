using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Ids;
using Knit.Core.Kb;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit move &lt;id&gt; &lt;new-slug&gt;</c> — ref-aware item move.
/// Renames the item file and walks every item +
/// KB entry to rewrite incoming <c>Target</c> cells matching the item's
/// UUID, then markdown links in body prose (items, KB, designs, project
/// docs). Modeled on <c>git mv</c>'s "move + keep refs sane" contract.
/// </summary>
public static class MoveCommand
{
    public static Command Build()
    {
        var cmd = new Command(
            "move",
            "Move an item entry (rename slug); rewrites incoming Target paths and prose links across items, KB, designs, project docs.");
        var idArg = new Argument<string>("id");
        var newSlugArg = new Argument<string>("new-slug");
        cmd.Arguments.Add(idArg);
        cmd.Arguments.Add(newSlugArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var currentFile = ctx.Store.FindItemFile(item.Id)!;
            var newSlug = Slug.From(parse.GetValue(newSlugArg)!);
            var oldEntry = ItemLayout.EntryName(currentFile);
            var newEntry = $"{item.Id}-{newSlug}";
            if (string.Equals(oldEntry, newEntry, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine("[yellow]no change[/]");
                return 0;
            }

            var newFile = Path.Combine(ctx.Store.ItemsDir, $"{newEntry}.md");
            if (File.Exists(newFile))
                return Out.Error($"target file already exists: {newFile}", ErrorCode.Conflict, json);
            File.Move(currentFile, newFile);

            ctx.Refresh();

            var (itemsChanged, kbChanged) = RewriteIncoming(ctx, item.Id, oldEntry, newEntry);
            // Last: the store's writes above would refuse a body changed under them.
            var proseChanged = new ProseRefRewriter(ctx.Store, ctx.Root).RewriteProse([(currentFile, newFile)], []).Count;

            if (json) { Out.EmitMutated(ctx, item); return 0; }
            AnsiConsole.MarkupLine(
                $"[green]moved[/] {Out.Titled(ctx, item)} → {Markup.Escape(newEntry)}" +
                $" [dim](items rewritten: {itemsChanged}, kb rewritten: {kbChanged}, prose rewritten: {proseChanged})[/]");
            return 0;
        }, exclusive: "move");
        return cmd;
    }

    private static (int itemsChanged, int kbChanged) RewriteIncoming(
        CliContext ctx, string itemId, string oldEntry, string newEntry)
    {
        var itemsChanged = 0;
        foreach (var other in ctx.Store.AllItems())
        {
            if (other.LinkRows.Count == 0) continue;
            var rewrote = false;
            var rows = new List<LinkRow>(other.LinkRows.Count);
            foreach (var row in other.LinkRows)
            {
                var targetId = LinkPath.ItemIdFromTarget(row.TargetPath);
                if (string.Equals(targetId, itemId, StringComparison.Ordinal))
                {
                    var newPath = ReplaceEntryInTarget(row.TargetPath, oldEntry, newEntry);
                    rows.Add(row with { TargetPath = newPath });
                    rewrote = true;
                }
                else rows.Add(row);
            }
            if (rewrote)
            {
                ctx.Store.Write(other with { LinkRows = rows });
                itemsChanged++;
            }
        }

        // Walk every KB artifact and rewrite paths that reference the
        // old entry name; the name holds the id, so a literal replacement
        // is safe.
        var kbChanged = 0;
        foreach (var file in KbLayout.EnumerateArtifactFiles(ctx.Root))
        {
            var content = AtomicFile.ReadAllText(file).Replace("\r\n", "\n");
            if (!content.Contains("## Links", StringComparison.OrdinalIgnoreCase)) continue;
            if (!content.Contains(oldEntry, StringComparison.Ordinal)) continue;
            var updated = content.Replace(oldEntry, newEntry);
            if (updated != content)
            {
                AtomicFile.WriteAllText(file, updated);
                kbChanged++;
            }
        }
        return (itemsChanged, kbChanged);
    }

    /// <summary>
    /// Replace the entry segment in a Target path (<c>.../&lt;entry&gt;.md</c>).
    /// </summary>
    private static string ReplaceEntryInTarget(string targetPath, string oldEntry, string newEntry)
    {
        // Replace as a path segment, not raw substring — guards against
        // accidental matches inside a longer slug.
        var norm = targetPath.Replace('\\', '/');
        if (norm.EndsWith($"/{oldEntry}.md", StringComparison.Ordinal))
            return norm[..^(oldEntry.Length + 4)] + $"/{newEntry}.md";
        return norm.Equals($"{oldEntry}.md", StringComparison.Ordinal) ? $"{newEntry}.md" : norm;
    }
}
