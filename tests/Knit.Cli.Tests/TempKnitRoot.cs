using System.Text;

namespace Knit.Cli.Tests;

/// <summary>
/// Auto-cleanup temp directory used as a knit root. Captures stdout/stderr
/// while the CLI runs in-process, and restores cwd (and the env overrides it
/// pins) on dispose.
///
/// Tests using this fixture must be in the <see cref="CliCollection"/> so they
/// run sequentially — Init reads cwd, and cwd is process-global.
/// </summary>
public sealed class TempKnitRoot : IDisposable
{
    public string Path { get; }

    /// <summary>Sandboxed stand-in for the home folder agents are looked up
    /// in (<c>~/.claude</c>, <c>~/.agents</c>, <c>~/.codex</c>, …) — empty
    /// unless a test creates something in it.</summary>
    public string AgentHome { get; }

    /// <summary><c>~/.claude/skills</c> under <see cref="AgentHome"/>.</summary>
    public string GlobalSkillsPath => System.IO.Path.Combine(AgentHome, ".claude", "skills");

    /// <summary><c>~/.agents/skills</c> under <see cref="AgentHome"/>.</summary>
    public string GlobalAgentsSkillsPath => System.IO.Path.Combine(AgentHome, ".agents", "skills");

    /// <summary>Sandboxed stand-in for the user-level config directory.</summary>
    public string ConfigHome { get; }

    private readonly string _previousCwd;
    private readonly string? _previousAgentHome;
    private readonly string? _previousCodexHome;
    private readonly string? _previousKnitRoot;
    private readonly string? _previousConfigHome;

    public TempKnitRoot()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"knit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
        _previousCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path);

        // The developer's real global bundles and agent folders must never
        // influence a test run — neither as install targets nor as detection.
        AgentHome = System.IO.Path.Combine(Path, "agent-home");
        _previousAgentHome = Environment.GetEnvironmentVariable("KNIT_AGENT_HOME");
        Environment.SetEnvironmentVariable("KNIT_AGENT_HOME", AgentHome);
        _previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", System.IO.Path.Combine(AgentHome, ".codex"));

        // Pin discovery to this directory. Temp lives under the user's home on
        // Windows, so without this the upward walk escapes the fixture and can
        // find a real `.knit/` above it — a test then reads, and *writes*, the
        // developer's own repo, and "no .knit/ found" assertions fail on their
        // machine only. KNIT_ROOT resolves to null while the fixture is
        // un-inited, which is exactly the not-a-repo case tests expect.
        _previousKnitRoot = Environment.GetEnvironmentVariable("KNIT_ROOT");
        Environment.SetEnvironmentVariable("KNIT_ROOT", Path);

        // Same reason for the user-level config: it lives outside every repo,
        // so an un-pinned run would read (and `config set --global` would
        // write) the developer's own file. TestEnvironment pins the assembly
        // to an empty directory; this narrows it to one test, so a global
        // config one test writes cannot leak into the next.
        ConfigHome = System.IO.Path.Combine(Path, "global-config");
        _previousConfigHome = Environment.GetEnvironmentVariable("KNIT_CONFIG_HOME");
        Environment.SetEnvironmentVariable("KNIT_CONFIG_HOME", ConfigHome);
    }

    public CliResult Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = Program.Main(args);
            return new CliResult(exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    /// <summary><see cref="Run"/> with <paramref name="stdin"/> as the input stream.</summary>
    public CliResult RunWithInput(string stdin, params string[] args)
    {
        var prevIn = Console.In;
        Console.SetIn(new StringReader(stdin));
        try { return Run(args); }
        finally { Console.SetIn(prevIn); }
    }

    /// <summary>Write the user-level config this fixture's runs will read.</summary>
    public void WriteGlobalConfig(string json)
    {
        Directory.CreateDirectory(ConfigHome);
        File.WriteAllText(System.IO.Path.Combine(ConfigHome, "config.json"), json);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KNIT_CONFIG_HOME", _previousConfigHome);
        Environment.SetEnvironmentVariable("KNIT_AGENT_HOME", _previousAgentHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _previousCodexHome);
        Environment.SetEnvironmentVariable("KNIT_ROOT", _previousKnitRoot);
        Directory.SetCurrentDirectory(_previousCwd);
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

public readonly record struct CliResult(int ExitCode, string Stdout, string Stderr);

[CollectionDefinition(nameof(CliCollection), DisableParallelization = true)]
public sealed class CliCollection { }
