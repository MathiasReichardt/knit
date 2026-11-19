using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Kb;
using Knit.Core.Refs;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class LinkCommands
{
    public static Command Link()
    {
        var cmd = new Command("link", "Add a link to another item or KB entry");
        var idArg = new Argument<string>("id");
        var parentOpt = new Option<string?>("--parent") { Description = "Set parent" };
        var relatedOpt = new Option<string?>("--related") { Description = "Add 'related' link" };
        var blocksOpt = new Option<string?>("--blocks") { Description = "Add 'blocks' link (this item blocks target)" };
        var kbRefOpt = new Option<string?>("--kb-ref") { Description = "Add 'KB ref' row pointing at a KB entry (e.g. doc:architecture, adr:0007, feature:auth)" };
        var milestoneOpt = new Option<string?>("--milestone") { Description = "Set the 'Milestone' row (replacing any existing one): <slug> resolves within the item's own project, <project>/<slug> addresses any project" };
        var branchOpt = new Option<string?>("--branch") { Description = "Add a 'Branch' row — the git branch this work lives on (stored as written; knit never checks git)" };
        var repoOpt = new Option<string?>("--repo") { Description = "Add a 'Repo' row — a declared mount name, a directory, or a git URL" };
        var prOpt = new Option<string?>("--pr") { Description = "Add a 'PR' row — the full pull/merge request URL, displayed as org/repo#42" };
        var commitOpt = new Option<string?>("--commit") { Description = "Add a 'Commit' row — a SHA (stored as written) or the full commit URL (displayed as org/repo@abc1234); not inherited by children" };
        var noteOpt = new Option<string?>("--note") { Description = "Notes column; on an existing link it replaces the note (\"\" clears it, omitted keeps it)" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(parentOpt);
        cmd.Options.Add(relatedOpt);
        cmd.Options.Add(blocksOpt);
        cmd.Options.Add(kbRefOpt);
        cmd.Options.Add(milestoneOpt);
        cmd.Options.Add(branchOpt);
        cmd.Options.Add(repoOpt);
        cmd.Options.Add(prOpt);
        cmd.Options.Add(commitOpt);
        cmd.Options.Add(noteOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var parent = parse.GetValue(parentOpt);
            var related = parse.GetValue(relatedOpt);
            var blocks = parse.GetValue(blocksOpt);
            var kbRef = parse.GetValue(kbRefOpt);
            var milestone = parse.GetValue(milestoneOpt);
            var note = parse.GetValue(noteOpt);
            var branch = parse.GetValue(branchOpt);
            var repo = parse.GetValue(repoOpt);
            var pr = parse.GetValue(prOpt);
            var commit = parse.GetValue(commitOpt);

            var actions = new[] { parent, related, blocks, kbRef, milestone, branch, repo, pr, commit }.Count(a => !string.IsNullOrEmpty(a));
            if (actions == 0) return Out.Error("specify --parent, --related, --blocks, --kb-ref, --milestone, --branch, --repo, --pr, or --commit", ErrorCode.BadArgument, json);
            if (actions > 1) return Out.Error("specify exactly one link kind", ErrorCode.BadArgument, json);

            var external = !string.IsNullOrEmpty(branch) ? (LinkRel.Branch, branch)
                : !string.IsNullOrEmpty(repo) ? (LinkRel.Repo, repo)
                : !string.IsNullOrEmpty(pr) ? (LinkRel.Pr, pr)
                : !string.IsNullOrEmpty(commit) ? (LinkRel.Commit, commit)
                : ((LinkRel, string)?)null;
            if (external is { } ext)
            {
                var (extRel, extRaw) = ext;
                if (!ExternalRefs.TryResolve(extRel, extRaw, ctx.Root, ctx.Config, out var target, out var extErr))
                    return Out.Error(extErr!, ErrorCode.BadArgument, json);
                var kept = item.LinkRows.FirstOrDefault(r => r.Rel == extRel
                    && string.Equals(r.TargetText, target.Text, StringComparison.Ordinal))?.Notes;
                var row = new LinkRow(extRel, target.Text, target.Path, ItemUtils.Note(note, kept));
                // Several rows of one rel are legitimate; an exact repeat isn't.
                var rows = item.LinkRows.Where(r => !(r.Rel == extRel
                    && string.Equals(r.TargetText, target.Text, StringComparison.Ordinal))).ToList();
                rows.Add(row);
                var extUpdated = item with { LinkRows = rows };
                ItemUtils.Save(ctx, extUpdated);
                if (json) { Out.EmitMutated(ctx, extUpdated); return 0; }
                AnsiConsole.MarkupLine(
                    $"[green]linked[/] {Out.Titled(ctx, item)} {LinkRels.ToWire(extRel).ToLowerInvariant()} → {Markup.Escape(target.Text)}");
                return 0;
            }

            if (!string.IsNullOrEmpty(milestone))
            {
                var (target, resolveErr) = Knit.Data.MilestoneResolver.Resolve(ctx.Store, ctx.Root, item, milestone);
                if (target is null) return Out.Error(resolveErr!, ErrorCode.NotFound, json);
                var (doc, _) = Knit.Core.Projects.MilestoneFile.Read(target.Path, target.ProjectSlug, ctx.Yaml);
                var updated = ItemUtils.WithMilestone(ctx, item, target.Path, doc.Title, note);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]linked[/] {Out.Titled(ctx, item)} milestone → {Markup.Escape(target.QualifiedSlug)}");
                return 0;
            }

            if (!string.IsNullOrEmpty(kbRef))
            {
                var parseResult = Ref.Parse(kbRef);
                if (parseResult.IsError) return KbCommand.RefParseError(ctx, kbRef, parseResult, json);
                var r = parseResult.GetValueOrThrow();
                if (r.Bucket == Bucket.Item)
                    return Out.Error("--kb-ref expects a KB bucket (adr, doc, feature, log, idea, source); use --parent / --related / --blocks for item links", ErrorCode.BadArgument, json);
                var resolveResult = RefResolver.Resolve(r, ctx.Root);
                var resolved = resolveResult.GetValueOrThrow();
                if (!resolved.Exists)
                    return Out.Error(
                        $"unresolved kb ref '{r}': no file at {resolved.Path}. " +
                        KbResolveHint.For(new KbStore(ctx.Root, ctx.Yaml), r),
                        ErrorCode.NotFound, json);
                var updated = ItemUtils.WithKbRef(ctx, item, r, resolved.Path, note);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]linked[/] {Out.Titled(ctx, item)} KB ref → {Markup.Escape(r.ToString())}");
                return 0;
            }

            if (!string.IsNullOrEmpty(parent))
            {
                var (target, e) = ItemUtils.Resolve(ctx, parent);
                if (target is null) return Out.Error(e!, ErrorCode.NotFound, json);
                if (!Hierarchy.ParentAllowed(item.Type, target.Type))
                    return Out.Error(Hierarchy.ExplainRefusal(item.Type, target.Type), ErrorCode.BadArgument, json);
                if (target.Id == item.Id) return Out.Error("an item cannot be its own parent", ErrorCode.BadArgument, json);
                var updated = ItemUtils.WithParent(ctx, item, target.Id, note);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]linked[/] {Out.Titled(ctx, item)} parent → {Out.Titled(ctx, target)}");
                return 0;
            }
            if (!string.IsNullOrEmpty(related))
            {
                var (target, e) = ItemUtils.Resolve(ctx, related);
                if (target is null) return Out.Error(e!, ErrorCode.NotFound, json);
                var updated = ItemUtils.WithLink(ctx, item, LinkRel.Related, target.Id, note);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]linked[/] {Out.Titled(ctx, item)} related → {Out.Titled(ctx, target)}");
                return 0;
            }
            // blocks
            var (b, eb) = ItemUtils.Resolve(ctx, blocks!);
            if (b is null) return Out.Error(eb!, ErrorCode.NotFound, json);
            var blocksUpdated = ItemUtils.WithLink(ctx, item, LinkRel.Blocks, b.Id, note);
            ItemUtils.Save(ctx, blocksUpdated);
            if (json) { Out.EmitMutated(ctx, blocksUpdated); return 0; }
            AnsiConsole.MarkupLine($"[green]linked[/] {Out.Titled(ctx, item)} blocks → {Out.Titled(ctx, b)}");
            return 0;
        });
        return cmd;
    }

    public static Command Unlink()
    {
        var cmd = new Command("unlink", "Remove a link from an item to another item or KB entry");
        var idArg = new Argument<string>("id");
        var targetArg = new Argument<string>("target") { Description = "Target id, 'parent' to clear parent, a KB ref like doc:architecture, or a milestone (<slug> or <project>/<slug>)" };
        cmd.Arguments.Add(idArg);
        cmd.Arguments.Add(targetArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var targetStr = parse.GetValue(targetArg)!;
            if (targetStr == "parent")
            {
                var updated = ItemUtils.WithoutParent(item);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]unlinked[/] parent of {Out.Titled(ctx, item)}");
                return 0;
            }

            // External rows first: a PR or commit URL and a `docs:specs` style branch name
            // both contain a colon, so the KB-ref shape below would claim them.
            {
                var matching = item.ExternalRows.Where(r =>
                    string.Equals(r.TargetText, targetStr, StringComparison.Ordinal)
                    || string.Equals(r.TargetPath, targetStr, StringComparison.Ordinal)).ToList();
                if (matching.Count > 0)
                {
                    var rows = item.LinkRows.Where(r => !matching.Contains(r)).ToList();
                    var updated = item with { LinkRows = rows };
                    ItemUtils.Save(ctx, updated);
                    if (json) { Out.EmitMutated(ctx, updated); return 0; }
                    AnsiConsole.MarkupLine($"[green]unlinked[/] {Out.Titled(ctx, item)} ✕ {Markup.Escape(targetStr)}");
                    return 0;
                }
            }

            // KB-ref target form: bucket:slug (item: bucket falls through to the item branch).
            if (targetStr.Contains(':', StringComparison.Ordinal) && !targetStr.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
            {
                var parseResult = Ref.Parse(targetStr);
                if (parseResult.IsError) return Out.Error(parseResult.GetErrorOrDefault()!.Message, ErrorCode.BadArgument, json);
                var r = parseResult.GetValueOrThrow();
                var resolveResult = RefResolver.Resolve(r, ctx.Root);
                var resolved = resolveResult.GetValueOrThrow();
                var updated = ItemUtils.WithoutKbRef(ctx, item, resolved.Path);
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]unlinked[/] {Out.Titled(ctx, item)} ✕ {Markup.Escape(r.ToString())}");
                return 0;
            }

            // Milestone target form: match against the item's own Milestone
            // rows — <slug> or <project>/<slug>. Falls through to item
            // resolution when nothing matches.
            {
                var slash = targetStr.IndexOf('/');
                var msProject = slash >= 0 ? targetStr[..slash] : null;
                var msSlug = slash >= 0 ? targetStr[(slash + 1)..] : targetStr;
                var matching = item.MilestoneRows.Where(r =>
                    LinkPath.MilestoneFromTarget(r.TargetPath) is { } msId
                    && string.Equals(msId.Slug, msSlug, StringComparison.Ordinal)
                    && (msProject is null || string.Equals(msId.Project, msProject, StringComparison.Ordinal))).ToList();
                if (matching.Count > 0)
                {
                    var rows = item.LinkRows.Where(r => !matching.Contains(r)).ToList();
                    var updated = item with { LinkRows = rows };
                    ItemUtils.Save(ctx, updated);
                    if (json) { Out.EmitMutated(ctx, updated); return 0; }
                    AnsiConsole.MarkupLine($"[green]unlinked[/] {Out.Titled(ctx, item)} ✕ milestone {Markup.Escape(targetStr)}");
                    return 0;
                }
            }

            var (target, e) = ItemUtils.Resolve(ctx, targetStr);
            if (target is null) return Out.Error(e!, ErrorCode.NotFound, json);

            var changed = item;
            if (string.Equals(item.Parent, target.Id, StringComparison.Ordinal))
                changed = ItemUtils.WithoutParent(changed);
            changed = ItemUtils.WithoutLink(changed, target.Id);
            ItemUtils.Save(ctx, changed);
            if (json) { Out.EmitMutated(ctx, changed); return 0; }
            AnsiConsole.MarkupLine($"[green]unlinked[/] {Out.Titled(ctx, item)} ✕ {Out.Titled(ctx, target)}");
            if (item.BlockedByTargetIds.Contains(target.Id))
                ReportBlockAfterUnlink(ctx, changed);
            return 0;
        });
        return cmd;
    }

    // A free-text reason outlives its blockers on purpose — it names an
    // impediment no link resolves, so only the user can say it is gone.
    private static void ReportBlockAfterUnlink(CliContext ctx, Item item)
    {
        var rowsLeft = item.BlockedByTargetIds.Count();
        if (rowsLeft > 0)
            AnsiConsole.MarkupLine($"[yellow]still blocked[/] by {rowsLeft} more blocker(s)");
        else if (!string.IsNullOrEmpty(item.BlockedReason))
        {
            AnsiConsole.MarkupLine($"[yellow]still blocked[/] — reason: {Markup.Escape(item.BlockedReason)}");
            AnsiConsole.MarkupLine(
                $"[dim]hint: check whether that still holds; if not, `knit unblock {Out.Short(ctx, item.Id)}`[/]");
        }
        else
            AnsiConsole.MarkupLine($"[green]unblocked[/] {Out.Titled(ctx, item)}");
    }
}
