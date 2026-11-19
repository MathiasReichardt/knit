using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.IO;
using Knit.Core.Paths;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Read-only view of a single file under the repo root, or under a named
/// config mount when <c>?mount=</c> is given. Used by the SPA code
/// viewer when a KB link points outside .knit/kb (e.g. a source file
/// referenced by an ADR). Editing is intentionally out of scope — M1c keeps
/// the SPA read-only for arbitrary repo files, and a mount is read-only by
/// design, so no mutator ever accepts one.
/// </summary>
public static class FileEndpoints
{
    // 2 MiB. Large enough for any source file we care about, small
    // enough to keep the SPA responsive and out of the OOM hazard zone.
    public const long MaxBytes = 2L * 1024 * 1024;

    public static void MapFileView(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/file", (HttpRequest req) =>
        {
            var raw = req.Query["path"].ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return Problem(400, "missing_path", "query parameter 'path' is required");

            var mount = req.Query["mount"].ToString();
            var mounted = !string.IsNullOrWhiteSpace(mount);
            string readRoot, fullPath;
            if (mounted)
            {
                // Resolution and containment for a mount live in MountResolver,
                // so the mount-specific reasons (unknown / unresolved / escape)
                // are worded in one place. An unresolved mount is a config
                // problem, not a missing file.
                var config = ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath));
                if (!MountResolver.TryResolveDir(options.RootPath, config, mount, out readRoot, out var mountErr)
                    || !MountResolver.TryResolveFile(options.RootPath, config, mount, raw, out fullPath, out mountErr))
                    return Problem(400, "invalid_mount", mountErr!);
            }
            else
            {
                readRoot = options.RootPath;
                if (!TryResolveRepoPath(readRoot, raw, out fullPath, out var err))
                    return Problem(400, "invalid_path", err!);
            }

            if (!File.Exists(fullPath))
                return Problem(404, "not_found", $"no file at {raw}");

            FileInfo info;
            try { info = new FileInfo(fullPath); }
            catch (Exception ex) { return Problem(500, "stat_failed", ex.Message); }

            var relReturned = Path.GetRelativePath(readRoot, fullPath).Replace('\\', '/');
            var mountName = mounted ? mount : null;

            if (info.Length > MaxBytes)
            {
                return Results.Json(
                    new FileViewDto(relReturned, info.Length, GuessMime(fullPath), Truncated: true, Content: "", FilePath: fullPath, Mount: mountName),
                    ApiJsonContext.Default.FileViewDto);
            }

            byte[] bytes;
            try { bytes = AtomicFile.ReadAllBytes(fullPath); }
            catch (Exception ex) { return Problem(500, "read_failed", ex.Message); }

            if (LooksBinary(bytes))
            {
                return Results.Json(
                    new FileViewDto(relReturned, info.Length, "application/octet-stream", Truncated: false, Content: "", FilePath: fullPath, Mount: mountName),
                    ApiJsonContext.Default.FileViewDto);
            }

            // Strip a leading UTF-8 BOM so it doesn't show up as a visible
            // character in the editor; the file on disk is untouched.
            var start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                start = 3;
            var text = System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);

            return Results.Json(
                new FileViewDto(relReturned, info.Length, GuessMime(fullPath), Truncated: false, Content: text, FilePath: fullPath, Mount: mountName),
                ApiJsonContext.Default.FileViewDto);
        });

        // A repo file's bytes, for an <img> or a PDF tab. Only the listed types:
        // the file comes from whoever pushed it, and served from this origin an
        // HTML or SVG file would run its scripts next to the mutating API. An
        // SVG goes through /api/file and is sanitized before it is inlined.
        app.MapGet("/api/raw", (HttpRequest req, HttpResponse res) =>
        {
            res.Headers.XContentTypeOptions = "nosniff";
            var raw = req.Query["path"].ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return Problem(400, "missing_path", "query parameter 'path' is required");
            if (!RawTypes.TryGetValue(Path.GetExtension(raw).ToLowerInvariant(), out var contentType))
                return Problem(400, "unsupported_type",
                    $"only {string.Join(", ", RawTypes.Keys)} files are served raw");
            if (!TryResolveRepoPath(options.RootPath, raw, out var fullPath, out var err))
                return Problem(400, "invalid_path", err!);
            if (!File.Exists(fullPath))
                return Problem(404, "not_found", $"no file at {raw}");
            return Results.Stream(File.OpenRead(fullPath), contentType);
        });
    }

    public static readonly IReadOnlyDictionary<string, string> RawTypes = new Dictionary<string, string>
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".pdf"] = "application/pdf",
    };

    private static bool TryResolveRepoPath(string root, string requested, out string fullPath, out string? error)
    {
        fullPath = "";
        error = null;
        // A leading `/` is absolute on Unix; trimming it would quietly turn
        // `/etc/x` into a repo-relative path.
        var normalized = requested.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            error = "absolute paths are not allowed";
            return false;
        }
        var rootFull = Path.GetFullPath(root);
        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(rootFull, normalized)); }
        catch (Exception ex) { error = ex.Message; return false; }

        // Lexical containment stops `..` escapes; the symlink check stops a
        // link committed inside the repo from redirecting the read outside it.
        if (!PathContainment.IsContained(rootFull, candidate)
            || PathContainment.HasSymlinkBelow(rootFull, candidate))
        {
            error = "path escapes the resolution root";
            return false;
        }
        fullPath = candidate;
        return true;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < n; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    private static string GuessMime(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".js" or ".mjs" or ".cjs" or ".jsx" => "text/javascript",
            ".ts" or ".tsx" => "text/typescript",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".scss" => "text/x-scss",
            ".sass" => "text/x-sass",
            ".less" => "text/x-less",
            ".cs" => "text/x-csharp",
            ".csproj" or ".props" or ".targets" or ".xml" or ".xaml" or ".svg" or ".axaml" => "application/xml",
            ".yml" or ".yaml" => "text/yaml",
            ".toml" => "text/x-toml",
            ".sh" or ".bash" or ".zsh" => "text/x-shellscript",
            ".ps1" or ".psm1" or ".psd1" => "text/x-powershell",
            ".py" => "text/x-python",
            ".rs" => "text/rust",
            ".go" => "text/x-go",
            ".c" or ".h" => "text/x-c",
            ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" or ".hxx" => "text/x-c++",
            ".java" or ".kt" => "text/x-java-source",
            ".ini" or ".properties" or ".env" => "text/x-ini",
            ".sql" => "application/sql",
            ".diff" or ".patch" => "text/x-diff",
            ".dockerfile" => "text/x-dockerfile",
            ".txt" or ".log" or ".gitignore" or ".editorconfig" => "text/plain",
            _ => GuessByFilename(path),
        };
    }

    // Extensionless files we still want to highlight (Dockerfile, Makefile, …).
    private static string GuessByFilename(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
            return "text/x-dockerfile";
        return "text/plain";
    }
}
