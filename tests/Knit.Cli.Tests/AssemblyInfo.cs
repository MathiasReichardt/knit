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
        ConfigHome = Path.Combine(Path.GetTempPath(), $"knit-cli-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ConfigHome);
        Environment.SetEnvironmentVariable("KNIT_CONFIG_HOME", ConfigHome);
    }
}
