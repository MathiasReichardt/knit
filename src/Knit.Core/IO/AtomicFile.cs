using System.Text;

namespace Knit.Core.IO;

/// <summary>
/// Reads and writes for files other processes may touch at the same time.
/// A write goes to a temp file beside the target and is renamed over
/// it, so a reader sees the old bytes or the new ones, never a torn file.
/// Reads open with <see cref="FileShare.Delete"/>: on Windows, replacing a
/// file another process holds open without it fails with a sharing violation.
/// </summary>
public static class AtomicFile
{
    public const string TempPrefix = ".knit-";
    public const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Covers what a reader without FileShare.Delete (an editor, a scanner) holds.
    private static readonly int[] RetryDelaysMs = [10, 25, 50, 100, 200, 400];

    public static bool IsTemp(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(TempPrefix, StringComparison.Ordinal)
               && name.EndsWith(TempSuffix, StringComparison.Ordinal);
    }

    public static string ReadAllText(string path) => Decode(ReadAllBytes(path));

    /// <summary>UTF-8, a leading BOM dropped — what <see cref="File.ReadAllText(string)"/> returns.</summary>
    public static string Decode(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble)) span = span[Encoding.UTF8.Preamble.Length..];
        return Encoding.UTF8.GetString(span);
    }

    /// <exception cref="FileLockedException">
    /// Another process still held the file exclusively after the retries.
    /// </exception>
    public static byte[] ReadAllBytes(string path)
    {
        using var stream = OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, Utf8NoBom.GetBytes(content));

    /// <exception cref="WriteRefusedException">
    /// The target could not be replaced after the retries; the message names
    /// the path and says nothing was written. The temp file is removed.
    /// </exception>
    public static void WriteAllBytes(string path, byte[] content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temp = Path.Combine(dir, $"{TempPrefix}{Guid.NewGuid():N}{TempSuffix}");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(content);
            Replace(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            var reason = IsLocked(ex) ? FileLockedException.Reason : ex.Message;
            throw new WriteRefusedException($"could not write {path}: {reason} — nothing written", ex);
        }
    }

    private static void Replace(string temp, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // ReplaceFile supersedes a target open with FileShare.Delete; a plain
                // MoveFileEx over it is denied on Windows until the reader closes.
                if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
                else File.Move(temp, path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       && attempt < RetryDelaysMs.Length && File.Exists(temp))
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
        }
    }

    private static FileStream OpenRead(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException ex) when (IsLocked(ex))
            {
                if (attempt >= RetryDelaysMs.Length) throw new FileLockedException(path, ex);
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
        }
    }

    // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION. Unix share modes are
    // advisory and surface as a plain errno, so only Windows is detected.
    private static bool IsLocked(Exception ex) =>
        OperatingSystem.IsWindows() && ex is IOException && (ex.HResult & 0xFFFF) is 32 or 33;

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
