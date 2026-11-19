using Knit.Core.Discovery;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Output;

/// <summary>
/// Live-refresh wrapper for read-only views. Re-runs <c>render</c> on
/// item-folder changes (FileSystemWatcher) plus a periodic safety poll
/// (covers WSL bind mounts, network shares, dropped events). Debounces
/// bursts of signals via <see cref="SignalDebouncer"/>.
/// </summary>
public static class LiveView
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SafetyPoll = TimeSpan.FromSeconds(30);

    public static int Run(CliContext ctx, Func<IRenderable> render, CancellationToken ct)
    {
        var itemsDir = RepoDiscovery.ItemsDir(ctx.Root);
        Directory.CreateDirectory(itemsDir);

        using var debouncer = new SignalDebouncer(Debounce);

        using var watcher = new FileSystemWatcher(itemsDir)
        {
            IncludeSubdirectories = true,
            Filter = "*.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
        };
        watcher.Changed += (_, _) => debouncer.Signal();
        watcher.Created += (_, _) => debouncer.Signal();
        watcher.Deleted += (_, _) => debouncer.Signal();
        watcher.Renamed += (_, _) => debouncer.Signal();

        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(SafetyPoll);
            try
            {
                while (await timer.WaitForNextTickAsync(pollCts.Token))
                    debouncer.Signal();
            }
            catch (OperationCanceledException) { }
        }, pollCts.Token);

        // A repaint follows a file change, so it reads the files again.
        IRenderable Fresh()
        {
            ctx.Refresh();
            return render();
        }

        try
        {
            AnsiConsole.Live(render())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .Start(live => Loop(live.UpdateTarget, Fresh, debouncer, ct));
        }
        catch (OperationCanceledException) { }
        finally
        {
            pollCts.Cancel();
            try { pollTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            AnsiConsole.Cursor.Show(true);
        }

        return 0;
    }

    /// <summary>
    /// Live-refresh loop. Paints the initial frame immediately, then
    /// repaints on every debouncer signal until cancelled. The initial
    /// paint is the fix for the bug where AnsiConsole.Live's seeded
    /// target wasn't actually rendered until the first explicit
    /// UpdateTarget call, leaving --watch blank until a file change or
    /// the 30 s safety poll fired.
    /// Extracted from <see cref="Run"/> so it can be unit-tested without
    /// a TTY.
    /// </summary>
    public static void Loop(
        Action<IRenderable> updateTarget,
        Func<IRenderable> render,
        SignalDebouncer debouncer,
        CancellationToken ct)
    {
        updateTarget(render());
        while (!ct.IsCancellationRequested)
        {
            var fired = debouncer.WaitAsync(ct).GetAwaiter().GetResult();
            if (!fired) break;
            updateTarget(render());
        }
    }
}
