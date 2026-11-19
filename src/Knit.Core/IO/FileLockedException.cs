namespace Knit.Core.IO;

/// <summary>
/// A read that did not happen because another process holds the file
/// exclusively (an editor saving, a scanner, a sync client) past the retries.
/// The message is user-facing: it names the path and says what to do.
/// </summary>
public sealed class FileLockedException(string path, Exception inner)
    : IOException($"could not read {path}: {Reason} — retry once it is released", inner)
{
    public const string Reason = "the file is locked by another process";
}
