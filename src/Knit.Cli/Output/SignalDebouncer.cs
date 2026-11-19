using System.Threading.Channels;

namespace Knit.Cli.Output;

/// <summary>
/// Coalesces a burst of <see cref="Signal"/> calls into a single
/// <see cref="WaitAsync"/> completion. A bounded channel (capacity 1,
/// drop-on-full) absorbs any number of incoming signals; the reader
/// returns once per debounce window, draining any further signals that
/// arrived during the window so the next wait blocks until the next
/// fresh burst.
/// </summary>
public sealed class SignalDebouncer : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly Channel<byte> _ch;

    public SignalDebouncer(TimeSpan debounce)
    {
        _debounce = debounce;
        _ch = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Non-blocking. Drops if a signal is already pending.</summary>
    public void Signal() => _ch.Writer.TryWrite(0);

    /// <summary>
    /// Returns <c>true</c> after the next signal + debounce window;
    /// <c>false</c> when <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task<bool> WaitAsync(CancellationToken ct)
    {
        try
        {
            await _ch.Reader.ReadAsync(ct).ConfigureAwait(false);
            await Task.Delay(_debounce, ct).ConfigureAwait(false);
            while (_ch.Reader.TryRead(out _)) { }
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }

    public void Dispose() => _ch.Writer.TryComplete();
}
