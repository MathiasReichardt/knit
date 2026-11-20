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

    // Only the licenses, not the file: THIRD-PARTY-NOTICES.md is regenerated per
    // release (the release job runs --check), so a plain bump passes, while a
    // copyleft or unresolved license still fails the PR that brings it in.
    [Fact]
    public void Shipped_dependencies_are_permissively_licensed()
    {
        var repoRoot = LocateRepoRoot();
        Assert.True(
            Directory.Exists(Path.Combine(repoRoot, "src", "Knit.Web", "node_modules")),
            "node_modules missing. Run `npm install` in src/Knit.Web first.");

        Run("node tools/gen-notices.mjs --check-licenses", repoRoot,
            "node tools/gen-notices.mjs --check-licenses");
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
