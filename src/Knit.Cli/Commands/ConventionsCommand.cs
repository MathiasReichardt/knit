using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Config;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit conventions</c> — the repo's and the user's rules for agents.
/// Read prints the text raw: its reader is an agent, and markup would only
/// need escaping out of the markdown again.
/// </summary>
public static class ConventionsCommand
{
    private const string Precedence =
        "Repo conventions win a conflict with user conventions; both override the Knit skills' guidance, no other skill's.";

    public static Command Build()
    {
        var cmd = new Command("conventions",
            "Print this repo's and your own conventions for agents — read once per session before the first write");
        var scopeOpt = ScopeOption("Print one scope only (default: user, then repo)");
        cmd.Options.Add(scopeOpt);
        cmd.Subcommands.Add(BuildSet());
        cmd.Subcommands.Add(BuildRemove());
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var raw = parse.GetValue(scopeOpt);
            if (raw is not null && !Conventions.TryParse(raw, out _)) return BadScope(raw, json);
            var files = raw is null
                ? Conventions.ReadAll(ctx.Root)
                : [Conventions.Read(Parse(raw), ctx.Root)];

            if (json)
            {
                Out.Json([.. files.Select(ToJson)], AgentJsonContext.Default.ListJsonConventions);
                return 0;
            }

            var present = files.Where(f => f.Exists).ToList();
            if (present.Count == 0)
            {
                Console.WriteLine("No conventions — " +
                                  string.Join(", ", files.Select(f => $"{f.Scope.ToWire()}: {f.Path}")));
                return 0;
            }
            if (present.Count > 1) Console.WriteLine(Precedence + "\n");
            for (var i = 0; i < present.Count; i++)
            {
                if (i > 0) Console.WriteLine();
                Console.WriteLine($"# {present[i].Scope.ToWire()} conventions — {present[i].Path}\n");
                Console.Write(present[i].Text);
            }
            return 0;
        });
        return cmd;
    }

    private static Command BuildSet()
    {
        var c = new Command("set", "Replace one scope's conventions with the text on stdin (empty input removes the file)");
        var scopeOpt = ScopeOption("repo (committed, shared) or user (this machine, every repo)");
        scopeOpt.Required = true;
        c.Options.Add(scopeOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var raw = parse.GetValue(scopeOpt)!;
            if (!Conventions.TryParse(raw, out var scope)) return BadScope(raw, json);
            if (!Console.IsInputRedirected)
                return Out.Error(
                    $"pipe the text in, e.g. `knit conventions set --scope {scope.ToWire()} < conventions.md`",
                    ErrorCode.BadArgument, json);

            var file = Conventions.Write(scope, ctx.Root, Console.In.ReadToEnd());
            if (file.LineCount > Conventions.LongLineCount)
                Console.Error.WriteLine(
                    $"note: {file.LineCount} lines — every agent run reads them; keep conventions under {Conventions.LongLineCount}");
            if (json)
                Out.Json(ToJson(file), AgentJsonContext.Default.JsonConventions);
            else
                Console.WriteLine(file.Exists
                    ? $"wrote {file.Scope.ToWire()} conventions → {file.Path}"
                    : $"removed {file.Scope.ToWire()} conventions ({file.Path})");
            return 0;
        });
        return c;
    }

    private static Command BuildRemove()
    {
        var c = new Command("remove", "Delete one scope's conventions file");
        var scopeOpt = ScopeOption("repo (committed, shared) or user (this machine, every repo)");
        scopeOpt.Required = true;
        c.Options.Add(scopeOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var raw = parse.GetValue(scopeOpt)!;
            if (!Conventions.TryParse(raw, out var scope)) return BadScope(raw, json);

            var existed = Conventions.Read(scope, ctx.Root).Exists;
            var file = Conventions.Remove(scope, ctx.Root);
            if (json)
                Out.Json(ToJson(file), AgentJsonContext.Default.JsonConventions);
            else
                Console.WriteLine(existed
                    ? $"removed {scope.ToWire()} conventions ({file.Path})"
                    : $"no {scope.ToWire()} conventions to remove ({file.Path})");
            return 0;
        });
        return c;
    }

    private static Option<string> ScopeOption(string description) => new("--scope") { Description = description };

    private static ConventionsScope Parse(string raw) => Conventions.TryParse(raw, out var s) ? s : default;

    private static int BadScope(string raw, bool json) =>
        Out.Error($"unknown scope '{raw}' — use repo or user", ErrorCode.BadArgument, json);

    private static JsonConventions ToJson(ConventionsFile f) => new(f.Scope.ToWire(), f.Path, f.Exists, f.Text);
}
