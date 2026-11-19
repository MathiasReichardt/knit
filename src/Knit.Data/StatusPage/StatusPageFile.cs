using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Paths;

namespace Knit.Data.StatusPage;

/// <param name="Generated">The date on the page's <c>Generated</c> line; null when a hand edit removed it.</param>
/// <param name="Stale">Older than <c>status_report.stale_days</c>, or of unknown age.</param>
public sealed record StatusPageAge(string Path, DateOnly? Generated, bool Stale);

public enum StatusPageWrite
{
    Written,
    /// <summary>The target exists without the generated marker — a hand-written file is never replaced unasked.</summary>
    NotGenerated,
}

/// <summary>
/// Where the status page goes and how it is written. The CLI and the web API
/// both go through here, so the button and the command can't disagree.
/// </summary>
public static class StatusPageFile
{
    /// <summary>
    /// The absolute target for <paramref name="path"/> (the <c>status_report.path</c>
    /// key or <c>--out</c>), resolved against the knit root. Refused outside the
    /// repo, because the page links into <c>.knit/</c> relative to its own folder,
    /// and inside <c>.knit/</c>, where it would be a stray file.
    /// </summary>
    public static (string? Path, string? Error) Resolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "the report path is empty");
        var repo = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(repo, path.Trim()));
        if (!PathContainment.IsContained(repo, full) || RepoDiscovery.SamePath(repo, full))
            return (null, $"'{path}' is outside the repo at {repo} — the report links into .knit/ from its own folder; use --stdout to write it elsewhere");
        if (PathContainment.IsContained(Path.GetFullPath(RepoDiscovery.KnitDir(repo)), full))
            return (null, $"'{path}' is inside .knit/ — the report is a page for the repo, not a tracker file");
        if (PathContainment.HasSymlinkBelow(repo, full))
            return (null, $"'{path}' passes through a symlink");
        if (Directory.Exists(full))
            return (null, $"'{path}' is a folder — name the file, e.g. {Path.Combine(path, new KnitConfig().StatusReportPath)}");
        return (full, null);
    }

    /// <summary>True when the file is absent or starts with the generated marker.</summary>
    private static bool IsGenerated(string path)
    {
        if (!File.Exists(path)) return true;
        using var reader = new StreamReader(path);
        return IsMarker(reader.ReadLine());
    }

    private static bool IsMarker(string? line) =>
        line is not null && line.TrimStart('﻿').TrimEnd() == StatusPageRenderer.Marker;

    /// <summary>
    /// How old the page at <c>status_report.path</c> is, or null when there is
    /// no page knit wrote: deleting the page is how a repo opts out of the hint.
    /// The age is read from the page's <c>Generated</c> line, because a clone
    /// resets mtimes.
    /// </summary>
    public static StatusPageAge? Age(string root, KnitConfig config, DateOnly today)
    {
        var (path, _) = Resolve(root, config.StatusReportPath);
        if (path is null || !File.Exists(path)) return null;
        using var reader = new StreamReader(path);
        if (!IsMarker(reader.ReadLine())) return null;
        DateOnly? generated = null;
        for (var i = 0; i < 8 && reader.ReadLine() is { } line; i++)
        {
            if (!line.StartsWith(StatusPageRenderer.GeneratedPrefix, StringComparison.Ordinal)) continue;
            var word = line[StatusPageRenderer.GeneratedPrefix.Length..].Split(' ', 2)[0];
            if (DueDates.TryParse(word, out var d)) generated = d;
            break;
        }
        var stale = generated is not { } g || today.DayNumber - g.DayNumber > config.StatusReportStaleDays;
        return new StatusPageAge(path, generated, stale);
    }

    public static StatusPageWrite Write(string path, string content, bool force)
    {
        if (!force && !IsGenerated(path)) return StatusPageWrite.NotGenerated;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, content);
        return StatusPageWrite.Written;
    }

    /// <summary>The page for the repo at <paramref name="root"/>, linked from <paramref name="target"/>'s folder.</summary>
    public static string Render(
        string root, ItemStore store, IFrontmatterSerializer yaml, KnitConfig config, string? gitRoot,
        DateOnly today, string target, Func<string, string> shortId)
    {
        var name = string.IsNullOrWhiteSpace(config.RepoName) ? GitHead.DefaultRepoName(root, gitRoot) : config.RepoName!;
        return StatusPageRenderer.Render(StatusPageInput.Load(root, store, yaml, name, today, target, shortId));
    }
}
