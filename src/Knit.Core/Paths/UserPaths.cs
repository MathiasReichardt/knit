using System.Runtime.InteropServices;

namespace Knit.Core.Paths;

/// <summary>
/// Per-user, outside-the-repo locations knit writes to. One place owns
/// the per-OS switch: a second hand-rolled <c>IsOSPlatform</c> elsewhere would
/// drift on exactly the details nobody notices until they bite — whether the
/// XDG variable is honoured when unset, and whether the override applies.
///
/// Deliberately <b>not</b> beside the binary: a knit on <c>PATH</c> usually
/// sits where the user cannot write without elevation, package managers and
/// <c>knit update</c> replace that directory on upgrade, one machine's users
/// would share one identity file, and two copies of the exe would read
/// different configs with nothing to say why.
/// </summary>
public static class UserPaths
{
    /// <summary>
    /// Points <see cref="ConfigDir"/> somewhere else, wholesale. Exists for
    /// tests above all — without it a test run reads the developer's real
    /// global config and stops being hermetic — and gives portable installs
    /// (knit on a stick, no machine state) an explicit switch instead of an
    /// implicit binary-relative rule.
    /// </summary>
    public const string ConfigHomeVariable = "KNIT_CONFIG_HOME";

    /// <summary>
    /// The directory holding the user-level <c>config.json</c>. Config, not
    /// cache: this is the precious half, so it follows <c>APPDATA</c> /
    /// <c>XDG_CONFIG_HOME</c> and never the disposable cache locations a later
    /// phase will add beside it here.
    /// </summary>
    public static string ConfigDir
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(ConfigHomeVariable);
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(Special(Environment.SpecialFolder.ApplicationData), "knit");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(Home(), "Library", "Application Support", "knit");

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Home(), ".config", "knit")
                : Path.Combine(xdg, "knit");
        }
    }

    public static string GlobalConfigFile => Path.Combine(ConfigDir, "config.json");

    private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Special(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(path) ? Home() : path;
    }
}
