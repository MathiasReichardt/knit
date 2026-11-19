using System.CommandLine;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Data.StatusPage;
using Knit.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

/// <summary>
/// The PM close-out view: one command that answers "how is this going".
/// Scope flags only (project / milestone / slate / under) — a state filter
/// would make the completion percentage a tautology, so `status` doesn't take
/// one. Narrow with `list` when you want the rows instead of the rollup.
/// </summary>
public static class StatusCommand
{
    private const int AttentionCap = 10;

    public static Command Build()
    {
        var cmd = new Command("status", "Rollup for a scope: completion, per-assignee load, blocked + overdue");
        var projectOpt = new Option<string?>("--project") { Description = "Scope to a project doc folder (slug under .knit/projects/); items whose nearest project ancestor points there." };
        var milestoneOpt = new Option<string?>("--milestone") { Description = "Scope to items linked to this milestone: <slug> (unambiguous across projects) or <project>/<slug>." };
        var slateOpt = new Option<string?>("--slate") { Description = "Scope to a slate. Inherited: matches items whose nearest ancestor with a slate has it." };
        var underOpt = new Option<string?>("--under") { Description = "Scope to the subtree rooted at this item id (or unique prefix), including the item itself." };
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(milestoneOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(underOpt);
        var watchOpt = Watch.Option();
        cmd.Options.Add(watchOpt);
        cmd.Subcommands.Add(BuildReport());

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            var project = parse.GetValue(projectOpt);
            var milestone = parse.GetValue(milestoneOpt);
            var slate = parse.GetValue(slateOpt);
            var under = parse.GetValue(underOpt);

            string? underId = null, underShort = null;
            if (!string.IsNullOrEmpty(under))
            {
                var (root, err) = ItemUtils.Resolve(ctx, under);
                if (root is null) return Out.Error(err!, ErrorCode.NotFound, json);
                underId = root.Id;
                underShort = Out.Short(ctx, root.Id);
            }

            var label = StatusRollup.Label(project, slate, milestone, underShort);
            var today = DateOnly.FromDateTime(DateTime.Now);

            (StatusReport? report, string? error) Build()
            {
                var graph = new LinkGraph(ctx.Store.AllItems().ToList());
                var (scoped, scopeErr) = StatusRollup.Scope(ctx.Store, ctx.Root, graph, project, slate, milestone, underId);
                if (scopeErr is not null) return (null, scopeErr);
                return (StatusRollup.Compute(scoped, today, label), null);
            }

            if (json)
            {
                var (report, err) = Build();
                if (report is null) return Out.Error(err!, ErrorCode.BadArgument, json);
                Out.Json(ToJson(ctx, report, today), AgentJsonContext.Default.JsonStatus);
                return 0;
            }

            // Scope errors have to surface before the live view takes over the
            // screen — a bad --project inside a repaint loop is unreadable.
            var (probe, probeErr) = Build();
            if (probe is null) return Out.Error(probeErr!, ErrorCode.BadArgument, json);

            IRenderable Render()
            {
                var (r, _) = Build();
                return r is null ? new Markup("[dim](no items)[/]") : Report(ctx, r, today);
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// `status report`: the generated page for readers who don't use Knit.
    /// One repo-wide report, so no scope flags. Knit writes the file and never
    /// commits it.
    /// </summary>
    private static Command BuildReport()
    {
        var cmd = new Command("report", "Write the status page (project-status.md) for readers who don't use Knit");
        var outOpt = new Option<string?>("--out") { Description = "Write here instead of status_report.path, relative to the repo root; must stay inside the repo." };
        var stdoutOpt = new Option<bool>("--stdout") { Description = "Print the page instead of writing it." };
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite a target that lacks the generated marker (a hand-written file)." };
        cmd.Options.Add(outOpt);
        cmd.Options.Add(stdoutOpt);
        cmd.Options.Add(forceOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var toStdout = parse.GetValue(stdoutOpt);
            var outPath = parse.GetValue(outOpt);
            if (toStdout && json)
                return Out.Error("--stdout prints the page itself; it has no --json form", ErrorCode.BadArgument, json);
            if (toStdout && outPath is not null)
                return Out.Error("--out and --stdout are exclusive — pick where the page goes", ErrorCode.BadArgument, json);

            var (target, err) = StatusPageFile.Resolve(ctx.Root, outPath ?? ctx.Config.StatusReportPath);
            if (target is null)
                return Out.Error(outPath is null ? $"status_report.path: {err}" : $"--out: {err}", ErrorCode.BadArgument, json);

            var page = StatusPageFile.Render(
                ctx.Root, ctx.Store, ctx.Yaml, ctx.Config, ctx.Resolution.GitRoot,
                DateOnly.FromDateTime(DateTime.Now), target, ctx.Shortener.Short);
            if (toStdout)
            {
                Console.Out.Write(page);
                return 0;
            }

            if (StatusPageFile.Write(target, page, parse.GetValue(forceOpt)) == StatusPageWrite.NotGenerated)
                return Out.Error(
                    $"{target} exists and was not written by knit (no generated marker on its first line) — " +
                    "rerun with --force to replace it, or pick another path with --out",
                    ErrorCode.Conflict, json);

            if (json) Out.Json(new JsonStatusReport(target, true), AgentJsonContext.Default.JsonStatusReport);
            else Console.WriteLine($"wrote {target}");
            return 0;
        });
        return cmd;
    }

    private static IRenderable Report(CliContext ctx, StatusReport r, DateOnly today)
    {
        // Stated in the rollup itself, not only on stderr: these numbers are
        // the redirect target's, and reading them as this checkout's is the
        // mistake a redirect invites.
        var redirect = ctx.Resolution.Redirected
            ? new Markup(
                $"[dim]knit root[/] {Markup.Escape(ctx.Root)} " +
                $"[dim]— redirected from {Markup.Escape(ctx.Resolution.Checkout!)}[/]")
            // A declared shared root answered with "." — kept visible, so a
            // local choice is never mistaken for the project's tracker.
            : Requirements.KnitRootOverriddenLocally(ctx.Resolution)
                ? new Markup("[dim]knit root[/] local [dim](declared external, overridden here)[/]")
                : null;

        var page = PageHint(ctx, today);
        if (r.Total == 0)
            return new Rows(new IRenderable?[]
            {
                redirect,
                new Markup($"[bold]{Markup.Escape(r.Scope)}[/]"),
                new Markup("[dim](no items in scope)[/]"),
                page,
            }.OfType<IRenderable>());

        var blocks = new List<IRenderable>();
        if (redirect is not null) blocks.Add(redirect);
        blocks.AddRange(new IRenderable[]
        {
            new Markup($"[bold]{Markup.Escape(r.Scope)}[/]{HealthFormat.Suffix(r.Health)}"),
            new Markup($"{Bar(r.Percent)} [bold]{r.Percent}%[/]"),
            new Markup(Headline(r)),
        });

        if (r.ByAssignee.Count > 0)
        {
            var t = new Table().Border(TableBorder.Rounded).Title("[dim]load[/]");
            t.AddColumns("assignee", "open", "in progress", "todo", "done", "blocked", "overdue");
            foreach (var a in r.ByAssignee)
                t.AddRow(
                    Out.Prose(string.IsNullOrEmpty(a.Assignee) ? "(unassigned)" : a.Assignee),
                    new Markup(Num(a.InProgress + a.Todo)),
                    new Markup(Num(a.InProgress)),
                    new Markup(Num(a.Todo)),
                    new Markup(Num(a.Done)),
                    new Markup(Num(a.Blocked)),
                    new Markup(Num(a.Overdue)));
            blocks.Add(t);
        }

        if (r.BlockedItems.Count > 0) blocks.Add(Attention(ctx, "blocked", r.BlockedItems, today, blockedReason: true));
        if (r.OverdueItems.Count > 0) blocks.Add(Attention(ctx, "overdue", r.OverdueItems, today, blockedReason: false));
        if (page is not null) blocks.Add(page);

        return new Rows(blocks);
    }

    /// <summary>One dim line when the generated page is stale; nothing when there is no page.</summary>
    private static Markup? PageHint(CliContext ctx, DateOnly today)
    {
        if (StatusPageFile.Age(ctx.Root, ctx.Config, today) is not { Stale: true } age) return null;
        var path = Markup.Escape(Path.GetRelativePath(ctx.Root, age.Path));
        return new Markup(age.Generated is { } g
            ? $"[dim]status page {path} is {Days(today.DayNumber - g.DayNumber)} old — knit status report[/]"
            : $"[dim]status page {path} has no Generated date — knit status report[/]");
    }

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    private static string Headline(StatusReport r)
    {
        var parts = new List<string>
        {
            $"{r.Total} item{(r.Total == 1 ? "" : "s")}",
            $"{r.Done} done",
            $"{r.InProgress} in progress",
            $"{r.Todo} todo",
        };
        if (r.Review > 0) parts.Add($"{r.Review} in review");
        if (r.Draft > 0) parts.Add($"{r.Draft} draft");
        if (r.Dismissed > 0) parts.Add($"[dim]{r.Dismissed} dismissed[/]");
        var attention = $"⊘ {Num(r.Blocked)} blocked   ◷ {Num(r.Overdue)} overdue   ◯ {Num(r.Unassigned)} unassigned";
        return $"[dim]{string.Join(" · ", parts)}[/]\n{attention}";
    }

    private static IRenderable Attention(
        CliContext ctx, string heading, IReadOnlyList<Item> items, DateOnly today, bool blockedReason)
    {
        var t = new Table().Border(TableBorder.Rounded).Title($"[dim]{heading}[/]");
        t.AddColumns("id", "priority", "title", "assignee", blockedReason ? "reason" : "due");
        foreach (var i in items.Take(AttentionCap))
            t.AddRow(
                new Markup(Out.Short(ctx, i.Id)),
                new Markup(Markup.Escape(PriorityFormat.Cell(i.Priority))),
                Out.Prose(i.Title),
                Out.Prose(i.Assignee),
                blockedReason
                    ? Out.Prose(i.BlockedSummary)
                    : new Markup(DueFormat.Cell(i.Due, i.State, today, ctx.Config.DueSoonDays)));
        if (items.Count > AttentionCap)
            return new Rows(t, new Markup($"[dim]  +{items.Count - AttentionCap} more — knit list --{heading}[/]"));
        return t;
    }

    /// <summary>
    /// Zero renders as a dim dash: a `0` still pulls the eye on a line whose
    /// whole job is to show where attention is needed.
    /// </summary>
    private static string Num(int n) => n == 0 ? "[dim]—[/]" : n.ToString();

    private static string Bar(int percent)
    {
        const int width = 24;
        var filled = (int)Math.Round(percent / 100.0 * width, MidpointRounding.AwayFromZero);
        return $"[green]{new string('█', filled)}[/][dim]{new string('░', width - filled)}[/]";
    }

    private static JsonStatus ToJson(CliContext ctx, StatusReport r, DateOnly today) => new(
        Scope: r.Scope,
        Total: r.Total,
        Done: r.Done,
        InProgress: r.InProgress,
        Review: r.Review,
        Todo: r.Todo,
        Draft: r.Draft,
        Dismissed: r.Dismissed,
        Blocked: r.Blocked,
        Overdue: r.Overdue,
        Unassigned: r.Unassigned,
        Percent: r.Percent,
        Health: r.Health?.ToWire(),
        ByAssignee: r.ByAssignee
            .Select(a => new JsonAssigneeLoad(
                string.IsNullOrEmpty(a.Assignee) ? null : a.Assignee,
                a.Total, a.Done, a.InProgress, a.Todo, a.Blocked, a.Overdue))
            .ToList(),
        BlockedItems: r.BlockedItems.Select(i => AgentMapper.Map(i, ctx)).ToList(),
        OverdueItems: r.OverdueItems.Select(i => AgentMapper.Map(i, ctx)).ToList(),
        RedirectedFrom: ctx.Resolution.Redirected ? ctx.Resolution.Checkout : null,
        LocalOverride: Requirements.KnitRootOverriddenLocally(ctx.Resolution),
        StatusPage: StatusPageFile.Age(ctx.Root, ctx.Config, today) is { } age
            ? new JsonStatusPage(age.Path, age.Generated is { } g ? DueDates.ToWire(g) : null, age.Stale)
            : null);
}
