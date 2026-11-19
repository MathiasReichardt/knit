using System.CommandLine;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.Data.StatusPage;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit verify</c> — three-class consistency check across items + KB:
///   1. Broken <c>Target</c> paths in <c>## Links</c> tables.
///   2. Frontmatter <c>title</c> vs body leading <c>#</c> h1 drift
///      (<c>show --json</c> strips the body h1 because it duplicates
///      the frontmatter title; silent drift between the two misleads
///      every downstream view).
///   3. Hierarchy violations: parent/child type combinations that the
///      write paths reject but a hand-edit can sneak past. Surfaces
///      drift only — fix with <c>knit retype</c> / <c>knit unlink</c>.
/// </summary>
public static class VerifyCommand
{
    public static Command Build()
    {
        var cmd = new Command("verify", "Find invalid/invisible item files, broken Target paths, title/h1 drift, hierarchy violations, dangling ADR origins, and dangling body-prose references across items + KB");
        var strictOpt = new Option<bool>("--strict")
        {
            Description = "Treat body-prose warnings (dangling code-span refs / md links / images) as hard failures (exit 4)",
        };
        cmd.Options.Add(strictOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var strict = parse.GetValue(strictOpt);

            var invalidItems = RepoVerifier.ScanItemFiles(ctx.Root, ctx.Yaml);
            var strayFiles = RepoVerifier.ScanStrayFiles(ctx.Root)
                .Concat(RepoVerifier.ScanLooseFiles(ctx.Store, ctx.Root))
                .Concat(RepoVerifier.ScanShadowKnitDirs(ctx.Root))
                .Concat(RepoVerifier.ScanConfigLayers(ctx.Root))
                .ToList();
            var broken = RepoVerifier.Scan(ctx.Store, ctx.Root);
            var malformed = RepoVerifier.ScanMalformedItemLinks(ctx.Store);
            var titles = RepoVerifier.ScanTitles(ctx.Store, ctx.Root, ctx.Yaml);
            var hierarchy = RepoVerifier.ScanHierarchy(ctx.Store);
            var origins = RepoVerifier.ScanOrigins(ctx.Store, ctx.Root, ctx.Yaml);
            var duplicateIds = RepoVerifier.ScanDuplicateKbIds(ctx.Root, ctx.Yaml);
            var identityDrift = RepoVerifier.ScanIdentityDrift(ctx.Root, ctx.Yaml);
            var healthFindings = RepoVerifier.ScanHealth(ctx.Store);
            var projectPointers = RepoVerifier.ScanProjectPointers(ctx.Store, ctx.Root);
            var orphanFolders = RepoVerifier.ScanOrphanProjectFolders(ctx.Store, ctx.Root);
            var orphanItems = RepoVerifier.ScanOrphanProjectItems(ctx.Store);
            var malformedMilestones = RepoVerifier.ScanMilestoneDocs(ctx.Root, ctx.Yaml);
            var multiMilestones = RepoVerifier.ScanMilestoneRows(ctx.Store);
            var doneWithOpenChildren = RepoVerifier.ScanDoneWithOpenChildren(ctx.Store);
            var staleBlockers = RepoVerifier.ScanStaleBlockers(ctx.Store);
            var unregisteredSlates = RepoVerifier.ScanUnregisteredSlates(ctx.Store, ctx.Root);
            var crossProjectMilestones = RepoVerifier.ScanCrossProjectMilestones(ctx.Store);
            var (proseRefs, proseLinks) = RepoVerifier.ScanProse(ctx.Store, ctx.Root, ctx.Config);
            var sourcePaths = RepoVerifier.ScanSourcePaths(ctx.Root, ctx.Config, ctx.Yaml);
            var svgFiles = RepoVerifier.ScanSvgFiles(ctx.Root);
            var knitDir = RepoDiscovery.KnitDir(ctx.Root);
            var globalConfigNotes = RepoVerifier.ScanGlobalConfig();
            var longConventions = RepoVerifier.ScanConventions(ctx.Root);
            var mountIssues = RepoVerifier.ScanMounts(
                    ctx.Root,
                    ConfigLoader.ReadGlobal().Node?.Deserialize(KnitConfigJsonContext.Default.KnitConfig),
                    ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.FileName),
                    ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.LocalFileName),
                    ctx.Yaml)
                .Where(m => m.Reason is not null)
                .ToList();
            // A source naming a mount nobody declares is broken content (a
            // warning --strict promotes); a declared mount that doesn't
            // resolve here is machine state, so it stays informational.
            var undeclaredMounts = mountIssues.Where(m => m.Source is null).ToList();
            var committedIdentity = RepoVerifier.ScanCommittedIdentity(ctx.Root);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var statusPage = StatusPageFile.Age(ctx.Root, ctx.Config, today);

            if (json)
            {
                using var sw = new StringWriter();
                sw.Write("{\"invalid_items\":[");
                for (var i = 0; i < invalidItems.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var v = invalidItems[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(v.SourcePath)}\"," +
                        $"\"reason\":\"{Escape(v.Reason)}\"}}");
                }
                sw.Write("],\"stray_files\":[");
                for (var i = 0; i < strayFiles.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var s = strayFiles[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(s.SourcePath)}\"," +
                        $"\"reason\":\"{Escape(s.Reason)}\"}}");
                }
                sw.Write("],\"link_targets\":[");
                for (var i = 0; i < broken.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var b = broken[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(b.SourcePath)}\"," +
                        $"\"rel\":\"{Escape(LinkRels.ToWire(b.Row.Rel))}\"," +
                        $"\"target_text\":\"{Escape(b.Row.TargetText)}\"," +
                        $"\"target_path\":\"{Escape(b.Row.TargetPath)}\"," +
                        $"\"expected_path\":\"{Escape(b.ExpectedPath)}\"}}");
                }
                sw.Write("],\"malformed_item_links\":[");
                for (var i = 0; i < malformed.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var m = malformed[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(m.SourcePath)}\"," +
                        $"\"rel\":\"{Escape(LinkRels.ToWire(m.Row.Rel))}\"," +
                        $"\"target_text\":\"{Escape(m.Row.TargetText)}\"," +
                        $"\"target_path\":\"{Escape(m.Row.TargetPath)}\"," +
                        $"\"extracted_id\":\"{Escape(m.ExtractedId)}\"}}");
                }
                sw.Write("],\"titles\":[");
                for (var i = 0; i < titles.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var t = titles[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(t.SourcePath)}\"," +
                        $"\"frontmatter_title\":\"{Escape(t.FrontmatterTitle)}\"," +
                        $"\"body_h1\":\"{Escape(t.BodyH1)}\"}}");
                }
                sw.Write("],\"hierarchy\":[");
                for (var i = 0; i < hierarchy.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var h = hierarchy[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(h.SourcePath)}\"," +
                        $"\"child_id\":\"{Escape(h.ChildId)}\"," +
                        $"\"child_type\":\"{Escape(h.ChildType.ToWire())}\"," +
                        $"\"parent_id\":\"{Escape(h.ParentId ?? string.Empty)}\"," +
                        $"\"parent_type\":\"{Escape(h.ParentType?.ToWire() ?? string.Empty)}\"}}");
                }
                sw.Write("],\"origins\":[");
                for (var i = 0; i < origins.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var o = origins[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(o.SourcePath)}\"," +
                        $"\"ref\":\"{Escape(o.Ref)}\"}}");
                }
                sw.Write("],\"duplicate_ids\":[");
                for (var i = 0; i < duplicateIds.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var d = duplicateIds[i];
                    sw.Write(
                        $"{{\"bucket\":\"{Escape(d.Bucket)}\"," +
                        $"\"id\":\"{Escape(d.Id)}\"," +
                        "\"paths\":[");
                    for (var j = 0; j < d.Paths.Count; j++)
                    {
                        if (j > 0) sw.Write(',');
                        sw.Write($"\"{Escape(d.Paths[j])}\"");
                    }
                    sw.Write("]}");
                }
                sw.Write("],\"identity_drift\":[");
                for (var i = 0; i < identityDrift.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var d = identityDrift[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(d.SourcePath)}\"," +
                        $"\"field\":\"{Escape(d.Field)}\"," +
                        $"\"declared\":\"{Escape(d.Declared)}\"," +
                        $"\"expected\":\"{Escape(d.Expected)}\"}}");
                }
                sw.Write("],\"health_on_non_project\":[");
                for (var i = 0; i < healthFindings.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var h = healthFindings[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(h.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(h.ItemId)}\"," +
                        $"\"type\":\"{Escape(h.Type.ToWire())}\"," +
                        $"\"health\":\"{Escape(h.Health.ToWire())}\"}}");
                }
                sw.Write("],\"project_pointers\":[");
                for (var i = 0; i < projectPointers.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var p = projectPointers[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(p.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(p.ItemId)}\"," +
                        $"\"type\":\"{Escape(p.Type.ToWire())}\"," +
                        $"\"slug\":\"{Escape(p.Slug)}\"," +
                        $"\"reason\":\"{Escape(p.Reason)}\"}}");
                }
                sw.Write("],\"orphan_project_folders\":[");
                for (var i = 0; i < orphanFolders.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var o = orphanFolders[i];
                    sw.Write(
                        $"{{\"folder\":\"{Escape(o.FolderPath)}\"," +
                        $"\"slug\":\"{Escape(o.Slug)}\"}}");
                }
                sw.Write("],\"orphan_project_items\":[");
                for (var i = 0; i < orphanItems.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var o = orphanItems[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(o.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(o.ItemId)}\"}}");
                }
                sw.Write("],\"malformed_milestones\":[");
                for (var i = 0; i < malformedMilestones.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var m = malformedMilestones[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(m.SourcePath)}\"," +
                        $"\"reason\":\"{Escape(m.Reason)}\"}}");
                }
                sw.Write("],\"multiple_milestones\":[");
                for (var i = 0; i < multiMilestones.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var m = multiMilestones[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(m.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(m.ItemId)}\"," +
                        $"\"count\":{m.Count}}}");
                }
                sw.Write("],\"done_with_open_children\":[");
                for (var i = 0; i < doneWithOpenChildren.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var d = doneWithOpenChildren[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(d.SourcePath)}\"," +
                        $"\"parent_id\":\"{Escape(d.ParentId)}\"," +
                        $"\"child_id\":\"{Escape(d.ChildId)}\"," +
                        $"\"child_type\":\"{Escape(d.ChildType.ToWire())}\"," +
                        $"\"child_state\":\"{Escape(d.ChildState.ToWire())}\"}}");
                }
                sw.Write("],\"stale_blockers\":[");
                for (var i = 0; i < staleBlockers.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var b = staleBlockers[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(b.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(b.ItemId)}\"," +
                        $"\"blocker_id\":\"{Escape(b.BlockerId)}\"," +
                        $"\"blocker_state\":\"{Escape(b.BlockerState.ToWire())}\"," +
                        $"\"sole\":{(b.Sole ? "true" : "false")}}}");
                }
                sw.Write("],\"unregistered_slates\":[");
                for (var i = 0; i < unregisteredSlates.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var u = unregisteredSlates[i];
                    sw.Write($"{{\"slate\":\"{Escape(u.Slate)}\",\"paths\":[");
                    for (var j = 0; j < u.Paths.Count; j++)
                    {
                        if (j > 0) sw.Write(',');
                        sw.Write($"\"{Escape(u.Paths[j])}\"");
                    }
                    sw.Write("]}");
                }
                sw.Write("],\"cross_project_milestones\":[");
                for (var i = 0; i < crossProjectMilestones.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var c = crossProjectMilestones[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(c.SourcePath)}\"," +
                        $"\"item_id\":\"{Escape(c.ItemId)}\"," +
                        $"\"item_project\":\"{Escape(c.ItemProject)}\"," +
                        $"\"milestone_project\":\"{Escape(c.MilestoneProject)}\"," +
                        $"\"milestone_slug\":\"{Escape(c.MilestoneSlug)}\"}}");
                }
                sw.Write("],\"prose_refs\":[");
                for (var i = 0; i < proseRefs.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var p = proseRefs[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(p.SourcePath)}\"," +
                        $"\"ref\":\"{Escape(p.Ref)}\"," +
                        $"\"reason\":\"{p.Reason}\"," +
                        $"\"candidates\":[{string.Join(',', p.Candidates.Select(c => $"\"{c}\""))}]}}");
                }
                sw.Write("],\"prose_links\":[");
                for (var i = 0; i < proseLinks.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var p = proseLinks[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(p.SourcePath)}\"," +
                        $"\"kind\":\"{(p.IsImage ? "image" : "link")}\"," +
                        $"\"target\":\"{Escape(p.Target)}\"," +
                        $"\"expected_path\":\"{Escape(p.ExpectedPath)}\"," +
                        $"\"reason\":\"{p.Reason}\"}}");
                }
                sw.Write("],\"source_paths\":[");
                for (var i = 0; i < sourcePaths.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var s = sourcePaths[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(s.SourcePath)}\"," +
                        (s.Mount is null ? "" : $"\"mount\":\"{Escape(s.Mount)}\",") +
                        $"\"path\":\"{Escape(s.StoredPath)}\"," +
                        $"\"expected_path\":\"{Escape(s.ExpectedPath)}\"," +
                        $"\"reason\":\"{Escape(s.Reason)}\"}}");
                }
                sw.Write("],\"svg_files\":[");
                for (var i = 0; i < svgFiles.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var s = svgFiles[i];
                    sw.Write(
                        $"{{\"source\":\"{Escape(s.SourcePath)}\"," +
                        $"\"reason\":\"{Escape(s.Reason)}\"}}");
                }
                sw.Write("],\"mounts\":[");
                for (var i = 0; i < mountIssues.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var m = mountIssues[i];
                    sw.Write(
                        $"{{\"name\":\"{Escape(m.Name)}\"," +
                        (m.DeclaredPath is null ? "" : $"\"declared_path\":\"{Escape(m.DeclaredPath)}\",") +
                        $"\"effective_path\":\"{Escape(m.EffectivePath)}\"," +
                        (m.Source is null ? "" : $"\"source\":\"{Escape(m.Source)}\",") +
                        $"\"reason\":\"{Escape(m.Reason!)}\"," +
                        $"\"source_count\":{m.SourceCount}}}");
                }
                sw.Write("],\"committed_identity\":[");
                for (var i = 0; i < committedIdentity.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var c = committedIdentity[i];
                    sw.Write(
                        $"{{\"path\":\"{Escape(c.Path)}\"," +
                        $"\"key\":\"{Escape(c.Key)}\"," +
                        $"\"value\":\"{Escape(c.Value)}\"}}");
                }
                sw.Write("],\"global_config\":[");
                for (var i = 0; i < globalConfigNotes.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var n = globalConfigNotes[i];
                    sw.Write(
                        $"{{\"path\":\"{Escape(n.Path)}\"," +
                        $"\"key\":\"{Escape(n.Key)}\"," +
                        $"\"reason\":\"{Escape(n.Reason)}\"}}");
                }
                sw.Write("],\"conventions\":[");
                for (var i = 0; i < longConventions.Count; i++)
                {
                    if (i > 0) sw.Write(',');
                    var c = longConventions[i];
                    sw.Write(
                        $"{{\"scope\":\"{c.Scope}\"," +
                        $"\"path\":\"{Escape(c.Path)}\"," +
                        $"\"lines\":{c.Lines}}}");
                }
                sw.Write(']');
                if (statusPage is not null)
                    sw.Write(
                        $",\"status_page\":{{\"path\":\"{Escape(statusPage.Path)}\"," +
                        (statusPage.Generated is { } g ? $"\"generated\":\"{DueDates.ToWire(g)}\"," : "") +
                        $"\"stale\":{(statusPage.Stale ? "true" : "false")}}}");
                sw.Write('}');
                Console.WriteLine(sw.ToString());
            }
            else if (invalidItems.Count == 0 && strayFiles.Count == 0 && broken.Count == 0 && malformed.Count == 0
                && titles.Count == 0 && hierarchy.Count == 0 && origins.Count == 0 && duplicateIds.Count == 0
                && identityDrift.Count == 0
                && healthFindings.Count == 0 && projectPointers.Count == 0
                && orphanFolders.Count == 0 && orphanItems.Count == 0
                && malformedMilestones.Count == 0 && multiMilestones.Count == 0
                && doneWithOpenChildren.Count == 0 && staleBlockers.Count == 0 && unregisteredSlates.Count == 0
                && proseRefs.Count == 0 && proseLinks.Count == 0 && sourcePaths.Count == 0
                && svgFiles.Count == 0 && mountIssues.Count == 0
                && committedIdentity.Count == 0 && globalConfigNotes.Count == 0 && longConventions.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]ok[/] no issues found");
            }
            else
            {
                if (invalidItems.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[red]invalid item files ({invalidItems.Count})[/]");
                    foreach (var v in invalidItems)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, v.SourcePath))}[/] " +
                            $"[red]{Markup.Escape(v.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: these files won't load as items and vanish from list/tree — fix the " +
                        "folder name / id / frontmatter, or remove the stray file[/]");
                }
                if (strayFiles.Count > 0)
                {
                    if (invalidItems.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]stray / hidden files ({strayFiles.Count})[/]");
                    foreach (var s in strayFiles)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, s.SourcePath))}[/] " +
                            $"[red]{Markup.Escape(s.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: nothing shows these files — rename or move them to the expected shape, " +
                        "link them from the doc they belong to, or remove them[/]");
                }
                if (broken.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]broken link targets ({broken.Count})[/]");
                    foreach (var b in broken)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, b.SourcePath))}[/] " +
                            $"{LinkRels.ToWire(b.Row.Rel)} → {Markup.Escape(b.Row.TargetText)} " +
                            $"[red]{Markup.Escape(b.Row.TargetPath)}[/]");
                    }
                    AnsiConsole.MarkupLine("[dim]hint: 'knit relink' to repair interactively[/]");
                }
                if (malformed.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]malformed item-link ids ({malformed.Count})[/]");
                    foreach (var m in malformed)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, m.SourcePath))}[/] " +
                            $"{LinkRels.ToWire(m.Row.Rel)} → {Markup.Escape(m.Row.TargetText)} " +
                            $"[red]{Markup.Escape(m.Row.TargetPath)}[/]");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: the target exists but its id isn't a valid NanoID, so the link " +
                        "won't resolve. No auto-fix — repoint the row at the correct item by hand.[/]");
                }
                if (titles.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]title drift ({titles.Count})[/]");
                    foreach (var t in titles)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, t.SourcePath))}[/]");
                        AnsiConsole.MarkupLine($"    frontmatter: {Markup.Escape(t.FrontmatterTitle)}");
                        AnsiConsole.MarkupLine($"    body h1:     [red]{Markup.Escape(t.BodyH1)}[/]");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: edit the entry or update the frontmatter title " +
                        "with 'knit set <id> title=...'[/]");
                }
                if (hierarchy.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]hierarchy violations ({hierarchy.Count})[/]");
                    foreach (var h in hierarchy)
                    {
                        var parent = h.ParentId is null
                            ? "(root)"
                            : $"{Out.TitledPlain(ctx, h.ParentId)} ({h.ParentType?.ToWire() ?? "?"})";
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, h.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, h.ChildId)} ({h.ChildType.ToWire()}) " +
                            $"under [red]{Markup.Escape(parent)}[/]");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: 'knit retype <id> --to <type> --parent <id>' to re-fit, " +
                        "or 'knit unlink <id> parent' to detach[/]");
                }

                if (origins.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0 || hierarchy.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]dangling ADR origins ({origins.Count})[/]");
                    foreach (var o in origins)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, o.SourcePath))}[/] " +
                            $"origin → [red]{Markup.Escape(o.Ref)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: the design/item this ADR records was removed or renamed — " +
                        "fix the 'origin:' frontmatter or restore the target[/]");
                }

                if (duplicateIds.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0 || hierarchy.Count > 0 || origins.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]duplicate KB ids ({duplicateIds.Count})[/]");
                    foreach (var d in duplicateIds)
                    {
                        AnsiConsole.MarkupLine($"  {d.Bucket} id [red]{Markup.Escape(d.Id)}[/] claimed by {d.Paths.Count} files:");
                        foreach (var p in d.Paths)
                            AnsiConsole.MarkupLine($"    [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, p))}[/]");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: refs resolve ambiguously to whichever file is enumerated first — " +
                        "merge the duplicates and delete the stale one[/]");
                }

                if (identityDrift.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0 || hierarchy.Count > 0 || origins.Count > 0 || duplicateIds.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]stale identity fields ({identityDrift.Count})[/]");
                    foreach (var d in identityDrift)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, d.SourcePath))}[/] " +
                            $"{Markup.Escape(d.Field)}: [red]{Markup.Escape(d.Declared)}[/] " +
                            $"but the filename says [green]{Markup.Escape(d.Expected)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: refs resolve by filename, so the declared value resolves nowhere — " +
                        "repair with `knit relink --ids` (the filename wins)[/]");
                }

                if (healthFindings.Count > 0)
                {
                    if (invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0 || hierarchy.Count > 0 || origins.Count > 0 || duplicateIds.Count > 0 || identityDrift.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]health on non-project items ({healthFindings.Count})[/]");
                    foreach (var h in healthFindings)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, h.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, h.ItemId)} ({h.Type.ToWire()}) " +
                            $"health = [red]{Markup.Escape(h.Health.ToWire())}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: health is a project-only field — clear it with " +
                        "'knit set <id> health=' or retype the item to project[/]");
                }

                var anyBeforeProjects = invalidItems.Count > 0 || strayFiles.Count > 0 || broken.Count > 0 || malformed.Count > 0 || titles.Count > 0 || hierarchy.Count > 0 || origins.Count > 0 || duplicateIds.Count > 0 || healthFindings.Count > 0;
                if (projectPointers.Count > 0)
                {
                    if (anyBeforeProjects) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]bad project doc-folder pointers ({projectPointers.Count})[/]");
                    foreach (var p in projectPointers)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, p.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, p.ItemId)} ({p.Type.ToWire()}) " +
                            $"project = {Markup.Escape(p.Slug)} [red]{Markup.Escape(p.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: clear the pointer with 'knit set <id> project=' or scaffold the " +
                        "folder with 'knit project new <slug>'[/]");
                }
                if (malformedMilestones.Count > 0)
                {
                    if (anyBeforeProjects || projectPointers.Count > 0) AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]malformed milestone sub-docs ({malformedMilestones.Count})[/]");
                    foreach (var m in malformedMilestones)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, m.SourcePath))}[/] " +
                            $"[red]{Markup.Escape(m.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: anything under milestones/ must parse as a milestone sub-doc — " +
                        "fix the frontmatter or move free-form files up into the project folder[/]");
                }

                var hardCountInner = invalidItems.Count + strayFiles.Count + broken.Count + malformed.Count + titles.Count + hierarchy.Count + origins.Count + duplicateIds.Count + healthFindings.Count + projectPointers.Count + malformedMilestones.Count;
                if (multiMilestones.Count > 0)
                {
                    if (hardCountInner > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]items with multiple milestones ({multiMilestones.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var m in multiMilestones)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, m.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, m.ItemId)} carries [{sev}]{m.Count}[/] Milestone rows");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: one milestone per item is the convention — remove the extra rows " +
                        "with 'knit unlink <id> <project>/<slug>'[/]");
                }
                if (orphanFolders.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]orphan project doc folders ({orphanFolders.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var o in orphanFolders)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, o.FolderPath))}[/] " +
                            $"[{sev}]{Markup.Escape(o.Slug)}[/] has no item pointing at it");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: link an item with 'knit project new " +
                        "<slug> --item <id>' / 'knit set <id> project=<slug>', or remove it with " +
                        "'knit project delete <slug>'[/]");
                }
                if (orphanItems.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]project items without a doc folder ({orphanItems.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var o in orphanItems)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, o.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, o.ItemId)} [{sev}]has no project: pointer[/] — no coexisting doc folder");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: join a doc folder with 'knit project new <slug> --item <id>' or " +
                        "'knit set <id> project=<slug>'[/]");
                }
                if (doneWithOpenChildren.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0 || orphanItems.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]done items with open children ({doneWithOpenChildren.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var d in doneWithOpenChildren)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, d.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, d.ParentId)} (done) has open child " +
                            $"[{sev}]{Out.Titled(ctx, d.ChildId)}[/] ({d.ChildType.ToWire()}, {d.ChildState.ToWire()})");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: closing does not cascade — close the child ('knit done'/'knit dismiss') " +
                        "or reopen the parent ('knit reopen <id>')[/]");
                }
                if (staleBlockers.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0 || orphanItems.Count > 0 || doneWithOpenChildren.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]open items blocked by closed ones ({staleBlockers.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var b in staleBlockers)
                    {
                        var itemShort = Out.Short(ctx, b.ItemId);
                        var fix = b.Sole ? $"knit unblock {itemShort}" : $"knit unlink {itemShort} {Out.Short(ctx, b.BlockerId)}";
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, b.SourcePath))}[/] " +
                            $"{Out.Titled(ctx, b.ItemId)} blocked by [{sev}]{Out.Titled(ctx, b.BlockerId)}[/] " +
                            $"({b.BlockerState.ToWire()}) [dim]→[/] {Markup.Escape(fix)}");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: closing a blocker resolves nothing — resolve the block, or point the row " +
                        "at a new blocker if the block stands[/]");
                }
                if (unregisteredSlates.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0 || orphanItems.Count > 0 || doneWithOpenChildren.Count > 0 || staleBlockers.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]slates not in the registry ({unregisteredSlates.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var u in unregisteredSlates)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [{sev}]{Markup.Escape(u.Slate)}[/] on {u.Paths.Count} item{(u.Paths.Count == 1 ? "" : "s")}");
                        foreach (var p in u.Paths.Take(5))
                            AnsiConsole.MarkupLine($"    [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, p))}[/]");
                        if (u.Paths.Count > 5)
                            AnsiConsole.MarkupLine($"    [dim]+{u.Paths.Count - 5} more[/]");
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: 'knit slate add <name>' to keep it, or 'knit slate rm <name>' to clear it " +
                        "from the items[/]");
                }
                if (proseRefs.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0 || orphanItems.Count > 0 || doneWithOpenChildren.Count > 0 || staleBlockers.Count > 0 || unregisteredSlates.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]dangling prose refs ({proseRefs.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var p in proseRefs)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, p.SourcePath))}[/] " +
                            $"`[{sev}]{Markup.Escape(p.Ref)}[/]`" + ProseRefNote(p));
                }
                if (proseLinks.Count > 0)
                {
                    if (hardCountInner > 0 || multiMilestones.Count > 0 || orphanFolders.Count > 0 || orphanItems.Count > 0 || doneWithOpenChildren.Count > 0 || staleBlockers.Count > 0 || unregisteredSlates.Count > 0 || proseRefs.Count > 0) AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]dangling prose links ({proseLinks.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var p in proseLinks)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, p.SourcePath))}[/] " +
                            $"{(p.IsImage ? "image" : "link")} → [{sev}]{Markup.Escape(p.Target)}[/]" +
                            (p.Reason == RepoVerifier.ProseLinkReasons.OutsideRepo
                                ? " [dim]leaves the repo root and no mount covers it[/]"
                                : ""));
                }
                if (proseRefs.Count > 0 || proseLinks.Count > 0)
                    AnsiConsole.MarkupLine(
                        "[dim]hint: prose refs aren't auto-maintained — fix the source, or rename via " +
                        "'knit kb rename'; after renaming a heading, grep the old section ref. " +
                        "'knit relink' expands a short item: ref that is still unique. " +
                        "Pass --strict to fail CI on these.[/]");
                if (sourcePaths.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]KB sources pointing at nothing ({sourcePaths.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var s in sourcePaths)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, s.SourcePath))}[/] " +
                            (s.Mount is null ? "" : $"{Markup.Escape(s.Mount)}:") +
                            $"[{sev}]{Markup.Escape(s.StoredPath)}[/] [dim]{Markup.Escape(s.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: restore the file, or repoint the entry with 'knit kb set <ref> path <path>'[/]");
                }
                if (svgFiles.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]SVG files to fix ({svgFiles.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var s in svgFiles)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, s.SourcePath))}[/] " +
                            $"[{sev}]{Markup.Escape(s.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: the web UI strips what isn't allowed and draws the rest; read the file's outline " +
                        "with 'knit ref show <ref>#<file>.svg'[/]");
                }
                if (mountIssues.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    // One line per mount, never per source — an unresolved
                    // mount is a single config problem.
                    AnsiConsole.MarkupLine($"[yellow]mounts needing attention ({mountIssues.Count})[/] [dim](warning)[/]");
                    foreach (var m in mountIssues)
                    {
                        var where = m.Source ?? "undeclared";
                        var sources = m.SourceCount == 1 ? "1 source" : $"{m.SourceCount} sources";
                        AnsiConsole.MarkupLine(
                            $"  [bold]{Markup.Escape(m.Name)}[/] [dim]({where}, {sources})[/] " +
                            $"[yellow]{Markup.Escape(m.Reason!)}[/]" +
                            (m.DeclaredPath is null ? "" : $" [dim]{Markup.Escape(m.DeclaredPath)}[/]"));
                    }
                    AnsiConsole.MarkupLine(
                        "[dim]hint: the KB entries stay valid — only opening the file is disabled. " +
                        "Repoint the committed path with 'knit mount set <name> <path>', or override it " +
                        "for this machine with 'knit mount set <name> <path> --local'.[/]");
                }
                if (committedIdentity.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    var sev = strict ? "red" : "yellow";
                    AnsiConsole.MarkupLine($"[{sev}]identity in the committed config ({committedIdentity.Count})[/]" + (strict ? "" : " [dim](warning)[/]"));
                    foreach (var c in committedIdentity)
                        AnsiConsole.MarkupLine(
                            $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, c.Path))}[/] " +
                            $"{c.Key} = [{sev}]{Markup.Escape(c.Value)}[/]");
                    AnsiConsole.MarkupLine(
                        "[dim]hint: it beats everyone's global config, so whoever clones this repo records as that " +
                        "person — 'knit config unset --repo <key>', then each person runs " +
                        "'knit config set --global <key> <value>'[/]");
                }
                if (globalConfigNotes.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    // Not counted: the file belongs to the machine, not the
                    // repo — but a setting that applies nowhere still has to be
                    // said out loud, or it is believed to apply.
                    AnsiConsole.MarkupLine(
                        $"[yellow]user-level config ignored ({globalConfigNotes.Count})[/] [dim](not a repo defect)[/]");
                    foreach (var n in globalConfigNotes)
                        AnsiConsole.MarkupLine(
                            $"  [bold]{Markup.Escape(n.Key.Length == 0 ? Path.GetFileName(n.Path) : n.Key)}[/] " +
                            $"[yellow]{Markup.Escape(n.Reason)}[/]");
                    AnsiConsole.MarkupLine(
                        $"[dim]hint: the file is {Markup.Escape(globalConfigNotes[0].Path)} — " +
                        "see 'knit config list' for which layer every value comes from.[/]");
                }
                if (longConventions.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    // Not counted: length is a cost, not a defect.
                    AnsiConsole.MarkupLine($"[yellow]long conventions ({longConventions.Count})[/] [dim](warning)[/]");
                    foreach (var c in longConventions)
                        AnsiConsole.MarkupLine(
                            $"  [bold]{c.Scope}[/] [dim]{Markup.Escape(c.Path)}[/] " +
                            $"[yellow]{c.Lines} lines[/]");
                    AnsiConsole.MarkupLine(
                        $"[dim]hint: every agent run reads them — keep them under {Conventions.LongLineCount} lines; " +
                        "a rule a machine can check belongs in a template, a config key or a verify check[/]");
                }
            }

            // Informational, never a failure and never promoted by --strict:
            // an item legitimately serving another project's milestone (see
            // `knit milestone move`). Printed outside the issue gate so a
            // clean repo still says "ok".
            if (!json && crossProjectMilestones.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[blue]cross-project milestone links ({crossProjectMilestones.Count})[/] [dim](info)[/]");
                foreach (var c in crossProjectMilestones)
                    AnsiConsole.MarkupLine(
                        $"  [dim]{Markup.Escape(Path.GetRelativePath(ctx.Root, c.SourcePath))}[/] " +
                        $"{Out.Titled(ctx, c.ItemId)} [dim]({Markup.Escape(c.ItemProject)})[/] → " +
                        $"{Markup.Escape(c.MilestoneProject)}/{Markup.Escape(c.MilestoneSlug)}");
                AnsiConsole.MarkupLine(
                    "[dim]hint: expected after 'knit milestone move' — items are re-pointed, not reassigned. " +
                    "Re-parent the item or re-link it if that isn't what you meant.[/]");
            }

            // A hint, never a failure and never promoted by --strict: an old
            // page is a chore waiting, not a repo defect.
            if (!json && statusPage is { Stale: true })
            {
                AnsiConsole.WriteLine();
                var path = Markup.Escape(Path.GetRelativePath(ctx.Root, statusPage.Path));
                var age = statusPage.Generated is { } g
                    ? $"generated {DueDates.ToWire(g)}, older than {ctx.Config.StatusReportStaleDays} day{(ctx.Config.StatusReportStaleDays == 1 ? "" : "s")}"
                    : "no Generated date";
                AnsiConsole.MarkupLine($"[blue]status page is stale[/] [dim](hint)[/]");
                AnsiConsole.MarkupLine($"  [dim]{path}[/] {Markup.Escape(age)}");
                AnsiConsole.MarkupLine(
                    "[dim]hint: 'knit status report' regenerates it; delete the page to stop this hint, " +
                    "or raise status_report.stale_days[/]");
            }

            // The structured classes are always hard failures (exit 4). Prose
            // findings are warnings (exit 0) unless --strict promotes them.
            var hardCount = invalidItems.Count + strayFiles.Count + broken.Count + malformed.Count + titles.Count + hierarchy.Count + origins.Count + duplicateIds.Count + identityDrift.Count + healthFindings.Count + projectPointers.Count + malformedMilestones.Count;
            var proseCount = proseRefs.Count + proseLinks.Count + sourcePaths.Count + svgFiles.Count + multiMilestones.Count + orphanFolders.Count + orphanItems.Count + doneWithOpenChildren.Count + staleBlockers.Count + unregisteredSlates.Count + undeclaredMounts.Count + committedIdentity.Count;
            return (hardCount > 0 || (strict && proseCount > 0)) ? (int)ErrorCode.Conflict : 0;
        });
        return cmd;
    }

    private static string ProseRefNote(RepoVerifier.ProseRef p) => p.Reason switch
    {
        RepoVerifier.ProseRefReasons.ShortId => $" [dim]short id — full id {p.Candidates[0]}[/]",
        RepoVerifier.ProseRefReasons.AmbiguousId =>
            $" [dim]ambiguous short id — matches {string.Join(", ", p.Candidates.Take(5))}" +
            (p.Candidates.Count > 5 ? $" +{p.Candidates.Count - 5} more" : "") + "[/]",
        RepoVerifier.ProseRefReasons.MissingSection =>
            " [dim]no such section — list the anchors with 'knit ref show <ref> --sections'[/]",
        RepoVerifier.ProseRefReasons.SuffixedAnchor =>
            " [dim]anchor of a repeated heading — it shifts when one is added above; make the heading unique[/]",
        RepoVerifier.ProseRefReasons.ReservedAnchor =>
            " [dim]heading anchor reads as a diagram pointer — rename the heading[/]",
        _ => "",
    };

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
