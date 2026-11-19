using System.CommandLine;
using System.Reflection;
using System.Text;
using Knit.Cli.Output;

namespace Knit.Cli.Commands;

/// <summary>
/// Prints the license texts embedded in this binary. A release artifact is a
/// bare executable with no sidecar files, but MIT/ISC/BSD all require the
/// notice to accompany a redistribution — so the notices ship inside the exe
/// and this command is how a recipient reads them.
/// </summary>
public static class LicensesCommand
{
    private const string OwnLicense = "Knit.Cli.Legal.LICENSE";
    private const string ThirdParty = "Knit.Cli.Legal.THIRD-PARTY-NOTICES.md";

    public static Command Build()
    {
        var cmd = new Command("licenses", "Print Knit's license and the notices for bundled third-party code");
        var thirdPartyOpt = new Option<bool>("--third-party") { Description = "Print only the third-party notices" };
        var outOpt = new Option<string?>("--out") { Description = "Write LICENSE and THIRD-PARTY-NOTICES.md into this directory instead of printing" };
        cmd.Options.Add(thirdPartyOpt);
        cmd.Options.Add(outOpt);
        cmd.SetAction(parse => Run(parse.GetValue(thirdPartyOpt), parse.GetValue(outOpt)));
        return cmd;
    }

    private static int Run(bool thirdPartyOnly, string? outDir)
    {
        if (outDir is not null)
        {
            if (thirdPartyOnly)
                return Out.Error("--out writes both files; drop --third-party", ErrorCode.BadArgument);
            return Write(outDir);
        }

        if (!thirdPartyOnly)
        {
            Console.WriteLine("Knit is licensed under the MIT License.");
            Console.WriteLine();
            Console.WriteLine(Read(OwnLicense).TrimEnd());
            Console.WriteLine();
            Console.WriteLine("Knit redistributes third-party code inside this binary. Their notices follow.");
            Console.WriteLine("Print them alone with: knit licenses --third-party");
            Console.WriteLine();
        }

        Console.WriteLine(Read(ThirdParty).TrimEnd());
        return 0;
    }

    private static int Write(string outDir)
    {
        string full;
        try
        {
            full = Path.GetFullPath(outDir);
            Directory.CreateDirectory(full);
            File.WriteAllText(Path.Combine(full, "LICENSE"), Read(OwnLicense));
            File.WriteAllText(Path.Combine(full, "THIRD-PARTY-NOTICES.md"), Read(ThirdParty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Out.Error($"could not write to '{outDir}': {ex.Message}", ErrorCode.Internal);
        }

        Console.WriteLine(Path.Combine(full, "LICENSE"));
        Console.WriteLine(Path.Combine(full, "THIRD-PARTY-NOTICES.md"));
        return 0;
    }

    private static string Read(string name)
    {
        var asm = typeof(LicensesCommand).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
