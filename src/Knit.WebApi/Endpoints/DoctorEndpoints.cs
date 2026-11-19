using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Doctor endpoints backing the web "Doctor" screen — the read-side
/// audit companion to the CLI's <c>knit verify</c> / <c>relink</c>.
/// Named "doctor" (not "health") so the conventional probe paths
/// (<c>/health</c>, <c>/healthz</c>) stay free for a future liveness
/// endpoint; the general <c>/api/health</c> info endpoint lives with the
/// item endpoints. These add the verify scan and per-row relink repair.
/// </summary>
public static class DoctorEndpoints
{
    public static void MapDoctor(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/doctor/verify", (ILoggerFactory logs) =>
        {
            // The check users trust when something looks wrong: it reads the
            // files, never the cache, and repairs a cache it finds stale.
            var repo = new RepoServices(options.RootPath);
            var root = repo.Root;
            var stale = options.ItemCache?.Heal() ?? [];
            if (stale.Count > 0)
                logs.CreateLogger("Knit.WebApi.ItemCache").LogWarning(
                    "Item cache was stale for {Files}; replaced from the files", string.Join(", ", stale));

            var invalidItems = RepoVerifier.ScanItemFiles(root, repo.Yaml);
            var strayFiles = RepoVerifier.ScanStrayFiles(root).Concat(RepoVerifier.ScanLooseFiles(repo.Store, root));
            var broken = RepoVerifier.Scan(repo.Store, root);
            var malformed = RepoVerifier.ScanMalformedItemLinks(repo.Store);
            var titles = RepoVerifier.ScanTitles(repo.Store, root, repo.Yaml);
            var hierarchy = RepoVerifier.ScanHierarchy(repo.Store);
            var origins = RepoVerifier.ScanOrigins(repo.Store, root, repo.Yaml);
            var duplicateIds = RepoVerifier.ScanDuplicateKbIds(root, repo.Yaml);
            var identityDrift = RepoVerifier.ScanIdentityDrift(root, repo.Yaml);
            var health = RepoVerifier.ScanHealth(repo.Store);
            var projectPointers = RepoVerifier.ScanProjectPointers(repo.Store, root);
            var orphanFolders = RepoVerifier.ScanOrphanProjectFolders(repo.Store, root);
            var orphanItems = RepoVerifier.ScanOrphanProjectItems(repo.Store);
            var malformedMilestones = RepoVerifier.ScanMilestoneDocs(root, repo.Yaml);
            var multipleMilestones = RepoVerifier.ScanMilestoneRows(repo.Store);
            var doneWithOpenChildren = RepoVerifier.ScanDoneWithOpenChildren(repo.Store);
            var staleBlockers = RepoVerifier.ScanStaleBlockers(repo.Store);
            var crossProjectMilestones = RepoVerifier.ScanCrossProjectMilestones(repo.Store);
            var unregisteredSlates = RepoVerifier.ScanUnregisteredSlates(repo.Store, root);
            var config = ConfigLoader.Load(repo.KnitDir);
            var (proseRefs, proseLinks) = RepoVerifier.ScanProse(repo.Store, root, config);
            var sourcePaths = RepoVerifier.ScanSourcePaths(root, config, repo.Yaml);
            var svgFiles = RepoVerifier.ScanSvgFiles(root);

            var dto = new VerifyResultDto(
                invalidItems.Select(v => new InvalidItemRowDto(Rel(root, v.SourcePath), v.Reason)).ToList(),
                strayFiles.Select(s => new StrayFileRowDto(Rel(root, s.SourcePath), s.Reason)).ToList(),
                broken.Select(b => new LinkTargetRowDto(
                    Rel(root, b.SourcePath),
                    LinkRels.ToWire(b.Row.Rel),
                    b.Row.TargetText,
                    b.Row.TargetPath,
                    Rel(root, b.ExpectedPath))).ToList(),
                malformed.Select(m => new MalformedLinkRowDto(
                    Rel(root, m.SourcePath),
                    LinkRels.ToWire(m.Row.Rel),
                    m.Row.TargetText,
                    m.Row.TargetPath,
                    m.ExtractedId)).ToList(),
                titles.Select(t => new TitleDriftRowDto(
                    Rel(root, t.SourcePath), t.FrontmatterTitle, t.BodyH1)).ToList(),
                hierarchy.Select(h => new HierarchyRowDto(
                    Rel(root, h.SourcePath),
                    h.ChildId,
                    h.ChildType.ToWire(),
                    h.ParentId,
                    h.ParentType?.ToWire())).ToList(),
                origins.Select(o => new OriginRowDto(Rel(root, o.SourcePath), o.Ref)).ToList(),
                duplicateIds.Select(d => new DuplicateIdRowDto(
                    d.Bucket, d.Id, d.Paths.Select(p => Rel(root, p)).ToList())).ToList(),
                identityDrift.Select(d => new IdentityDriftRowDto(
                    Rel(root, d.SourcePath), d.Field, d.Declared, d.Expected)).ToList(),
                health.Select(h => new HealthOnNonProjectRowDto(
                    Rel(root, h.SourcePath), h.ItemId, h.Type.ToWire(), h.Health.ToWire())).ToList(),
                projectPointers.Select(p => new ProjectPointerRowDto(
                    Rel(root, p.SourcePath), p.ItemId, p.Type.ToWire(), p.Slug, p.Reason)).ToList(),
                orphanFolders.Select(o => new OrphanProjectFolderRowDto(
                    Rel(root, o.FolderPath), o.Slug)).ToList(),
                orphanItems.Select(o => new OrphanProjectItemRowDto(
                    Rel(root, o.SourcePath), o.ItemId)).ToList(),
                malformedMilestones.Select(m => new MalformedMilestoneRowDto(
                    Rel(root, m.SourcePath), m.Reason)).ToList(),
                multipleMilestones.Select(m => new MultipleMilestonesRowDto(
                    Rel(root, m.SourcePath), m.ItemId, m.Count)).ToList(),
                doneWithOpenChildren.Select(d => new DoneWithOpenChildRowDto(
                    Rel(root, d.SourcePath), d.ParentId, d.ChildId,
                    d.ChildType.ToWire(), d.ChildState.ToWire())).ToList(),
                staleBlockers.Select(b => new StaleBlockerRowDto(
                    Rel(root, b.SourcePath), b.ItemId, b.BlockerId, b.BlockerState.ToWire(), b.Sole)).ToList(),
                crossProjectMilestones.Select(c => new CrossProjectMilestoneRowDto(
                    Rel(root, c.SourcePath), c.ItemId, c.ItemProject, c.MilestoneProject, c.MilestoneSlug)).ToList(),
                unregisteredSlates.Select(u => new UnregisteredSlateRowDto(
                    u.Slate, u.Paths.Select(p => Rel(root, p)).ToList())).ToList(),
                proseRefs.Select(p => new ProseRefRowDto(Rel(root, p.SourcePath), p.Ref, p.Reason, p.Candidates)).ToList(),
                proseLinks.Select(p => new ProseLinkRowDto(
                    Rel(root, p.SourcePath),
                    p.IsImage ? "image" : "link",
                    p.Target,
                    Rel(root, p.ExpectedPath),
                    p.Reason)).ToList(),
                sourcePaths.Select(s => new SourcePathRowDto(
                    Rel(root, s.SourcePath), s.Mount, s.StoredPath, s.Reason)).ToList(),
                svgFiles.Select(s => new SvgFileRowDto(Rel(root, s.SourcePath), s.Reason)).ToList());

            return Results.Json(dto, ApiJsonContext.Default.VerifyResultDto);
        });

        // The settings' "Clear cache": the next read parses every item file.
        app.MapDelete("/api/cache", () =>
        {
            options.ItemCache?.Drop();
            return Results.NoContent();
        });

        app.MapPost("/api/doctor/relink", (RelinkRequestDto req) =>
        {
            var repo = new RepoServices(options);
            using var exclusive = repo.Exclusive("relink");
            var source = Path.GetFullPath(Path.Combine(repo.Root, req.Source));
            if (!File.Exists(source))
                return ApiProblem.Problem(StatusCodes.Status404NotFound, "not_found", $"no file at {req.Source}");

            var candidate = RepoVerifier.ResolveRelinkCandidate(repo.Store, repo.Root, source, req.TargetPath);
            if (candidate is null)
                return Results.Json(new RelinkResultDto(false, null), ApiJsonContext.Default.RelinkResultDto);

            var changed = RepoVerifier.ApplyRelink(source, req.TargetPath, candidate);
            return Results.Json(
                new RelinkResultDto(changed, changed ? candidate : null),
                ApiJsonContext.Default.RelinkResultDto);
        });
    }

    private static string Rel(string root, string abs)
    {
        if (string.IsNullOrEmpty(abs)) return abs;
        return Path.GetRelativePath(root, abs).Replace('\\', '/');
    }
}
