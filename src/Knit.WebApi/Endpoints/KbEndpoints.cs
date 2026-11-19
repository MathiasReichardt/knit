using System.Globalization;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Ids;
using Knit.Core.Kb;
using Knit.Core.Paths;
using Knit.Core.Refs;
using Knit.Core.Templates;
using Knit.WebApi.Contracts;
using Knit.WebApi.Mapping;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Read + write endpoints for KB artifacts. URLs are split by purpose so a
/// bucket name cannot collide with a literal route segment:
/// <list type="bullet">
///   <item><c>/api/kb/tree</c> — live FS tree</item>
///   <item><c>/api/kb/list</c> — flat list</item>
///   <item><c>/api/kb/folder/{*subpath}</c> — folder landing card</item>
///   <item><c>/api/kb/entry/{bucket}/{*slug}</c> — single entry</item>
///   <item><c>/api/kb/index/rebuild</c> — regenerate index.md files</item>
/// </list>
/// </summary>
public static class KbEndpoints
{
    public static void MapKb(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/kb/tree", () =>
        {
            var kbDir = KbLayout.KbDir(options.RootPath);
            var children = BuildTree(kbDir, kbDir, new KbStore(options.RootPath));
            return Results.Json(children, ApiJsonContext.Default.IReadOnlyListKbTreeNodeDto);
        });

        app.MapGet("/api/kb/list", (HttpRequest req) =>
        {
            var bucketStr = req.Query["bucket"].ToString();
            IEnumerable<Bucket> buckets = BucketStrings.All.Where(b => b != Bucket.Item);
            if (!string.IsNullOrEmpty(bucketStr))
            {
                if (!BucketStrings.TryParse(bucketStr, out var b) || b == Bucket.Item)
                    return Problem(400, "invalid_bucket", $"unknown KB bucket '{bucketStr}'");
                buckets = [b];
            }
            var store = new KbStore(options.RootPath);
            var list = new List<KbSummaryDto>();
            foreach (var b in buckets)
            {
                foreach (var (r, path) in store.ListBucket(b))
                {
                    var read = store.ReadPath(path);
                    if (read is null) continue;
                    list.Add(KbMapping.ToSummary(r, path, read.Value.Frontmatter));
                }
            }

            // Landing KB row: recent=1 (or sort=mtime[:asc|:desc]) orders
            // the most-recently-edited entries across all buckets. The ISO
            // Modified string sorts chronologically as text.
            var sort = req.Query["sort"].ToString();
            var byMtime = Paging.Flag(req.Query, "recent") || sort.StartsWith("mtime", StringComparison.OrdinalIgnoreCase);
            if (byMtime)
            {
                list = sort.EndsWith(":asc", StringComparison.OrdinalIgnoreCase)
                    ? list.OrderBy(k => k.Modified, StringComparer.Ordinal).ToList()
                    : list.OrderByDescending(k => k.Modified, StringComparer.Ordinal).ToList();
            }

            var offset = Paging.Int(req.Query, "offset", 0);
            var limit = Paging.Int(req.Query, "limit", 0);
            var page = Paging.Page(list, offset, limit, req.HttpContext.Response);
            return Results.Json(page, ApiJsonContext.Default.IReadOnlyListKbSummaryDto);
        });

        app.MapGet("/api/kb/folder/{*subpath}", (string? subpath) =>
        {
            var rel = (subpath ?? "").Replace('\\', '/').Trim('/');
            var kbDir = Path.GetFullPath(KbLayout.KbDir(options.RootPath));
            string folderPath;
            try { folderPath = rel.Length == 0 ? kbDir : Path.GetFullPath(Path.Combine(kbDir, rel)); }
            catch (Exception ex) { return Problem(400, "invalid_path", ex.Message); }
            // Reject `..`-escapes and symlinks before touching the FS, like /api/file.
            if (!PathContainment.IsContained(kbDir, folderPath)
                || PathContainment.HasSymlinkBelow(kbDir, folderPath))
                return Problem(400, "invalid_path", "path escapes the kb root");
            if (!Directory.Exists(folderPath))
                return Problem(404, "folder_not_found", $"no folder at {rel}");
            var indexPath = Path.Combine(folderPath, KbLayout.PerFolderIndexFile);
            // Master root: render kb-index.md instead of index.md.
            if (rel.Length == 0)
                indexPath = KbLayout.MasterIndexPath(options.RootPath);
            string? indexBody = null;
            var exists = File.Exists(indexPath);
            if (exists)
            {
                try { indexBody = AtomicFile.ReadAllText(indexPath); }
                catch { exists = false; }
            }
            var dto = new KbFolderDto(rel, folderPath, exists, indexBody);
            return Results.Json(dto, ApiJsonContext.Default.KbFolderDto);
        });

        app.MapGet("/api/kb/entry/{bucket}/{*slug}", (string bucket, string slug, HttpResponse res) =>
        {
            if (!TryGetEntry(options, bucket, slug, out var r, out var path, out var err))
                return err!;
            var store = new KbStore(options.RootPath);
            var read = store.ReadPath(path);
            if (read is null) return Problem(500, "unreadable", "failed to read KB entry");
            res.Headers.ETag = Etag.OfFile(path);
            var dto = KbMapping.ToDto(r, path, read.Value.Frontmatter, read.Value.Body);
            return Results.Json(dto, ApiJsonContext.Default.KbDto);
        });

        app.MapPost("/api/kb/entry", async (HttpRequest req, CancellationToken ct) =>
        {
            KbCreateRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbCreateRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");
            if (!BucketStrings.TryParse(body.Bucket, out var bucket) || bucket == Bucket.Item)
                return Problem(400, "invalid_bucket", $"unknown KB bucket '{body.Bucket}'");

            var store = new KbStore(options.RootPath);
            var (r, fm, templateBody, createErr) = BuildCreate(bucket, body, store);
            if (createErr is not null) return createErr;

            if (File.Exists(store.ResolveWritePath(r!)))
                return Problem(409, "exists", $"{r} already exists");

            var path = store.Write(r!, fm!, templateBody!);
            new KbIndexer(store).RebuildBucketAndMaster(bucket);
            var read = store.ReadPath(path)!.Value;
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(path);
            var dto = KbMapping.ToDto(r!, path, read.Frontmatter, read.Body);
            return Results.Json(dto, ApiJsonContext.Default.KbDto,
                statusCode: StatusCodes.Status201Created);
        });

        app.MapMethods("/api/kb/entry/{bucket}/{*slug}", new[] { "PATCH" },
            async (string bucket, string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!TryGetEntry(options, bucket, slug, out var r, out var path, out var err))
                return err!;
            KbPatchRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbPatchRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");

            var store = new KbStore(options.RootPath);
            var read = store.ReadPath(path);
            if (read is null) return Problem(500, "unreadable", "failed to read KB entry");
            var precondition = HttpConcurrency.RequireIfMatch(
                req, Etag.OfFile(path));
            if (precondition is not null) return precondition;
            var fm = body.Frontmatter is null
                ? new Dictionary<string, object?>(read.Value.Frontmatter, StringComparer.Ordinal)
                : KbMapping.Apply(read.Value.Frontmatter, body.Frontmatter, r.Bucket);
            // Origin refs are validated + canonicalized here (parity with the CLI's
            // `kb add adr --origin`) so the web can't silently write a dangling or
            // non-design/item origin that only `knit verify` would later catch.
            if (body.Frontmatter is not null && r.Bucket == Bucket.Adr)
            {
                var (canonical, originErr) = OriginRefs.Normalize(options.RootPath, body.Frontmatter.Origin);
                if (originErr is not null) return Problem(400, "invalid_origin", originErr);
                if (canonical.Length == 0) fm.Remove("origin");
                else fm["origin"] = canonical.Select(s => (object?)s).ToList();
            }
            var newBody = body.Body ?? read.Value.Body;
            // Two-way sync: the leading body H1 and the frontmatter title are
            // one value. An explicit title (edited via the header) wins and is
            // mirrored into the H1; otherwise a body edit's leading H1 becomes
            // the title.
            var explicitTitle = body.Frontmatter?.Title;
            if (!string.IsNullOrWhiteSpace(explicitTitle))
            {
                newBody = Knit.Core.Frontmatter.LeadingH1.SetOrInsert(newBody, explicitTitle!);
            }
            else if (body.Body is not null)
            {
                var h1 = Knit.Core.Frontmatter.LeadingH1.Extract(newBody);
                if (h1 is not null) fm["title"] = h1;
            }
            var writtenPath = store.Write(r, fm, newBody);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            var refreshed = store.ReadPath(writtenPath)!.Value;
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(writtenPath);
            var dto = KbMapping.ToDto(r, writtenPath, refreshed.Frontmatter, refreshed.Body);
            return Results.Json(dto, ApiJsonContext.Default.KbDto);
        });

        app.MapPost("/api/kb/rename",
            async (HttpRequest req, CancellationToken ct) =>
        {
            KbRenameRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbRenameRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Ref) || string.IsNullOrWhiteSpace(body.NewSlug))
                return Problem(400, "invalid_request", "ref and newSlug are required");
            var refParsed = Ref.Parse(body.Ref);
            if (refParsed.IsError) return Problem(400, "invalid_ref", refParsed.GetErrorOrDefault()!.Message);
            var oldRef = refParsed.GetValueOrThrow();
            if (oldRef.Bucket == Bucket.Item)
                return Problem(400, "invalid_ref", "kb rename does not operate on item refs");

            var repo = new RepoServices(options);
            using var exclusive = repo.Exclusive("kb rename");
            var rewriter = new Knit.Data.KbRefRewriter(repo.Store);
            var result = KbRename.Rename(options.RootPath, oldRef, body.NewSlug, rewriter,
                new Knit.Data.ProseRefRewriter(repo.Store, options.RootPath));
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                var status = e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ? 409 : 400;
                return Problem(status, "rename_failed", e.Message);
            }
            var v = result.GetValueOrThrow();
            var kb = new KbStore(options.RootPath);
            new KbIndexer(kb).RebuildBucketAndMaster(oldRef.Bucket);
            var dto = new KbRenameResultDto(
                OldRef: v.OldRef.ToString(),
                NewRef: v.NewRef.ToString(),
                OldPath: v.OldPath,
                NewPath: v.NewPath,
                ItemsRewritten: v.ItemsRewritten,
                KbEntriesAffected: v.KbEntriesAffected,
                FilesRelinked: v.FilesRelinked);
            return Results.Json(dto, ApiJsonContext.Default.KbRenameResultDto);
        });

        app.MapPost("/api/kb/delete",
            async (HttpRequest req, CancellationToken ct) =>
        {
            KbDeleteRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbDeleteRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Ref))
                return Problem(400, "invalid_request", "ref is required");
            var refParsed = Ref.Parse(body.Ref);
            if (refParsed.IsError) return Problem(400, "invalid_ref", refParsed.GetErrorOrDefault()!.Message);
            var r = refParsed.GetValueOrThrow();
            if (r.Bucket == Bucket.Item)
                return Problem(400, "invalid_ref", "kb delete does not operate on item refs");

            var repo = new RepoServices(options);
            using var exclusive = repo.Exclusive("kb delete");
            var remover = new Knit.Data.KbRefRewriter(repo.Store);
            string Rel(string p) => Path.GetRelativePath(options.RootPath, p).Replace('\\', '/');

            if (body.DryRun)
            {
                var pv = KbDelete.Preview(options.RootPath, r, remover);
                if (pv.IsError) return DeleteError(pv.GetErrorOrDefault()!);
                var p = pv.GetValueOrThrow();
                return Results.Json(new KbDeleteResultDto(
                    Ref: p.Ref.ToString(),
                    DeletedPath: Rel(p.TargetPath),
                    DeletedFolder: false,
                    DryRun: true,
                    IsFolderForm: p.IsFolderForm,
                    ItemsAffected: p.ItemsAffected,
                    BlockingFiles: p.BlockingFiles.Select(Rel).ToList(),
                    KbEntriesAffected: p.KbEntriesAffected),
                    ApiJsonContext.Default.KbDeleteResultDto);
            }

            var result = KbDelete.Delete(options.RootPath, r, body.DeleteFolder, remover);
            if (result.IsError) return DeleteError(result.GetErrorOrDefault()!);
            var v = result.GetValueOrThrow();
            if (v.BlockingFiles.Count > 0)
            {
                // Folder-form entry with siblings, deleteFolder not set. 409
                // carries the blocker list so the SPA can offer "delete anyway".
                return Results.Json(new KbDeleteResultDto(
                    Ref: v.Ref.ToString(),
                    DeletedPath: null,
                    DeletedFolder: false,
                    DryRun: false,
                    IsFolderForm: true,
                    ItemsAffected: [],
                    BlockingFiles: v.BlockingFiles.Select(Rel).ToList(),
                    KbEntriesAffected: []),
                    ApiJsonContext.Default.KbDeleteResultDto,
                    statusCode: StatusCodes.Status409Conflict);
            }

            var kb = new KbStore(options.RootPath);
            new KbIndexer(kb).RebuildBucketAndMaster(v.Ref.Bucket);
            return Results.Json(new KbDeleteResultDto(
                Ref: v.Ref.ToString(),
                DeletedPath: v.DeletedPath is null ? null : Rel(v.DeletedPath),
                DeletedFolder: v.DeletedFolder,
                DryRun: false,
                IsFolderForm: v.DeletedFolder,
                ItemsAffected: v.ItemsAffected,
                BlockingFiles: [],
                KbEntriesAffected: v.KbEntriesAffected),
                ApiJsonContext.Default.KbDeleteResultDto);
        });

        app.MapPost("/api/kb/supersede",
            async (HttpRequest req, CancellationToken ct) =>
        {
            KbSupersedeRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbSupersedeRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.NewRef) || string.IsNullOrWhiteSpace(body.OldRef))
                return Problem(400, "invalid_request", "newRef and oldRef are required");

            Ref ParseAdr(string s)
            {
                if (s.Contains(':'))
                {
                    var p = Ref.Parse(s);
                    return p.IsError ? null! : p.GetValueOrThrow();
                }
                return new Ref(Bucket.Adr, s);
            }
            var newRefIn = ParseAdr(body.NewRef);
            var oldRefIn = ParseAdr(body.OldRef);
            if (newRefIn is null || oldRefIn is null
                || newRefIn.Bucket != Bucket.Adr || oldRefIn.Bucket != Bucket.Adr)
                return Problem(400, "invalid_ref", "supersede only works on adr refs");

            var store = new KbStore(options.RootPath);
            var newResolved = RefResolver.Resolve(newRefIn, options.RootPath).GetValueOrThrow();
            var oldResolved = RefResolver.Resolve(oldRefIn, options.RootPath).GetValueOrThrow();
            if (!newResolved.Exists) return Problem(404, "not_found", $"unresolved {newRefIn}");
            if (!oldResolved.Exists) return Problem(404, "not_found", $"unresolved {oldRefIn}");
            var newRef = newResolved.Ref;
            var oldRef = oldResolved.Ref;

            var newRead = store.ReadPath(newResolved.Path)!.Value;
            var oldRead = store.ReadPath(oldResolved.Path)!.Value;

            var newFm = new Dictionary<string, object?>(newRead.Frontmatter, StringComparer.Ordinal);
            var supersedes = (newFm.TryGetValue("supersedes", out var sv) && sv is List<object?> l ? l : []).ToList();
            if (!supersedes.Any(x => string.Equals(x?.ToString(), oldRef.ToString(), StringComparison.Ordinal)))
                supersedes.Add(oldRef.ToString());
            newFm["supersedes"] = supersedes;
            store.Write(newRef, newFm, newRead.Body);

            var oldFm = new Dictionary<string, object?>(oldRead.Frontmatter, StringComparer.Ordinal);
            oldFm["status"] = "superseded";
            oldFm["superseded_by"] = newRef.ToString();
            store.Write(oldRef, oldFm, oldRead.Body);

            new KbIndexer(store).RebuildBucketAndMaster(Bucket.Adr);
            return Results.Json(
                new KbSupersedeResultDto(newRef.ToString(), oldRef.ToString()),
                ApiJsonContext.Default.KbSupersedeResultDto);
        });

        app.MapPost("/api/kb/split", async (HttpRequest req, CancellationToken ct) =>
        {
            KbSplitRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.KbSplitRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Ref))
                return Problem(400, "invalid_request", "ref is required");
            var parsed = Ref.Parse(body.Ref);
            if (parsed.IsError) return Problem(400, "invalid_ref", parsed.GetErrorOrDefault()!.Message);
            var r = parsed.GetValueOrThrow();

            var repo = new RepoServices(options);
            using var exclusive = repo.Exclusive("kb split");
            var result = KbSplit.Split(
                options.RootPath, r, new KbStore(options.RootPath),
                new Knit.Data.KbRefRewriter(repo.Store),
                new Knit.Data.ProseRefRewriter(repo.Store, options.RootPath));
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                return e.Message.StartsWith("unresolved", StringComparison.Ordinal)
                    ? Problem(404, "not_found", e.Message)
                    : Problem(400, "invalid_bucket", e.Message);
            }
            var v = result.GetValueOrThrow();
            return Results.Json(
                new KbSplitResultDto(v.OldPath, v.NewPath, v.Ref.ToString(), v.ItemsRewritten, v.FilesRelinked),
                ApiJsonContext.Default.KbSplitResultDto);
        });

        app.MapPost("/api/kb/index/rebuild", () =>
        {
            var store = new KbStore(options.RootPath);
            var written = new KbIndexer(store).RebuildAll();
            return Results.Json(
                new KbIndexRebuildResultDto(written),
                ApiJsonContext.Default.KbIndexRebuildResultDto);
        });
    }

    private static bool TryGetEntry(WebApiOptions options, string bucket, string slug,
        out Ref r, out string path, out IResult? err)
    {
        r = default!;
        path = "";
        if (!BucketStrings.TryParse(bucket, out var b) || b == Bucket.Item)
        {
            err = Problem(400, "invalid_bucket", $"unknown KB bucket '{bucket}'");
            return false;
        }
        r = new Ref(b, slug);
        var resolved = RefResolver.Resolve(r, options.RootPath).GetValueOrThrow();
        if (!resolved.Exists)
        {
            err = Problem(404, "not_found", $"unresolved ref {r}");
            return false;
        }
        path = resolved.Path;
        err = null;
        return true;
    }

    private static IReadOnlyList<KbTreeNodeDto> BuildTree(string root, string kbDir, KbStore store)
    {
        if (!Directory.Exists(root)) return [];
        var nodes = new List<KbTreeNodeDto>();

        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            var rel = Path.GetRelativePath(kbDir, dir).Replace('\\', '/');

            // grow-to-folder form: `<slug>/readme.md` represents an entry, not a folder.
            var readme = Path.Combine(dir, "readme.md");
            if (File.Exists(readme) && TryRefFromPath(kbDir, readme, out var entryRef))
            {
                var entryFm = store.ReadPath(readme)?.Frontmatter ?? new Dictionary<string, object?>();
                var title = TitleFrom(entryFm) ?? entryRef.Slug;
                var entryTags = TagsFrom(entryFm);

                // entries can host nested children — recurse into the folder, skipping the readme.
                var nested = BuildTree(dir, kbDir, store)
                    .Where(c => !(c.IsFolder == false && string.Equals(c.Name, "readme.md", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                nodes.Add(new KbTreeNodeDto(
                    Name: name,
                    RelPath: rel,
                    IsFolder: false,
                    Bucket: entryRef.Bucket.ToWire(),
                    Ref: entryRef.ToString(),
                    Title: title,
                    FilePath: readme,
                    Tags: entryTags,
                    Children: nested));
                continue;
            }

            nodes.Add(new KbTreeNodeDto(
                Name: name,
                RelPath: rel,
                IsFolder: true,
                Bucket: null,
                Ref: null,
                Title: null,
                FilePath: null,
                Tags: [],
                Children: BuildTree(dir, kbDir, store)));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.md").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            // Skip generated index files at any level — they're TOCs, not entries.
            if (string.Equals(name, KbLayout.PerFolderIndexFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, KbLayout.MasterIndexFile, StringComparison.OrdinalIgnoreCase)) continue;

            var rel = Path.GetRelativePath(kbDir, file).Replace('\\', '/');
            if (!TryRefFromPath(kbDir, file, out var fileRef))
            {
                // Markdown file directly under kb/ root (not under a bucket) — surface it as a folder-level file.
                nodes.Add(new KbTreeNodeDto(name, rel, false, null, null, null, file, [], []));
                continue;
            }
            var fm = store.ReadPath(file)?.Frontmatter ?? new Dictionary<string, object?>();
            var title = TitleFrom(fm) ?? fileRef.Slug;
            nodes.Add(new KbTreeNodeDto(
                Name: name,
                RelPath: rel,
                IsFolder: false,
                Bucket: fileRef.Bucket.ToWire(),
                Ref: fileRef.ToString(),
                Title: title,
                FilePath: file,
                Tags: TagsFrom(fm),
                Children: []));
        }
        return nodes;
    }

    private static bool TryRefFromPath(string kbDir, string filePath, out Ref r)
    {
        r = default!;
        var rel = Path.GetRelativePath(kbDir, filePath).Replace('\\', '/');
        var slash = rel.IndexOf('/');
        if (slash <= 0) return false;
        var bucketFolder = rel[..slash];
        if (!BucketFolders.TryFromFolder(bucketFolder, out var bucket)) return false;
        var tail = rel[(slash + 1)..];
        string slug;
        if (tail.EndsWith("/readme.md", StringComparison.OrdinalIgnoreCase))
            slug = tail[..^"/readme.md".Length];
        else if (tail.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            slug = tail[..^3];
        else return false;
        if (string.IsNullOrEmpty(slug)) return false;
        r = new Ref(bucket, slug);
        return true;
    }

    private static string? TitleFrom(IReadOnlyDictionary<string, object?> fm)
    {
        if (fm.TryGetValue("title", out var v) && v is not null)
        {
            var s = v.ToString();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }

    private static IReadOnlyList<string> TagsFrom(IReadOnlyDictionary<string, object?> fm)
    {
        if (!fm.TryGetValue("tags", out var v) || v is null) return [];
        if (v is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        var single = v.ToString();
        return string.IsNullOrEmpty(single) ? [] : [single];
    }

    private static (Ref?, Dictionary<string, object?>?, string?, IResult?) BuildCreate(
        Bucket bucket, KbCreateRequestDto body, KbStore store)
    {
        var title = body.Title?.Trim() ?? "";
        var summary = body.Summary;
        var feature = body.Feature;

        switch (bucket)
        {
            case Bucket.Adr:
            {
                if (string.IsNullOrWhiteSpace(title))
                    return (null, null, null, Problem(400, "invalid_title", "title is required for adr"));
                var nnnn = NextAdrNumber(store);
                var slug = $"{nnnn:D4}-{Slug.From(title)}";
                var prefixedTitle = $"{nnnn:D4}. {title}";
                var fm = KbFrontmatterMap.ToMap(new AdrFrontmatter(
                    Id: $"{nnnn:D4}",
                    Title: prefixedTitle,
                    Date: DateOnly.FromDateTime(DateTime.UtcNow),
                    Status: "proposed",
                    Supersedes: [],
                    SupersededBy: null,
                    Feature: string.IsNullOrEmpty(feature) ? [] : [feature],
                    Origin: [],
                    Tags: [],
                    Summary: summary));
                var bodyText = RenderTemplate(Bucket.Adr,
                    new Dictionary<string, string> { ["title"] = title, ["number"] = $"{nnnn:D4}" });
                return (new Ref(Bucket.Adr, slug), fm, bodyText, null);
            }
            case Bucket.Doc:
            {
                var name = body.Slug?.Trim();
                if (string.IsNullOrWhiteSpace(name)) name = title;
                if (string.IsNullOrWhiteSpace(name))
                    return (null, null, null, Problem(400, "invalid_slug", "slug or title is required for doc"));
                var docNorm = SubdocSlug.NormalizeDocPath(store, name!, title);
                if (docNorm.Error is not null)
                    return (null, null, null, Problem(400, "invalid_slug", docNorm.Error));
                var slug = docNorm.Slug!;
                var docTitle = string.IsNullOrEmpty(title) ? name! : title;
                var fm = KbFrontmatterMap.ToMap(new DocFrontmatter(docTitle, feature, [], summary));
                var bodyText = RenderTemplate(Bucket.Doc,
                    new Dictionary<string, string> { ["title"] = docTitle });
                return (new Ref(Bucket.Doc, slug), fm, bodyText, null);
            }
            case Bucket.Feature:
            {
                var rawSlug = body.Slug?.Trim();
                if (string.IsNullOrWhiteSpace(rawSlug)) rawSlug = title;
                if (string.IsNullOrWhiteSpace(rawSlug))
                    return (null, null, null, Problem(400, "invalid_slug", "slug or title is required for feature"));
                var featNorm = SubdocSlug.NormalizeFeaturePath(store, rawSlug!, title);
                if (featNorm.Error is not null)
                    return (null, null, null, Problem(400, "invalid_slug", featNorm.Error));
                var slug = featNorm.Slug!;
                // Default the title to the raw typed slug (nicer than the
                // lowercased/dashed result) when no title was given.
                var featTitle = string.IsNullOrEmpty(title) ? rawSlug! : title;
                var fm = KbFrontmatterMap.ToMap(new FeatureFrontmatter(featTitle, slug, [], summary));
                var bodyText = RenderTemplate(Bucket.Feature,
                    new Dictionary<string, string> { ["title"] = featTitle });
                return (new Ref(Bucket.Feature, slug), fm, bodyText, null);
            }
            case Bucket.Log:
            {
                if (string.IsNullOrWhiteSpace(title))
                    return (null, null, null, Problem(400, "invalid_title", "title is required for log"));
                DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
                if (!string.IsNullOrEmpty(body.Date)
                    && DateOnly.TryParseExact(body.Date, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    date = d;
                var slug = $"{date:yyyy-MM-dd}-{Slug.From(title)}";
                var fm = KbFrontmatterMap.ToMap(new LogFrontmatter(title, date, [], [], summary));
                var bodyText = RenderTemplate(Bucket.Log,
                    new Dictionary<string, string> { ["title"] = title });
                return (new Ref(Bucket.Log, slug), fm, bodyText, null);
            }
            case Bucket.Idea:
            {
                var text = title;
                if (string.IsNullOrWhiteSpace(text))
                    return (null, null, null, Problem(400, "invalid_title", "title is required for idea"));
                // Slugify whether typed or derived from the text, so an idea
                // slug is always a filesystem-safe, lowercase token.
                var rawSlug = body.Slug?.Trim();
                var slug = Slug.From(string.IsNullOrWhiteSpace(rawSlug) ? text : rawSlug!);
                var fm = KbFrontmatterMap.ToMap(new IdeaFrontmatter(text, feature, [], null));
                var bodyText = RenderTemplate(Bucket.Idea,
                    new Dictionary<string, string> { ["title"] = text, ["body"] = "" });
                return (new Ref(Bucket.Idea, slug!), fm, bodyText, null);
            }
            case Bucket.Source:
            {
                var rawId = body.Slug?.Trim();
                if (string.IsNullOrWhiteSpace(rawId))
                    return (null, null, null, Problem(400, "invalid_slug", "slug (id) is required for source"));
                var srcTitle = string.IsNullOrEmpty(title) ? rawId! : title;
                // The id doubles as a filesystem-safe slug, so it may nest
                // under a folder-form source; the stored `id:` is the leaf.
                var srcNorm = SubdocSlug.Normalize(store, Bucket.Source, rawId, srcTitle);
                if (srcNorm.Error is not null)
                    return (null, null, null, Problem(400, "invalid_slug", srcNorm.Error));
                var srcSlug = srcNorm.Slug!;
                var id = KbIdentity.ExpectedId(Bucket.Source, srcSlug)!;
                var kind = body.SourceKind;
                if (string.IsNullOrEmpty(kind))
                    kind = string.IsNullOrEmpty(body.Path) ? "link" : "file";
                if (kind is not "link" and not "file")
                    return (null, null, null, Problem(400, "invalid_kind", "sourceKind must be 'link' or 'file'"));
                if (kind == "link" && string.IsNullOrEmpty(body.Url))
                    return (null, null, null, Problem(400, "invalid_url", "link sources require url"));
                if (kind == "file" && string.IsNullOrEmpty(body.Path))
                    return (null, null, null, Problem(400, "invalid_path", "file sources require path"));
                var mount = body.Mount;
                if (!string.IsNullOrEmpty(mount))
                {
                    if (kind != "file")
                        return (null, null, null, Problem(400, "invalid_mount", "mount applies to file sources only"));
                    // Only the name is checked here: whether it resolves on
                    // this machine is config state the doctor view reports,
                    // not a reason to refuse the entry.
                    if (!MountResolver.IsValidName(mount))
                        return (null, null, null, Problem(400, "invalid_mount", $"invalid mount name '{mount}' — {MountResolver.NameRule}"));
                    var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(store.RepoRoot));
                    if (cfg?.Mounts is null || !cfg.Mounts.ContainsKey(mount))
                        return (null, null, null, Problem(400, "unknown_mount", $"unknown mount '{mount}' — declare it with `knit mount add {mount} <path>`"));
                }
                var srcPath = body.Path;
                // Same route as `knit kb add source`: a literal path is
                // rebased onto the mount that covers it, so the SPA and an
                // agent can both pass the path they see.
                if (kind == "file" && MountResolver.IsLiteralPath(store.RepoRoot, srcPath!))
                {
                    var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(store.RepoRoot));
                    var rebase = MountResolver.RebaseLiteral(store.RepoRoot, cfg, srcPath!, mount);
                    if (rebase.Error is not null)
                        return (null, null, null, rebase.Ambiguous
                            ? Problem(409, "ambiguous_mount", rebase.Error)
                            : Problem(400, "invalid_path", rebase.Error));
                    srcPath = rebase.Path;
                    mount = rebase.Mount;
                }
                var fm = KbFrontmatterMap.ToMap(new SourceFrontmatter(id, srcTitle, kind, body.Url, srcPath, [], summary, mount));
                var bodyText = RenderTemplate(Bucket.Source,
                    new Dictionary<string, string> { ["title"] = srcTitle });
                return (new Ref(Bucket.Source, srcSlug), fm, bodyText, null);
            }
            default:
                return (null, null, null, Problem(400, "invalid_bucket", $"unsupported bucket {bucket.ToWire()}"));
        }
    }

    private static int NextAdrNumber(KbStore store)
    {
        var max = 0;
        foreach (var (r, _) in store.ListBucket(Bucket.Adr))
        {
            var dash = r.Slug.IndexOf('-');
            var prefix = dash > 0 ? r.Slug[..dash] : r.Slug;
            if (int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
        }
        return max + 1;
    }

    private static string RenderTemplate(Bucket bucket, IReadOnlyDictionary<string, string> values)
    {
        var template = KbTemplates.Load(bucket);
        return TemplateEngine.Render(template, values);
    }

    private static IResult DeleteError(Knit.Core.Result.KnitError e)
    {
        var status = e.Kind == Knit.Core.Result.ErrorKind.NotFound ? 404 : 400;
        return Problem(status, e.Kind == Knit.Core.Result.ErrorKind.NotFound ? "not_found" : "delete_failed", e.Message);
    }
}
