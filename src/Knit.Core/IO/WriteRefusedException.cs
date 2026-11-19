namespace Knit.Core.IO;

/// <summary>
/// A write that did not happen because of someone else — another writer
/// changed the file, holds the repo lock, or holds the file open. The
/// message is user-facing: it names the path and says what to do.
/// </summary>
public sealed class WriteRefusedException(string message, Exception? inner = null) : IOException(message, inner);
