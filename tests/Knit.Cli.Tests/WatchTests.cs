using System.Diagnostics;
using Knit.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Tests;

public class SignalDebouncerTests
{
    [Fact]
    public async Task Debouncer_coalesces_burst_into_single_signal()
    {
        using var d = new SignalDebouncer(TimeSpan.FromMilliseconds(50));
        for (var i = 0; i < 10; i++) d.Signal();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        var fired = await d.WaitAsync(cts.Token);
        sw.Stop();

        Assert.True(fired);
        Assert.True(sw.ElapsedMilliseconds >= 40, $"debounce window honoured ({sw.ElapsedMilliseconds} ms)");

        // No further signals after burst -> second wait must not return.
        using var quietCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var fired2 = await d.WaitAsync(quietCts.Token);
        Assert.False(fired2);
    }

    [Fact]
    public async Task Debouncer_signals_again_after_drain()
    {
        using var d = new SignalDebouncer(TimeSpan.FromMilliseconds(20));
        d.Signal();
        Assert.True(await d.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token));

        d.Signal();
        Assert.True(await d.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token));
    }

    [Fact]
    public async Task Debouncer_cancellation_returns_false()
    {
        using var d = new SignalDebouncer(TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var fired = await d.WaitAsync(cts.Token);
        Assert.False(fired);
    }
}

public class LiveViewLoopTests
{
    [Fact]
    public async Task Loop_paints_initial_frame_before_any_signal()
    {
        // Regression: --watch was blank until a FileSystemWatcher
        // event or the 30 s safety poll fired. Loop must call updateTarget
        // once at startup so the seeded Live target is actually rendered.
        using var d = new SignalDebouncer(TimeSpan.FromSeconds(10));
        var calls = 0;
        IRenderable Render() { Interlocked.Increment(ref calls); return new Text("x"); }

        using var cts = new CancellationTokenSource();
        var task = Task.Run(() => LiveView.Loop(_ => { }, Render, d, cts.Token));

        // Wait until the initial render fires, with a generous ceiling.
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref calls) == 0 && sw.ElapsedMilliseconds < 1000)
            await Task.Delay(10);

        Assert.True(Volatile.Read(ref calls) >= 1,
            $"expected at least one initial paint within 1 s, got {calls}");

        cts.Cancel();
        await task;
    }

    [Fact]
    public async Task Loop_repaints_on_signal()
    {
        using var d = new SignalDebouncer(TimeSpan.FromMilliseconds(20));
        var calls = 0;
        IRenderable Render() { Interlocked.Increment(ref calls); return new Text("x"); }

        using var cts = new CancellationTokenSource();
        var task = Task.Run(() => LiveView.Loop(_ => { }, Render, d, cts.Token));

        // Wait for the initial paint, then signal and expect a second paint.
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref calls) == 0 && sw.ElapsedMilliseconds < 1000)
            await Task.Delay(10);
        var afterInitial = Volatile.Read(ref calls);

        d.Signal();
        sw.Restart();
        while (Volatile.Read(ref calls) <= afterInitial && sw.ElapsedMilliseconds < 1000)
            await Task.Delay(10);

        Assert.True(Volatile.Read(ref calls) > afterInitial,
            $"expected repaint after signal, calls={calls} afterInitial={afterInitial}");

        cts.Cancel();
        await task;
    }
}

[Collection(nameof(CliCollection))]
public class WatchGatingTests
{
    [Fact]
    public void Watch_with_json_errors_BadArgument()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

        var r = root.Run("list", "--watch", "--json");

        Assert.Equal((int)ErrorCode.BadArgument, r.ExitCode);
        Assert.Contains("--watch", r.Stderr);
        Assert.Contains("--json", r.Stderr);
    }

    [Fact]
    public void Watch_with_redirected_stdout_errors_BadArgument()
    {
        // TempKnitRoot redirects Console.Out to a StringWriter for capture,
        // which the test host already does at process level — Console.IsOutputRedirected
        // reports true throughout the test run.
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

        var r = root.Run("list", "--watch");

        Assert.Equal((int)ErrorCode.BadArgument, r.ExitCode);
        Assert.Contains("interactive terminal", r.Stderr);
    }
}
