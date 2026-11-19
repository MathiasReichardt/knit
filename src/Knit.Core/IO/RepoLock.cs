using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Knit.Core.Discovery;

namespace Knit.Core.IO;

/// <summary>
/// <c>.knit/.lock</c> — mutual exclusion for the mutations that rewrite many
/// files (<c>move</c>, <c>retype</c>, <c>relink</c>, <c>kb split</c>).
/// Exclusion, not atomicity: a crash mid-operation still leaves a half-applied
/// change, which <c>knit verify</c> finds and <c>knit relink</c> repairs.
/// Single-item writes never take it; their guard is in <c>ItemStore</c>.
/// <para>
/// The file records its owner (host, pid, operation, since), so a lock left
/// behind names itself. A stale one is broken: on the owner's host when its pid
/// is gone, anywhere after <see cref="StaleAfter"/> — a pid means nothing on
/// another machine sharing the root.
/// </para>
/// </summary>
public sealed class RepoLock : IDisposable
{
    public const string FileName = ".lock";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private readonly string _path;
    private readonly string _token;

    private RepoLock(string path, string token)
    {
        _path = path;
        _token = token;
    }

    public static string PathFor(string root) => Path.Combine(RepoDiscovery.KnitDir(root), FileName);

    /// <param name="root">The effective knit root.</param>
    /// <param name="operation">Named to whoever finds the lock held.</param>
    /// <param name="report">Told when a stale lock was broken, naming whose it was.</param>
    /// <param name="wait">How long a live owner is waited for before refusing.</param>
    /// <exception cref="WriteRefusedException">A live owner still held it after a short wait.</exception>
    public static RepoLock Acquire(string root, string operation, Action<string>? report = null, TimeSpan? wait = null)
    {
        var path = PathFor(root);
        var token = Guid.NewGuid().ToString("N");
        var content = Owner.Current(operation, token).ToJson();
        var deadline = DateTimeOffset.UtcNow + (wait ?? DefaultWait);
        while (true)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(content);
                return new RepoLock(path, token);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new WriteRefusedException($"could not take the repo lock {path}: {ex.Message} — nothing written", ex);
            }
            catch (IOException ex) when (ex is not DirectoryNotFoundException)
            {
                if (!File.Exists(path))
                {
                    if (DateTimeOffset.UtcNow < deadline) continue;
                    throw new WriteRefusedException($"could not take the repo lock {path}: {ex.Message} — nothing written", ex);
                }
                var held = Owner.Read(path);
                if (held.IsStale())
                {
                    // Re-read first: another knit may have broken it and taken a fresh one.
                    if (Owner.Read(path) == held && TryDelete(path))
                        report?.Invoke($"broke a stale repo lock left by {held.Describe()}");
                    continue;
                }
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new WriteRefusedException(
                        $"the repo is locked by {held.Describe()} — nothing written; re-run once it finishes. " +
                        $"If that knit is gone, the lock is broken after {StaleAfter.TotalMinutes:0} minutes, or delete {path}");
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (Owner.Read(_path).Token == _token) TryDelete(_path);
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed record Owner(string? Host, int? Pid, string? Operation, DateTimeOffset? Since, string? Token, DateTime Written)
    {
        public static Owner Current(string operation, string token) =>
            new(Environment.MachineName, Environment.ProcessId, operation, DateTimeOffset.UtcNow, token, DateTime.UtcNow);

        public byte[] ToJson()
        {
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteString("host", Host);
                if (Pid is { } pid) w.WriteNumber("pid", pid);
                w.WriteString("operation", Operation);
                if (Since is { } since) w.WriteString("since", since.ToString("O", CultureInfo.InvariantCulture));
                w.WriteString("token", Token);
                w.WriteEndObject();
            }
            return buffer.ToArray();
        }

        /// <summary>Lenient: a half-written or hand-edited lock still has an age.</summary>
        public static Owner Read(string path)
        {
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { written = DateTime.UtcNow; }
            try
            {
                using var doc = JsonDocument.Parse(AtomicFile.ReadAllBytes(path));
                var o = doc.RootElement;
                return new Owner(
                    Str(o, "host"),
                    o.TryGetProperty("pid", out var p) && p.TryGetInt32(out var pid) ? pid : null,
                    Str(o, "operation"),
                    DateTimeOffset.TryParse(Str(o, "since"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : null,
                    Str(o, "token"),
                    written);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new Owner(null, null, null, null, null, written);
            }
        }

        private static string? Str(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        public bool IsStale()
        {
            var since = Since ?? new DateTimeOffset(Written, TimeSpan.Zero);
            if (DateTimeOffset.UtcNow - since > StaleAfter) return true;
            if (Pid is not { } pid || !string.Equals(Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                using var process = Process.GetProcessById(pid);
                // A reused pid started after the lock was taken.
                return process.StartTime.ToUniversalTime() > since.UtcDateTime.AddSeconds(1);
            }
            catch (ArgumentException) { return true; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                           or NotSupportedException) { return false; }
        }

        public string Describe()
        {
            var who = Host is null ? "an unknown knit" : Pid is { } pid ? $"{Host} (pid {pid})" : Host;
            var what = Operation is null ? "" : $" running '{Operation}'";
            var since = Since ?? new DateTimeOffset(Written, TimeSpan.Zero);
            return $"{who}{what} since {since.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }
}
