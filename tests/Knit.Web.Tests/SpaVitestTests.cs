using System.Diagnostics;
using System.Text;
using Xunit;

namespace Knit.Web.Tests;

// Bridges the SPA's vitest suite + eslint gate into Test Explorer /
// `dotnet test` so a solution-wide "Run All Tests" covers the React app too.
// Thin wrappers: each shells out to an `npm` script in src/Knit.Web and fails
// with the tool's own output on a non-zero exit. Requires Node + an installed
// node_modules (run `npm install` in src/Knit.Web once).
public class SpaVitestTests
{
    [Fact]
    public void Spa_vitest_suite_passes() => RunNpmScript("test", "vitest");

    [Fact]
    public void Spa_lint_clean() => RunNpmScript("lint", "eslint");

    // THIRD-PARTY-NOTICES.md is embedded in the CLI binary and is a legal
    // artifact, so a dependency change that isn't reflected there is a defect,
    // not a chore. Nothing else would notice.
    [Fact]
    public void Third_party_notices_match_the_lockfile()
    {
        var repoRoot = LocateRepoRoot();
        Assert.True(
            Directory.Exists(Path.Combine(repoRoot, "src", "Knit.Web", "node_modules")),
            "node_modules missing. Run `npm install` in src/Knit.Web first.");

        Run("node tools/gen-npm-notices.mjs --check", repoRoot,
            "node tools/gen-npm-notices.mjs --check");
    }

    private static void RunNpmScript(string script, string tool)
    {
        var webDir = LocateWebDir();
        var nodeModules = Path.Combine(webDir, "node_modules", tool);
        Assert.True(
            Directory.Exists(nodeModules),
            $"{tool} not installed in {webDir}. Run `npm install` there first.");

        Run($"npm run {script}", webDir, $"npm run {script}");
    }

    private static void Run(string command, string workingDir, string label)
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/sh", $"-c \"{command}\"");

        var psi = new ProcessStartInfo(fileName, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var output = new StringBuilder();
        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to launch npm. Is Node.js on PATH?", ex);
        }

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(180_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException($"{label} did not finish within 180s.");
        }
        proc.WaitForExit(); // flush async readers

        Assert.True(
            proc.ExitCode == 0,
            $"{label} failed (exit {proc.ExitCode}):\n{output}");
    }

    private static string LocateWebDir() => Path.Combine(LocateRepoRoot(), "src", "Knit.Web");

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Knit.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir;
    }
}
