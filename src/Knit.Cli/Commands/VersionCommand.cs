using System.CommandLine;
using System.CommandLine.Invocation;
using Knit.Core;

namespace Knit.Cli.Commands;

public static class VersionCommand
{
    public static Command Build()
    {
        var cmd = new Command("version", "Print the knit CLI version");
        cmd.SetAction(_ =>
        {
            Console.WriteLine(Line());
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// Point System.CommandLine's built-in <c>--version</c> at the same line
    /// <c>knit version</c> prints. Its own action reads the raw
    /// <c>AssemblyInformationalVersion</c>, so the two spellings would
    /// otherwise disagree whenever the SDK decorates that attribute.
    /// </summary>
    public static void RouteBuiltInOption(RootCommand root)
    {
        foreach (var opt in root.Options)
            if (opt is VersionOption v)
                v.Action = new PrintAction();
    }

    private static string Line() => VersionInfo.Get(typeof(VersionCommand).Assembly);

    private sealed class PrintAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            Console.WriteLine(Line());
            return 0;
        }
    }
}
