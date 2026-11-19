using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Templates;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class AddCommand
{
    public static Command Build()
    {
        var cmd = new Command("add", "Create a new item");
        var typeArg = new Argument<string>("type") { Description = "project | epic | feature | bug | task | todo" };
        var titleArg = new Argument<string>("title") { Description = "Item title (quote it)" };
        var parentOpt = new Option<string?>("--parent") { Description = "Parent item id (or unique prefix)" };
        var templateOpt = new Option<string?>("--template") { Description = "Template variant name" };
        var forceOpt = new Option<bool>("--force") { Description = "Allow --slate to forward-reference a slate not (yet) in the registry." };
        var draftOpt = new Option<bool>("--draft") { Description = "Create as a draft — not yet specified or approved, excluded from next / ready / pick" };
        var todoOpt = new Option<bool>("--todo") { Description = "Create as todo even where `initial_state` is `draft`" };
        cmd.Arguments.Add(typeArg);
        cmd.Arguments.Add(titleArg);
        cmd.Options.Add(parentOpt);
        cmd.Options.Add(templateOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(draftOpt);
        cmd.Options.Add(todoOpt);

        // Field flags. Each is validated by the same FieldSet.Apply that backs
        // `knit set`, so create and edit can never diverge on what they accept.
        var fieldOpts = new List<(Option<string?> Opt, string Field)>();
        Option<string?> Field(string flag, string field, string description)
        {
            var o = new Option<string?>(flag) { Description = description };
            cmd.Options.Add(o);
            fieldOpts.Add((o, field));
            return o;
        }
        Field("--assignee", "assignee", "Assign on creation");
        Field("--due", "due", "Due date (YYYY-MM-DD)");
        Field("--priority", "priority", "someday | normal | important | critical");
        Field("--effort", "effort", "Free-form effort estimate");
        Field("--tags", "tags", "Comma-separated tag list");
        Field("--slate", "slate", "Slate name (validated against the registry; see --force)");
        Field("--design", "design", "Design-doc slug (must already exist)");
        Field("--agent-skills", "agent_skills", "Comma-separated skill list");

        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var typeStr = parse.GetValue(typeArg)!;
            if (!EnumStrings.TryParseItemType(typeStr, out var type))
                return Out.Error($"unknown type '{typeStr}' (expected project|epic|feature|bug|task|todo)", ErrorCode.BadArgument, json);

            var parent = parse.GetValue(parentOpt);
            string? parentId = null;
            ItemType? parentType = null;
            if (!string.IsNullOrEmpty(parent))
            {
                var (parentItem, _) = ItemUtils.Resolve(ctx, parent);
                if (parentItem is null) return Out.Error($"parent not found: {parent}", ErrorCode.NotFound, json);
                parentId = parentItem.Id;
                parentType = parentItem.Type;
            }

            if (!Hierarchy.ParentAllowed(type, parentType))
                return Out.Error(Hierarchy.ExplainRefusal(type, parentType), ErrorCode.BadArgument, json);

            var draft = parse.GetValue(draftOpt);
            var asTodo = parse.GetValue(todoOpt);
            if (draft && asTodo)
                return Out.Error("--draft and --todo contradict each other", ErrorCode.BadArgument, json);
            // Config decides only when neither flag is passed. An unparseable
            // value can't reach here — `knit config set` validates it — so an
            // unknown one means a hand-edited file, and `todo` is the safe read.
            var initialState = draft || (!asTodo && string.Equals(ctx.Config.InitialState, "draft", StringComparison.OrdinalIgnoreCase))
                ? ItemState.Draft
                : ItemState.Todo;

            var title = parse.GetValue(titleArg)!;
            var id = ctx.Store.NewItemId();
            string template;
            try
            {
                template = TemplateEngine.Load(ctx.Root, type, parse.GetValue(templateOpt));
            }
            catch (ArgumentException ex)
            {
                return Out.Error(ex.Message, ErrorCode.BadArgument, json);
            }
            var body = TemplateEngine.Render(
                template,
                TemplateEngine.StandardValues(id, title, ctx.Config.Handle!, type));

            var nextRank = ComputeNextRank(ctx);

            var linkRows = new List<LinkRow>();
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentFile = ctx.Store.FindItemFile(parentId);
                if (parentFile is not null)
                {
                    // New flat item lives directly in items/; relative path
                    // from items/ to the parent's file gives the canonical target.
                    var path = LinkPath.Relative(ctx.Store.ItemsDir, parentFile);
                    var parentItem = ctx.Store.Read(parentId);
                    var text = parentItem?.Title ?? Knit.Core.Discovery.ItemLayout.EntryName(parentFile);
                    linkRows.Add(new LinkRow(LinkRel.Parent, text, path, null));
                }
            }

            var item = new Item
            {
                Id = id,
                Title = title,
                Type = type,
                State = initialState,
                Parent = parentId,
                LinkRows = linkRows,
                Rank = nextRank,
                Created = DateTimeOffset.UtcNow,
                Body = body,
            };

            // Applied before the first write: a rejected --due must leave no
            // half-created item on disk.
            var force = parse.GetValue(forceOpt);
            foreach (var (opt, field) in fieldOpts)
            {
                var raw = parse.GetValue(opt);
                if (raw is null) continue;
                var result = FieldSet.Apply(ctx, item, field, raw.Trim(), force);
                if (result.Error is not null) return Out.Error(result.Error, result.Code, json);
                item = result.Item!;
            }

            var folder = ctx.Store.Write(item);
            ctx.Refresh();

            if (json) { Out.EmitMutated(ctx, item); return 0; }
            AnsiConsole.MarkupLine($"[green]created[/] {Out.Titled(ctx, item)} [dim]{Markup.Escape(folder)}[/]");
            return 0;
        }, needsHandle: _ => true);
        return cmd;
    }

    private static string ComputeNextRank(CliContext ctx)
    {
        var maxRank = ctx.Store.AllItems()
            .Select(i => i.Rank)
            .Where(r => Rank.IsValid(r))
            .DefaultIfEmpty()
            .Max(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(maxRank)) return Rank.Initial();
        return Rank.Between(maxRank, "");
    }
}
