using System.Runtime.CompilerServices;
using Knit.Core.Paths;

namespace Knit.Core.Tests;

/// <summary>
/// The user-level config lives outside every repo, so without this a
/// test run reads the developer's own <c>handle</c> / mounts and passes or
/// fails depending on whose machine it is on. Pointed at an empty directory
/// for the whole assembly; tests that need a global layer write one there.
/// </summary>
public static class TestEnvironment
{
    public static string ConfigHome { get; private set; } = "";

    [ModuleInitializer]
    public static void Isolate()
    {
        ConfigHome = Path.Combine(Path.GetTempPath(), $"knit-core-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ConfigHome);
        Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, ConfigHome);
    }
}
