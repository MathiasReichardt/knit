using Knit.Core.Discovery;
using Knit.Core.IO;
using Knit.Core.Paths;

namespace Knit.Core.Config;

public enum ConventionsScope { User, Repo }

/// <summary>One scope's conventions file; <c>Text</c> is null when the file doesn't exist.</summary>
public sealed record ConventionsFile(ConventionsScope Scope, string Path, string? Text)
{
    public bool Exists => Text is not null;

    public int LineCount => Text is null ? 0 : Text.TrimEnd('\n').Split('\n').Length;
}

/// <summary>
/// A repo's and a user's own rules for agents, as plain markdown:
/// <c>.knit/conventions.md</c>, committed and shared, and <c>conventions.md</c>
/// beside the user-level <c>config.json</c>. Markdown has no keys, so the two
/// are never merged — readers get both, user first, and the skills state that
/// the repo wins a conflict.
/// </summary>
public static class Conventions
{
    public const string FileName = "conventions.md";

    /// <summary>Above this every agent run pays for a long read; <c>knit verify</c> says so.</summary>
    public const int LongLineCount = 200;

    /// <summary>User first, then repo — the order readers print and the precedence builds on.</summary>
    public static readonly ConventionsScope[] Scopes = [ConventionsScope.User, ConventionsScope.Repo];

    public static string ToWire(this ConventionsScope scope) => scope == ConventionsScope.User ? "user" : "repo";

    public static bool TryParse(string? raw, out ConventionsScope scope)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "user": scope = ConventionsScope.User; return true;
            case "repo": scope = ConventionsScope.Repo; return true;
            default: scope = default; return false;
        }
    }

    public static string PathFor(ConventionsScope scope, string root) => scope == ConventionsScope.User
        ? Path.Combine(UserPaths.ConfigDir, FileName)
        : Path.Combine(RepoDiscovery.KnitDir(root), FileName);

    public static ConventionsFile Read(ConventionsScope scope, string root)
    {
        var path = PathFor(scope, root);
        return new ConventionsFile(scope, path, File.Exists(path) ? Normalize(AtomicFile.ReadAllText(path)) : null);
    }

    public static IReadOnlyList<ConventionsFile> ReadAll(string root) => [.. Scopes.Select(s => Read(s, root))];

    /// <summary>
    /// Replace the file: UTF-8 without BOM, LF, one trailing newline. Blank
    /// text removes it — no file and an empty one mean the same, and only the
    /// first is what a repo without conventions looks like.
    /// </summary>
    public static ConventionsFile Write(ConventionsScope scope, string root, string text)
    {
        var path = PathFor(scope, root);
        var normalized = Normalize(text);
        if (normalized.Length == 0) return Remove(scope, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, normalized);
        return new ConventionsFile(scope, path, normalized);
    }

    /// <summary>Delete the file; a missing one is already the result.</summary>
    public static ConventionsFile Remove(ConventionsScope scope, string root)
    {
        var path = PathFor(scope, root);
        if (File.Exists(path)) File.Delete(path);
        return new ConventionsFile(scope, path, null);
    }

    private static string Normalize(string text)
    {
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return lf.Length == 0 ? "" : lf + "\n";
    }
}
