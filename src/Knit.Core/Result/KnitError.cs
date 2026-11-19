using FunicularSwitch.Generators;

namespace Knit.Core.Result;

/// <summary>
/// Domain-level error category. Each value names a *kind* of failure so
/// callers can react (retry, prompt, surface as a specific exit code)
/// without parsing the message. Presentation concerns (numeric exit
/// code, wire string) live in the CLI layer and map from this enum.
/// </summary>
public enum ErrorKind
{
    /// <summary>Catch-all for failures that don't fit a more specific
    /// kind — typically bugs, unhandled exceptions, IO faults.</summary>
    Internal,

    /// <summary>Wrong / missing / invalid input or a semantically-rejected
    /// combination of otherwise-valid args.</summary>
    BadArgument,

    /// <summary>Target doesn't exist or couldn't be uniquely resolved.</summary>
    NotFound,

    /// <summary>Operation can't proceed because of current state
    /// (already exists, already linked, etc.).</summary>
    Conflict,
}

/// <summary>
/// Error type carried by <see cref="OperationResult{T}"/>. Every error
/// names its <see cref="ErrorKind"/> so the CLI (or any other caller)
/// can map to the right exit code / response shape without inspecting
/// the message.
/// </summary>
public sealed class KnitError
{
    public string Message { get; }
    public ErrorKind Kind { get; }

    public KnitError(string message, ErrorKind kind)
    {
        Message = message;
        Kind = kind;
    }

    public static KnitError Internal(string message)    => new(message, ErrorKind.Internal);
    public static KnitError BadArgument(string message) => new(message, ErrorKind.BadArgument);
    public static KnitError NotFound(string message)    => new(message, ErrorKind.NotFound);
    public static KnitError Conflict(string message)    => new(message, ErrorKind.Conflict);

    public override string ToString() => Message;
}

[ResultType(ErrorType = typeof(KnitError))]
public abstract partial class OperationResult<T> { }
