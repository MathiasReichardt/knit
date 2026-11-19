using Knit.Core.Result;

namespace Knit.Cli.Output;

/// <summary>
/// Categorical error codes — small, stable, machine-readable. The
/// *specific situation* lives in the message; the code is the
/// **category**. Display contract:
/// <list type="bullet">
///   <item><c>stderr</c>: <c>error: [&lt;code&gt;] &lt;message&gt;</c></item>
///   <item><c>--json</c>: <c>{"error_code":"&lt;code&gt;", "message":..., "exit_code":...}</c></item>
/// </list>
/// The numeric value of each member IS the process exit code — cast to
/// <see cref="int"/> to get it. One source of truth, no separate map.
/// Resist the urge to add a code per situation; that's what the message
/// is for. New codes are appropriate only when consumers need to react
/// differently (e.g. retry, prompt for missing input).
/// </summary>
public enum ErrorCode
{
    /// <summary>Success.</summary>
    None = 0,

    /// <summary>Wrong / missing / invalid argument; unknown subcommand;
    /// validation failure; or a semantically-rejected combination of
    /// otherwise-valid args (e.g. parent-child type mismatch).</summary>
    BadArgument = 2,

    /// <summary>Target doesn't exist or could not be uniquely resolved
    /// (item, KB ref, file, prefix).</summary>
    NotFound = 3,

    /// <summary>Operation can't proceed because of current state
    /// (folder already exists, target already linked, etc.).</summary>
    Conflict = 4,

    /// <summary>Unexpected failure — bug, environment issue, unhandled
    /// exception. The user-facing message stays trace-free; set
    /// <c>KNIT_DEBUG=1</c> to print the stack trace to stderr.</summary>
    Internal = 70,
}

public static class ErrorCodes
{
    public static string ToWire(this ErrorCode c) => c switch
    {
        ErrorCode.None => "",
        ErrorCode.BadArgument => "bad_argument",
        ErrorCode.NotFound => "not_found",
        ErrorCode.Conflict => "conflict",
        ErrorCode.Internal => "internal",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "unknown error code"),
    };

    public static ErrorCode FromKind(ErrorKind k) => k switch
    {
        ErrorKind.BadArgument => ErrorCode.BadArgument,
        ErrorKind.NotFound    => ErrorCode.NotFound,
        ErrorKind.Conflict    => ErrorCode.Conflict,
        ErrorKind.Internal    => ErrorCode.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "unknown error kind"),
    };
}
