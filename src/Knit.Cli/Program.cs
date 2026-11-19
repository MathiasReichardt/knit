using System.CommandLine;
using System.Text;
using Knit.Cli.Commands;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        ConfigureAnsi(args);

        try { return Run(args); }
        catch (Exception ex) { return ReportInternal(ex, args); }
    }

    private static int Run(string[] args)
    {

        var root = new RootCommand("knit — git-native task tracker");
        root.Options.Add(GlobalOptions.Root);
        root.Options.Add(GlobalOptions.Verbose);
        root.Options.Add(GlobalOptions.Json);
        root.Options.Add(GlobalOptions.NoColor);

        root.Subcommands.Add(InitCommand.Build());
        root.Subcommands.Add(AddCommand.Build());
        root.Subcommands.Add(ShowCommand.Build());
        root.Subcommands.Add(ListCommand.Build());
        root.Subcommands.Add(TreeCommand.Build());
        root.Subcommands.Add(SearchCommand.Build());
        root.Subcommands.Add(ReadyCommand.Build());
        root.Subcommands.Add(TimelineCommand.Build());
        root.Subcommands.Add(StatusCommand.Build());
        root.Subcommands.Add(StateCommands.Start());
        root.Subcommands.Add(StateCommands.Submit());
        root.Subcommands.Add(StateCommands.Done());
        root.Subcommands.Add(StateCommands.Reopen());
        root.Subcommands.Add(StateCommands.Approve());
        root.Subcommands.Add(StateCommands.Block());
        root.Subcommands.Add(StateCommands.Unblock());
        root.Subcommands.Add(StateCommands.Dismiss());
        root.Subcommands.Add(StateCommands.Assign());
        root.Subcommands.Add(HandlesCommand.Build());
        root.Subcommands.Add(DeleteCommand.Build());
        root.Subcommands.Add(EditCommands.Set());
        root.Subcommands.Add(EditCommands.Reorder());
        root.Subcommands.Add(EditCommands.Edit());
        root.Subcommands.Add(EditCommands.Comment());
        root.Subcommands.Add(LinkCommands.Link());
        root.Subcommands.Add(LinkCommands.Unlink());
        root.Subcommands.Add(PickCommand.Build());
        root.Subcommands.Add(SkillInstallCommand.Build());
        root.Subcommands.Add(KbCommand.Build());
        root.Subcommands.Add(DesignCommand.Build());
        root.Subcommands.Add(ProjectCommand.Build());
        root.Subcommands.Add(SlateCommand.Build());
        root.Subcommands.Add(MountCommand.Build());
        root.Subcommands.Add(ConfigCommand.Build());
        root.Subcommands.Add(ConventionsCommand.Build());
        root.Subcommands.Add(KnitRootCommand.Build());

        root.Subcommands.Add(MoveCommand.Build());
        root.Subcommands.Add(RetypeCommand.Build());
        root.Subcommands.Add(VerifyCommand.Build());
        root.Subcommands.Add(RelinkCommand.Build());
        root.Subcommands.Add(ContextCommand.Build());
        root.Subcommands.Add(NextCommand.Build());
        root.Subcommands.Add(RefShowCommand.Build());
        root.Subcommands.Add(UrlCommand.Build());
        root.Subcommands.Add(VersionCommand.Build());
        root.Subcommands.Add(LicensesCommand.Build());
        root.Subcommands.Add(WebCommand.Build());

        VersionCommand.RouteBuiltInOption(root);

        var parse = root.Parse(args);
        if (UnknownCommandGuard.Check(parse) is int unknown) return unknown;
        if (FormatGuard.Check(parse, args) is int refused) return refused;
        if (RequiresGuard.Check(parse, args) is int unmet) return unmet;
        return parse.Invoke();
    }

    /// <summary>
    /// Unhandled-exception sink. Surfaces a user-facing message tagged
    /// <c>[internal]</c>, never the raw stack. Set <c>KNIT_DEBUG=1</c> to
    /// also print the trace to stderr — see README §Troubleshooting.
    /// </summary>
    private static int ReportInternal(Exception ex, string[] args)
    {
        var json = args.Contains("--json");
        var msg = $"{ex.GetType().Name}: {ex.Message}";
        var exit = Out.Error(msg, ErrorCode.Internal, json);
        if (Environment.GetEnvironmentVariable("KNIT_DEBUG") == "1")
            Console.Error.WriteLine(ex);
        return exit;
    }

    /// <summary>
    /// Strip ANSI styling from stdout when output is redirected, when
    /// <c>NO_COLOR</c> is set (https://no-color.org), when <c>--json</c>
    /// is requested, or when <c>--no-color</c> is passed. Spectre.Console
    /// auto-detects redirection in most cases, but pinning the profile
    /// here makes the contract explicit and AOT-safe.
    /// </summary>
    private static void ConfigureAnsi(string[] args)
    {
        var disable =
            Console.IsOutputRedirected ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) ||
            args.Contains("--no-color") ||
            args.Contains("--json");
        if (!disable) return;
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(Console.Out),
        });
    }
}
