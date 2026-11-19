using System.Diagnostics;
using Knit.Cli.Output;
using Knit.Core.IO;

namespace Knit.Cli.Tests;

/// <summary>
/// The repo lock around multi-file mutations and the traces an
/// interrupted write leaves, seen through the CLI.
/// </summary>
[Collection(nameof(CliCollection))]
public class ConcurrencyCliTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void A_multi_file_mutation_refuses_while_a_live_knit_holds_the_lock_and_names_it()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "rename me").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        using (RepoLock.Acquire(root.Path, "kb split"))
        {
            var r = root.Run("move", id, "renamed");
            Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
            Assert.Contains($"(pid {Environment.ProcessId}) running 'kb split'", r.Stderr);
            // Single-item writes never take the lock.
            Assert.Equal(0, root.Run("set", id, "priority=critical").ExitCode);
        }
    }

    [Fact]
    public void A_lock_left_by_a_dead_knit_is_broken_with_a_note()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "rename me").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        File.WriteAllText(RepoLock.PathFor(root.Path),
            $$"""{"host":"{{Environment.MachineName}}","pid":{{DeadPid()}},"operation":"relink","since":"{{DateTimeOffset.UtcNow:O}}"}""");

        var r = root.Run("move", id, "renamed");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("note: broke a stale repo lock left by", r.Stderr);
        Assert.False(File.Exists(RepoLock.PathFor(root.Path)));
    }

    [Fact]
    public void Verify_flags_a_temp_file_an_interrupted_write_left_behind()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "seed").ExitCode);
        var temp = Path.Combine(root.Path, ".knit", "items", $"{AtomicFile.TempPrefix}0123{AtomicFile.TempSuffix}");
        File.WriteAllText(temp, "partial");

        var r = root.Run("verify", "--json");

        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"stray_files\":[{", r.Stdout);
        Assert.Contains("interrupted", r.Stdout);
    }

    [Fact]
    public void The_lock_file_is_gitignored()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Contains(RepoLock.FileName, File.ReadAllLines(Path.Combine(root.Path, ".knit", ".gitignore")));
    }

    private static int DeadPid()
    {
        var live = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        var pid = 4_000_000;
        while (live.Contains(pid)) pid++;
        return pid;
    }
}
