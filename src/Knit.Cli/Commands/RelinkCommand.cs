using System.CommandLine;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit relink</c> — companion to <c>verify</c>. Walks every broken
/// Target path and every short <c>item:</c> prose ref and, when run in a
/// TTY, prompts: pick a candidate (auto-resolved by id/slug match; a short
/// ref expands to its full id while still unique), or skip.
/// In <c>--non-interactive</c> mode it auto-repairs only unambiguous
/// candidates and skips the rest.
/// </summary>
public static class RelinkCommand
{
    public static Command Build()
    {
        var cmd = new Command("relink", "Repair broken Target paths and expand short item: prose refs (interactive unless --non-interactive)");
        var nonInteractiveOpt = new Option<bool>("--non-interactive")
            { Description = "Apply unambiguous fixes only; skip the rest." };
        // Opt-in, not folded into the default run: `id:` / `slug:` is an
        // identity field, and rewriting one as a side effect of repairing
        // link targets is not what the caller asked for.
        var idsOpt = new Option<bool>("--ids")
        {
            Description = "Instead of link targets, rewrite stale KB `id:` / design `slug:` fields from the filename.",
        };
        cmd.Options.Add(nonInteractiveOpt);
        cmd.Options.Add(idsOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var nonInteractive = parse.GetValue(nonInteractiveOpt);
            if (parse.GetValue(idsOpt)) return RepairIds(ctx, nonInteractive);

            var broken = RepoVerifier.Scan(ctx.Store, ctx.Root);
            var itemRefs = RepoVerifier.ScanProse(ctx.Store, ctx.Root, ctx.Config).Refs
                .Where(p => p.Reason is RepoVerifier.ProseRefReasons.ShortId or RepoVerifier.ProseRefReasons.AmbiguousId)
                .DistinctBy(p => (p.SourcePath, p.Ref))
                .ToList();
            if (broken.Count == 0 && itemRefs.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]ok[/] no broken targets");
                return 0;
            }

            var repaired = 0;
            var skipped = 0;
            foreach (var b in broken)
            {
                var candidate = RepoVerifier.ResolveRelinkCandidate(ctx.Store, ctx.Root, b.SourcePath, b.Row.TargetPath);
                if (candidate is null)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]no candidate[/] for {Markup.Escape(b.Row.TargetText)} " +
                        $"in {Markup.Escape(Path.GetRelativePath(ctx.Root, b.SourcePath))}");
                    skipped++;
                    continue;
                }
                var apply = nonInteractive
                    ? true
                    : AnsiConsole.Confirm(
                        $"repair [yellow]{Markup.Escape(b.Row.TargetText)}[/] → " +
                        $"[green]{Markup.Escape(candidate)}[/]?", true);
                if (!apply) { skipped++; continue; }
                RepoVerifier.ApplyRelink(b.SourcePath, b.Row.TargetPath, candidate);
                repaired++;
            }
            foreach (var p in itemRefs)
            {
                var rel = Markup.Escape(Path.GetRelativePath(ctx.Root, p.SourcePath));
                if (p.Reason == RepoVerifier.ProseRefReasons.AmbiguousId)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]ambiguous[/] `{Markup.Escape(p.Ref)}` in {rel} — matches " +
                        string.Join(", ", p.Candidates.Take(5)) + (p.Candidates.Count > 5 ? ", …" : ""));
                    skipped++;
                    continue;
                }
                var full = p.Candidates[0];
                var apply = nonInteractive || AnsiConsole.Confirm(
                    $"expand [yellow]`{Markup.Escape(p.Ref)}`[/] → [green]`item:{full}`[/] in {rel}?", true);
                if (!apply) { skipped++; continue; }
                var prefix = p.Ref["item:".Length..].Split('#')[0];
                if (RepoVerifier.ApplyItemRefExpansion(p.SourcePath, prefix, full)) repaired++;
                else skipped++;
            }
            AnsiConsole.MarkupLine($"[green]repaired:[/] {repaired}, [yellow]skipped:[/] {skipped}");
            return 0;
        }, exclusive: "relink");
        return cmd;
    }

    /// <summary>
    /// <c>relink --ids</c>: set every drifted <c>id:</c> / <c>slug:</c> from
    /// the filename. Unambiguous by construction — the file's location is
    /// what refs resolve against — so <c>--non-interactive</c> applies all of
    /// them; the prompt exists only to show what is about to change.
    /// </summary>
    private static int RepairIds(CliContext ctx, bool nonInteractive)
    {
        var drift = RepoVerifier.ScanIdentityDrift(ctx.Root, ctx.Yaml);
        if (drift.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]ok[/] no stale identity fields");
            return 0;
        }

        var repaired = 0;
        var skipped = 0;
        foreach (var d in drift)
        {
            var rel = Path.GetRelativePath(ctx.Root, d.SourcePath);
            var apply = nonInteractive || AnsiConsole.Confirm(
                $"set [yellow]{Markup.Escape(d.Field)}: {Markup.Escape(d.Declared)}[/] → " +
                $"[green]{Markup.Escape(d.Expected)}[/] in {Markup.Escape(rel)}?", true);
            if (!apply) { skipped++; continue; }
            if (RepoVerifier.ApplyIdentityRepair(d.SourcePath, d.Field, d.Expected, ctx.Yaml)) repaired++;
            else skipped++;
        }
        AnsiConsole.MarkupLine($"[green]repaired:[/] {repaired}, [yellow]skipped:[/] {skipped}");
        return 0;
    }
}
