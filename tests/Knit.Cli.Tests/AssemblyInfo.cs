using Xunit;

// Test classes share the process, and the fixtures steer the CLI through
// process-global environment variables (`KNIT_ROOT`, `KNIT_AGENT_HOME`,
// `KNIT_CONFIG_HOME`). A class running in parallel with one that has pointed
// those somewhere else reads the other's repo — the user-level config
// made that visible, since every command now reads a file outside the repo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Knit.Cli.Tests;

/// <summary>
/// The user-level config lives outside every repo, so a test that does
/// not pin it reads the developer's real <c>handle</c>, <c>due_soon_days</c>
/// and mounts — and a `config set --global` would write them. <see
/// cref="TempKnitRoot"/> pins it per test; this pins the assembly, for the
/// classes that build a repo without the fixture (the <c>WebApi*</c> suites).
/// </summary>
public static class TestEnvironment
{
    public static string ConfigHome { get; private set; } = "";

    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Isolate()
    {
        // macOS: TMPDIR lies under /var, a symlink to /private/var. The CLI
        // reads the working directory back resolved, so a fixture path would
        // never equal the root it reports.
        if (OperatingSystem.IsMacOS())
            Environment.SetEnvironmentVariable("TMPDIR", Resolved(Path.GetTempPath()));

        ConfigHome = Path.Combine(Path.GetTempPath(), $"knit-cli-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ConfigHome);
        Environment.SetEnvironmentVariable("KNIT_CONFIG_HOME", ConfigHome);

        // Wide enough that no line wraps — some messages carry two absolute
        // temp paths, and a runner's are long — so assertions don't depend on
        // the width of the console the tests run in (none on CI → 80).
        Knit.Cli.Output.Out.WidthOverride = 1000;
    }

    /// <summary><paramref name="path"/> with every symlinked segment resolved.</summary>
    private static string Resolved(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                current = target.FullName;
        }
        return current + Path.DirectorySeparatorChar;
    }
}
