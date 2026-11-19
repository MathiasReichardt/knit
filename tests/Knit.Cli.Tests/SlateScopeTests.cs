using System.Text.Json;
using Knit.Cli.Commands;

namespace Knit.Cli.Tests;

/// <summary><c>--slate</c> / <c>--no-slate</c> on <c>next</c>, <c>ready</c> and <c>pick</c>.</summary>
[Collection(nameof(CliCollection))]
public class SlateScopeTests
{
    // epic (q1) → feature (inherits) → t1 (inherits), t3 (inherits), t2 (own: q2); loose (none)
    private sealed record Repo(TempKnitRoot Root, string Epic, string Feature, string T1, string T2, string T3, string Loose) : IDisposable
    {
        public void Dispose() => Root.Dispose();
    }

    private static Repo Seed()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "q1").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "q2").ExitCode);
        var epic = Add(root, "epic", "epic", "--slate", "q1");
        var feature = Add(root, "feature", "feature", "--parent", epic);
        var t1 = Add(root, "task", "t1", "--parent", feature);
        var t2 = Add(root, "task", "t2", "--parent", feature, "--slate", "q2");
        var t3 = Add(root, "task", "t3", "--parent", feature);
        var loose = Add(root, "task", "loose");
        return new Repo(root, epic, feature, t1, t2, t3, loose);
    }

    private static string Add(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["add", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static string? Next(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["next", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, r.Stderr);
        var el = JsonDocument.Parse(r.Stdout).RootElement;
        return el.ValueKind == JsonValueKind.Null ? null : el.GetProperty("id").GetString();
    }

    private static string[] Ready(TempKnitRoot root, params string[] args)
    {
        var r = root.Run(["ready", .. args, "--json"]);
        Assert.True(r.ExitCode == 0, r.Stderr);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!).Order().ToArray();
    }

    private static string[] Sorted(params string[] ids) => ids.Order().ToArray();

    [Fact]
    public void Ready_matches_the_effective_slate_and_a_child_override()
    {
        using var s = Seed();
        Assert.Equal(Sorted(s.Epic, s.Feature, s.T1, s.T3), Ready(s.Root, "--slate", "q1"));
        Assert.Equal([s.T2], Ready(s.Root, "--slate", "q2"));
        Assert.Equal([s.Loose], Ready(s.Root, "--no-slate"));
        Assert.Equal(Sorted(s.Feature, s.T1, s.T3), Ready(s.Root, "--slate", "q1", "--under", s.Feature));
    }

    [Fact]
    public void Next_stays_inside_the_slate_and_still_prefers_in_progress()
    {
        using var s = Seed();
        Assert.Equal(0, s.Root.Run("start", s.Loose).ExitCode);
        Assert.Equal(0, s.Root.Run("start", s.T3).ExitCode);

        Assert.Equal(s.T3, Next(s.Root, "--slate", "q1"));
        Assert.Equal(s.T2, Next(s.Root, "--slate", "q2"));
        Assert.Equal(s.Loose, Next(s.Root, "--no-slate"));
        Assert.Equal(s.T2, Next(s.Root, "--slate", "q2", "--under", s.Epic));
        Assert.Null(Next(s.Root, "--slate", "q1", "--under", s.T2));
    }

    [Theory]
    [InlineData("next")]
    [InlineData("ready")]
    public void Slate_and_no_slate_are_exclusive(string command)
    {
        using var s = Seed();
        var r = s.Root.Run(command, "--slate", "q1", "--no-slate");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--no-slate", r.Stderr);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("ready")]
    [InlineData("list")]
    [InlineData("tree")]
    public void An_unknown_slate_errors_instead_of_matching_nothing(string command)
    {
        using var s = Seed();
        var r = s.Root.Run(command, "--slate", "q3");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no slate 'q3'", r.Stderr);
        Assert.Contains("knit slate list", r.Stderr);
    }

    [Fact]
    public void A_slate_only_on_items_is_known()
    {
        using var s = Seed();
        Assert.Equal(0, s.Root.Run("set", s.Loose, "slate=adhoc", "--force").ExitCode);
        Assert.Equal(s.Loose, Next(s.Root, "--slate", "adhoc"));
    }

    [Fact]
    public void Pick_offers_only_the_slate_s_items()
    {
        using var s = Seed();
        var ctx = CliContext.TryLoad(s.Root.Path)!;

        var (q1, _, _) = PickCommand.Candidates(ctx, new PickCommand.PickFilter(Slate: "q1"));
        Assert.Equal(Sorted(s.Epic, s.Feature, s.T1, s.T3), q1!.Select(i => i.Id).Order().ToArray());

        var (tasks, _, _) = PickCommand.Candidates(ctx, new PickCommand.PickFilter(Type: "task", Under: s.Feature, Slate: "q1"));
        Assert.Equal(Sorted(s.T1, s.T3), tasks!.Select(i => i.Id).Order().ToArray());

        var (none, _, _) = PickCommand.Candidates(ctx, new PickCommand.PickFilter(NoSlate: true));
        Assert.Equal([s.Loose], none!.Select(i => i.Id).ToArray());

        var (unknown, error, _) = PickCommand.Candidates(ctx, new PickCommand.PickFilter(Slate: "q3"));
        Assert.Null(unknown);
        Assert.Contains("no slate 'q3'", error);

        var (badState, stateError, _) = PickCommand.Candidates(ctx, new PickCommand.PickFilter(State: "doing"));
        Assert.Null(badState);
        Assert.Contains("unknown state", stateError);
    }
}
