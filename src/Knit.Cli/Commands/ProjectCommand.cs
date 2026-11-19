using System.CommandLine;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Domain;
using Knit.Core.Ids;
using Knit.Core.Projects;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit project</c> — doc-folder operations (.knit/projects/). The
/// tracker side of a project is an ordinary item of type <c>project</c>;
/// this command manages its workspace folder (readme, roadmap, milestone
/// sub-docs). <c>milestone show</c> is the backlinks surface: it lists
/// every item carrying a <c>Milestone</c> row targeting the sub-doc.
/// </summary>
public static class ProjectCommand
{
    public static Command Build()
    {
        var cmd = new Command("project", "Project doc-folder operations (.knit/projects/)");
        cmd.Subcommands.Add(BuildNew());
        cmd.Subcommands.Add(BuildRoadmap());
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildDelete());
        cmd.Subcommands.Add(BuildDoc());
        cmd.Subcommands.Add(BuildMilestone());
        return cmd;
    }

    private static Command BuildDelete()
    {
        var c = new Command("delete",
            "Remove a project: deletes the project item + doc folder, but re-roots (does not cascade) direct children");
        var slugArg = new Argument<string>("slug");
        var yesOpt = new Option<bool>("--yes") { Aliases = { "-y" }, Description = "Skip the confirmation prompt" };
        var scrubOpt = new Option<bool>("--scrub-links")
        {
            Description = "Also remove back-references pointing here (Milestone rows, relation links). Default: leave dangling",
        };
        c.Arguments.Add(slugArg);
        c.Options.Add(yesOpt);
        c.Options.Add(scrubOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var scrub = parse.GetValue(scrubOpt);
            var yes = parse.GetValue(yesOpt);

            var folderExists = Directory.Exists(ProjectsLayout.ProjectDir(ctx.Root, slug));
            var hasItem = ctx.Store.AllItems().Any(i => string.Equals(i.Project, slug, StringComparison.Ordinal));
            if (!folderExists && !hasItem)
                return Out.Error(
                    $"no project '{slug}' — neither a doc folder nor a project item points at it",
                    ErrorCode.NotFound, json);

            if (!yes && !json && !Console.IsErrorRedirected)
            {
                var impact = Knit.Data.ProjectRemover.Preview(ctx.Store, ctx.Root, slug);
                AnsiConsole.MarkupLine($"[yellow]Removing project[/] {Markup.Escape(slug)}:");
                if (impact.ItemId is { } iid)
                    AnsiConsole.MarkupLine($"  [dim]item[/] {Out.Titled(ctx, iid)} deleted");
                AnsiConsole.MarkupLine($"  [dim]doc folder[/] {impact.MilestoneCount} milestone(s), {impact.FileCount} file(s) deleted");
                if (impact.DirectChildIds.Count > 0)
                    AnsiConsole.MarkupLine($"  [dim]{impact.DirectChildIds.Count} direct child(ren) re-rooted (parent cleared)[/]");
                if (impact.DanglingItemIds.Count > 0)
                    AnsiConsole.MarkupLine(scrub
                        ? $"  [dim]{impact.DanglingItemIds.Count} item(s) with links here will be scrubbed[/]"
                        : $"  [dim]{impact.DanglingItemIds.Count} item(s) link here — left dangling (run `knit verify`)[/]");
                AnsiConsole.MarkupLine("  [dim]recoverable via git until committed[/]");
                if (!AnsiConsole.Confirm("Proceed?", defaultValue: false))
                    return Out.Error("aborted", ErrorCode.BadArgument, json);
            }

            var result = Knit.Data.ProjectRemover.Remove(ctx.Store, ctx.Root, slug, scrub);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("slug", slug);
                    if (result.RemovedItemId is { } rid) w.WriteString("removed_item_id", rid);
                    else w.WriteNull("removed_item_id");
                    WriteStringArray(w, "re_rooted_child_ids", result.ReRootedChildIds);
                    WriteStringArray(w, "scrubbed_item_ids", result.ScrubbedItemIds);
                    WriteStringArray(w, "dangling_item_ids", result.DanglingItemIds);
                    w.WriteNumber("milestone_count", result.DeletedMilestoneCount);
                    w.WriteNumber("file_count", result.DeletedFileCount);
                    w.WriteBoolean("folder_deleted", result.FolderDeleted);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]removed[/] project {Markup.Escape(slug)}");
            if (result.ReRootedChildIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]re-rooted {result.ReRootedChildIds.Count} direct child(ren) to top level[/]");
            if (result.ScrubbedItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]scrubbed links in {result.ScrubbedItemIds.Count} item(s)[/]");
            else if (result.DanglingItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]{result.DanglingItemIds.Count} item(s) link here and are left dangling; run `knit verify`[/]");
            return 0;
        });
        return c;
    }

    private static void WriteStringArray(Utf8JsonWriter w, string name, IReadOnlyList<string> ids)
    {
        w.WriteStartArray(name);
        foreach (var id in ids) w.WriteStringValue(id);
        w.WriteEndArray();
    }

    private static Command BuildNew()
    {
        var c = new Command("new",
            "Create a project: scaffold the doc folder + a linked tracker item (project) in one action");
        var slugArg = new Argument<string>("slug");
        var titleOpt = new Option<string?>("--title") { Description = "Project title (readme H1 + item title; defaults to the slug)" };
        var goalOpt = new Option<string?>("--goal") { Description = "Fill the readme's ## Goal section (defaults to a prompt line)" };
        var roadmapOpt = new Option<bool>("--roadmap") { Description = "Also scaffold the optional roadmap.md narrative" };
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite existing files" };
        var itemOpt = new Option<string?>("--item")
        {
            Description = "Link an existing project item to the folder instead of creating a new one",
        };
        var noItemOpt = new Option<bool>("--no-item")
        {
            Description = "Scaffold only the doc folder — no tracker item (leaves the folder an orphan; verify warns)",
        };
        c.Arguments.Add(slugArg);
        c.Options.Add(titleOpt);
        c.Options.Add(goalOpt);
        c.Options.Add(roadmapOpt);
        c.Options.Add(forceOpt);
        c.Options.Add(itemOpt);
        c.Options.Add(noItemOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var title = parse.GetValue(titleOpt) ?? slug;
            var force = parse.GetValue(forceOpt);
            var goal = parse.GetValue(goalOpt);
            var roadmap = parse.GetValue(roadmapOpt);
            var noItem = parse.GetValue(noItemOpt);
            var linkRaw = parse.GetValue(itemOpt);

            if (noItem && linkRaw is not null)
                return Out.Error("--no-item and --item are mutually exclusive", ErrorCode.BadArgument, json);

            // Folder-only escape hatch — intentionally
            // orphaned; verify warns until an item is linked.
            if (noItem)
            {
                var folderRes = ProjectScaffold.NewProject(ctx.Root, slug, title, force, goal);
                if (folderRes.IsError)
                {
                    var err = folderRes.GetErrorOrDefault()!;
                    return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
                }
                if (roadmap)
                {
                    var rm = ProjectScaffold.NewRoadmap(ctx.Root, slug, title, force);
                    if (rm.IsError)
                    {
                        var err = rm.GetErrorOrDefault()!;
                        return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
                    }
                }
                var path = folderRes.GetValueOrThrow();
                if (json)
                {
                    Console.WriteLine($"{{\"slug\":\"{slug}\",\"path\":\"{path.Replace("\\", "\\\\")}\",\"item_id\":null}}");
                    return 0;
                }
                AnsiConsole.MarkupLine($"[green]created[/] {Markup.Escape(slug)} [dim]{Markup.Escape(path)}[/] [yellow](no item — orphan folder)[/]");
                AnsiConsole.MarkupLine(
                    $"[dim]hint: link it later with 'knit project new {Markup.Escape(slug)} --item <id>' or 'knit set <id> project={Markup.Escape(slug)}'[/]");
                return 0;
            }

            string? linkItemId = null;
            if (linkRaw is not null)
            {
                var (item, _) = ItemUtils.Resolve(ctx, linkRaw);
                if (item is null) return Out.Error($"item not found: {linkRaw}", ErrorCode.NotFound, json);
                linkItemId = item.Id;
            }

            var result = Knit.Data.ProjectCreator.Create(
                ctx.Store, ctx.Root, ctx.Config.Handle!, slug, title, roadmap, force, linkItemId, goal);
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }
            var r = result.GetValueOrThrow();
            ctx.Refresh();

            if (json)
            {
                Console.WriteLine(
                    $"{{\"slug\":\"{slug}\",\"path\":\"{r.FolderPath.Replace("\\", "\\\\")}\",\"item_id\":\"{r.Item.Id}\",\"created_item\":{(r.CreatedItem ? "true" : "false")}}}");
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]created[/] project {Markup.Escape(slug)} [dim]{Markup.Escape(r.FolderPath)}[/]");
            AnsiConsole.MarkupLine(r.CreatedItem
                ? $"[dim]tracker item {Out.Titled(ctx, r.Item)} created and linked (project={Markup.Escape(slug)})[/]"
                : $"[dim]linked existing item {Out.Titled(ctx, r.Item)} (project={Markup.Escape(slug)})[/]");
            return 0;
        }, needsHandle: p => !p.GetValue(noItemOpt));
        return c;
    }

    private static Command BuildRoadmap()
    {
        var c = new Command("roadmap",
            "Scaffold roadmap.md for an existing project — standalone, never touches the readme");
        var slugArg = new Argument<string>("slug");
        var titleOpt = new Option<string?>("--title") { Description = "Roadmap H1 title (defaults to the slug)" };
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite an existing roadmap.md" };
        c.Arguments.Add(slugArg);
        c.Options.Add(titleOpt);
        c.Options.Add(forceOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var title = parse.GetValue(titleOpt) ?? slug;

            var result = ProjectScaffold.NewRoadmap(ctx.Root, slug, title, parse.GetValue(forceOpt));
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }
            var path = result.GetValueOrThrow();
            if (json)
            {
                Console.WriteLine($"{{\"slug\":\"{slug}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]created[/] {Markup.Escape(slug)} roadmap [dim]{Markup.Escape(path)}[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildDoc()
    {
        var cmd = new Command("doc", "Free-form doc operations (projects/<slug>/... — inert to the link graph)");
        cmd.Subcommands.Add(BuildDocRename());
        return cmd;
    }

    private static Command BuildDocRename()
    {
        var c = new Command("rename",
            "Rename / move a free-form doc or folder; inbound prose links are repointed");
        var slugArg = new Argument<string>("slug");
        var oldArg = new Argument<string>("old-path") { Description = "Current project-folder-relative path" };
        var newArg = new Argument<string>("new-path") { Description = "New project-folder-relative path" };
        c.Arguments.Add(slugArg);
        c.Arguments.Add(oldArg);
        c.Arguments.Add(newArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slug = parse.GetValue(slugArg)!;
            var oldPath = parse.GetValue(oldArg)!;
            var newPath = parse.GetValue(newArg)!;

            var result = ProjectDoc.Move(ctx.Root, slug, oldPath, newPath, new Knit.Data.ProseRefRewriter(ctx.Store, ctx.Root));
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }
            var r = result.GetValueOrThrow();
            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteString("slug", slug);
                    w.WriteString("old_path", r.OldPath);
                    w.WriteString("new_path", r.NewPath);
                    w.WriteBoolean("folder_form", r.WasFolderForm);
                    WriteStringArray(w, "files_relinked", r.FilesRelinked);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]renamed[/] {Markup.Escape(slug)} [dim]{Markup.Escape(oldPath)}[/] → [dim]{Markup.Escape(newPath)}[/]");
            if (r.FilesRelinked.Count > 0)
                AnsiConsole.MarkupLine($"[dim]repointed links in {r.FilesRelinked.Count} file(s)[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildList()
    {
        var c = new Command("list", "List project doc folders");
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var slugs = ProjectsLayout.ProjectSlugs(ctx.Root);
            // The health signal lives on the linked tracker item, but this is
            // the one view where every row is a project — so it belongs here.
            var health = ctx.Store.AllItems()
                .Where(i => i.Type == ItemType.Project && !string.IsNullOrEmpty(i.Project) && i.Health is not null)
                .GroupBy(i => i.Project!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Health, StringComparer.Ordinal);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartArray();
                    foreach (var slug in slugs)
                    {
                        w.WriteStartObject();
                        w.WriteString("slug", slug);
                        w.WriteString("path", ProjectsLayout.ProjectDir(ctx.Root, slug));
                        w.WriteBoolean("has_roadmap", File.Exists(ProjectsLayout.RoadmapPath(ctx.Root, slug)));
                        w.WriteNumber("milestones", ProjectsLayout.MilestoneFiles(ctx.Root, slug).Count);
                        if (health.GetValueOrDefault(slug) is { } h) w.WriteString("health", h.ToWire());
                        else w.WriteNull("health");
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            if (slugs.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]no project doc folders[/]");
                return 0;
            }
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumns("slug", "health", "milestones", "path");
            foreach (var slug in slugs)
                table.AddRow(
                    Markup.Escape(slug),
                    health.GetValueOrDefault(slug) is { } h ? Markup.Escape(h.ToWire()) : "[dim]—[/]",
                    ProjectsLayout.MilestoneFiles(ctx.Root, slug).Count.ToString(),
                    Markup.Escape(ProjectsLayout.ProjectDir(ctx.Root, slug)));
            AnsiConsole.Write(table);
            return 0;
        });
        return c;
    }

    private static Command BuildMilestone()
    {
        var cmd = new Command("milestone", "Milestone sub-doc operations (projects/<slug>/milestones/)");
        cmd.Subcommands.Add(BuildMilestoneNew());
        cmd.Subcommands.Add(BuildMilestoneList());
        cmd.Subcommands.Add(BuildMilestoneShow());
        cmd.Subcommands.Add(BuildMilestoneSet());
        cmd.Subcommands.Add(BuildMilestoneRename());
        cmd.Subcommands.Add(BuildMilestoneMove());
        cmd.Subcommands.Add(BuildMilestoneDelete());
        return cmd;
    }

    private static Command BuildMilestoneNew()
    {
        var c = new Command("new", "Scaffold a milestone sub-doc (projects/<project>/milestones/<slug>.md) — requires a roadmap (`knit project roadmap <project>`)");
        var projectArg = new Argument<string>("project");
        var slugArg = new Argument<string>("slug");
        var titleOpt = new Option<string?>("--title") { Description = "Milestone title (defaults to the slug)" };
        var targetOpt = new Option<string?>("--target") { Description = "Target date (YYYY-MM-DD)" };
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite an existing milestone" };
        c.Arguments.Add(projectArg);
        c.Arguments.Add(slugArg);
        c.Options.Add(titleOpt);
        c.Options.Add(targetOpt);
        c.Options.Add(forceOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var project = parse.GetValue(projectArg)!;
            var slug = parse.GetValue(slugArg)!;
            var title = parse.GetValue(titleOpt) ?? slug;

            DateOnly? target = null;
            var targetRaw = parse.GetValue(targetOpt);
            if (!string.IsNullOrEmpty(targetRaw))
            {
                if (!DueDates.TryParse(targetRaw, out var t))
                    return Out.Error($"invalid --target date '{targetRaw}' — expected YYYY-MM-DD", ErrorCode.BadArgument, json);
                target = t;
            }

            var result = ProjectScaffold.NewMilestone(ctx.Root, project, slug, title, target, parse.GetValue(forceOpt), ctx.Yaml);
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message, ErrorCodes.FromKind(err.Kind), json);
            }
            var path = result.GetValueOrThrow();
            if (json)
            {
                Console.WriteLine($"{{\"project\":\"{project}\",\"slug\":\"{slug}\",\"path\":\"{path.Replace("\\", "\\\\")}\"}}");
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]created[/] {Markup.Escape(project)}/{Markup.Escape(slug)} [dim]{Markup.Escape(path)}[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneList()
    {
        var c = new Command("list", "List milestone sub-docs (all projects, or one)");
        var projectArg = new Argument<string?>("project") { Arity = ArgumentArity.ZeroOrOne };
        c.Arguments.Add(projectArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var only = parse.GetValue(projectArg);
            var projects = only is null ? ProjectsLayout.ProjectSlugs(ctx.Root) : [only];
            // Per project, order by manual rank then slug (matches the SPA
            // roadmap spine + WebApi list); projects stay grouped.
            var docs = new List<MilestoneDoc>();
            foreach (var p in projects)
                docs.AddRange(ProjectsLayout.MilestoneFiles(ctx.Root, p)
                    .Select(file => MilestoneFile.Read(file, p, ctx.Yaml).Doc)
                    .OrderBy(d => Rank.IsValid(d.Rank) ? d.Rank! : "￿", StringComparer.Ordinal)
                    .ThenBy(d => d.Slug, StringComparer.Ordinal));

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartArray();
                    foreach (var d in docs) WriteDoc(w, d);
                    w.WriteEndArray();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            if (docs.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]no milestones[/]");
                return 0;
            }
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumns("milestone", "title", "target", "state");
            foreach (var d in docs)
                table.AddRow(
                    new Markup(Markup.Escape(d.QualifiedSlug)),
                    Out.Prose(d.Title),
                    new Markup(d.Target is { } t ? DueDates.ToWire(t) : "[dim]—[/]"),
                    new Markup(Markup.Escape(d.State.ToWire())));
            AnsiConsole.Write(table);
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneShow()
    {
        var c = new Command("show", "Show a milestone sub-doc + its backlinks (items linking it)");
        var refArg = new Argument<string>("milestone") { Description = "<slug> (unambiguous across projects) or <project>/<slug>" };
        var pathOnlyOpt = new Option<bool>("--path-only");
        c.Arguments.Add(refArg);
        c.Options.Add(pathOnlyOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var (target, err) = Knit.Data.MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, parse.GetValue(refArg)!);
            if (target is null) return Out.Error(err!, ErrorCode.NotFound, json);

            if (parse.GetValue(pathOnlyOpt))
            {
                Console.WriteLine(target.Path);
                return 0;
            }

            var (doc, _) = MilestoneFile.Read(target.Path, target.ProjectSlug, ctx.Yaml);
            var body = MilestoneFile.ReadBody(target.Path) ?? string.Empty;
            var backlinks = Knit.Data.MilestoneResolver.LinkedItems(ctx.Store, target.ProjectSlug, target.Slug);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    WriteDocFields(w, doc);
                    w.WriteString("body", body);
                    w.WriteStartArray("backlinks");
                    foreach (var i in backlinks)
                    {
                        w.WriteStartObject();
                        w.WriteString("id", i.Id);
                        w.WriteString("type", i.Type.ToWire());
                        w.WriteString("state", i.State.ToWire());
                        w.WriteString("title", i.Title);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(doc.QualifiedSlug)}[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(doc.Path)}[/]");
            AnsiConsole.WriteLine();
            var t = new Table().HideHeaders().Border(TableBorder.None);
            t.AddColumns("k", "v");
            t.AddRow("[dim]title[/]", Markup.Escape(doc.Title));
            t.AddRow("[dim]target[/]", doc.Target is { } d ? DueDates.ToWire(d) : "[dim]—[/]");
            t.AddRow("[dim]state[/]", Markup.Escape(doc.State.ToWire()));
            AnsiConsole.Write(t);
            AnsiConsole.WriteLine();
            if (body.TrimEnd().Length > 0)
                AnsiConsole.Write(new Panel(new Text(body.TrimEnd())).Header("Body").Expand());
            if (backlinks.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]no linked items[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold]Linked items ({backlinks.Count})[/]");
                foreach (var i in backlinks)
                    AnsiConsole.MarkupLine(
                        $"  {Markup.Escape(Out.Short(ctx, i.Id))} [dim]{i.Type.ToWire()}[/] {Markup.Escape(i.Title)}");
            }
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneSet()
    {
        var c = new Command("set", "Edit a milestone's state / target / title in place");
        var refArg = new Argument<string>("milestone") { Description = "<slug> (unambiguous across projects) or <project>/<slug>" };
        var stateOpt = new Option<string?>("--state") { Description = "planned | reached | dropped" };
        var targetOpt = new Option<string?>("--target") { Description = "Target date (YYYY-MM-DD); pass an empty string to clear" };
        var titleOpt = new Option<string?>("--title") { Description = "Milestone title" };
        c.Arguments.Add(refArg);
        c.Options.Add(stateOpt);
        c.Options.Add(targetOpt);
        c.Options.Add(titleOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var (target, err) = Knit.Data.MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, parse.GetValue(refArg)!);
            if (target is null) return Out.Error(err!, ErrorCode.NotFound, json);

            MilestoneState? state = null;
            var stateRaw = parse.GetValue(stateOpt);
            if (stateRaw is not null)
            {
                if (!MilestoneStates.TryParse(stateRaw, out var st))
                    return Out.Error($"invalid --state '{stateRaw}' — expected planned|reached|dropped", ErrorCode.BadArgument, json);
                state = st;
            }

            DateOnly? tgt = null;
            var clearTarget = false;
            var targetRaw = parse.GetValue(targetOpt);
            if (targetRaw is not null)
            {
                if (targetRaw.Length == 0) clearTarget = true;
                else if (!DueDates.TryParse(targetRaw, out var t))
                    return Out.Error($"invalid --target date '{targetRaw}' — expected YYYY-MM-DD", ErrorCode.BadArgument, json);
                else tgt = t;
            }

            var title = parse.GetValue(titleOpt);
            if (title is not null && string.IsNullOrWhiteSpace(title))
                return Out.Error("--title cannot be empty", ErrorCode.BadArgument, json);

            if (state is null && tgt is null && !clearTarget && title is null)
                return Out.Error("specify at least one of --state / --target / --title", ErrorCode.BadArgument, json);

            var edits = new MilestoneEdits(Title: title, State: state, Target: tgt, ClearTarget: clearTarget);
            var result = MilestoneEdit.Apply(ctx.Root, target.ProjectSlug, target.Slug, edits, ctx.Yaml);
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                return Out.Error(e.Message, ErrorCodes.FromKind(e.Kind), json);
            }
            var doc = result.GetValueOrThrow();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                    WriteDoc(w, doc);
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]updated[/] {Markup.Escape(doc.QualifiedSlug)} " +
                $"[dim]state=[/]{Markup.Escape(doc.State.ToWire())} " +
                $"[dim]target=[/]{(doc.Target is { } d ? DueDates.ToWire(d) : "—")}");
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneRename()
    {
        var c = new Command("rename", "Rename a milestone (slug change); re-points item back-links and prose links");
        var refArg = new Argument<string>("milestone") { Description = "<slug> (unambiguous across projects) or <project>/<slug>" };
        var newSlugArg = new Argument<string>("new-slug");
        c.Arguments.Add(refArg);
        c.Arguments.Add(newSlugArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var (target, err) = Knit.Data.MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, parse.GetValue(refArg)!);
            if (target is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var result = Knit.Data.MilestoneMutator.Rename(
                ctx.Store, ctx.Root, target.ProjectSlug, target.Slug, parse.GetValue(newSlugArg)!);
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                return Out.Error(e.Message, ErrorCodes.FromKind(e.Kind), json);
            }
            var r = result.GetValueOrThrow();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("project", r.Project);
                    w.WriteString("old_slug", r.OldSlug);
                    w.WriteString("new_slug", r.NewSlug);
                    w.WriteString("old_path", r.OldPath);
                    w.WriteString("new_path", r.NewPath);
                    WriteStringArray(w, "repointed_item_ids", r.RepointedItemIds);
                    WriteStringArray(w, "files_relinked", r.FilesRelinked);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]renamed[/] {Markup.Escape(r.Project)}/{Markup.Escape(r.OldSlug)} → {Markup.Escape(r.Project)}/{Markup.Escape(r.NewSlug)}");
            if (r.RepointedItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]re-pointed {r.RepointedItemIds.Count} item back-link(s)[/]");
            if (r.FilesRelinked.Count > 0)
                AnsiConsole.MarkupLine($"[dim]repointed links in {r.FilesRelinked.Count} file(s)[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneMove()
    {
        var c = new Command("move", "Move a milestone to another project; re-points item back-links and prose links");
        var refArg = new Argument<string>("milestone") { Description = "<slug> (unambiguous across projects) or <project>/<slug>" };
        var toOpt = new Option<string>("--to") { Required = true, Description = "Destination project slug" };
        var renameOpt = new Option<string?>("--rename") { Description = "New slug in the destination (required when the slug is taken there)" };
        c.Arguments.Add(refArg);
        c.Options.Add(toOpt);
        c.Options.Add(renameOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var (target, err) = Knit.Data.MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, parse.GetValue(refArg)!);
            if (target is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var to = parse.GetValue(toOpt)!.Trim();
            var rename = parse.GetValue(renameOpt)?.Trim();
            if (string.Equals(to, target.ProjectSlug, StringComparison.Ordinal) && string.IsNullOrEmpty(rename))
                return Out.Error($"milestone '{target.QualifiedSlug}' is already in '{to}'", ErrorCode.BadArgument, json);

            var result = Knit.Data.MilestoneMutator.Move(
                ctx.Store, ctx.Root, target.ProjectSlug, target.Slug, to, rename);
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                var msg = e.Kind == Knit.Core.Result.ErrorKind.Conflict && string.IsNullOrEmpty(rename)
                    ? $"{e.Message} — pass --rename <new-slug> to move it under a different slug"
                    : e.Message;
                return Out.Error(msg, ErrorCodes.FromKind(e.Kind), json);
            }
            var r = result.GetValueOrThrow();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("from_project", r.FromProject);
                    w.WriteString("to_project", r.ToProject);
                    w.WriteString("old_slug", r.OldSlug);
                    w.WriteString("new_slug", r.NewSlug);
                    w.WriteString("old_path", r.OldPath);
                    w.WriteString("new_path", r.NewPath);
                    WriteStringArray(w, "repointed_item_ids", r.RepointedItemIds);
                    WriteStringArray(w, "cross_project_item_ids", r.CrossProjectItemIds);
                    WriteStringArray(w, "files_relinked", r.FilesRelinked);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine(
                $"[green]moved[/] {Markup.Escape(r.FromProject)}/{Markup.Escape(r.OldSlug)} → {Markup.Escape(r.ToProject)}/{Markup.Escape(r.NewSlug)}");
            if (r.RepointedItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]re-pointed {r.RepointedItemIds.Count} item back-link(s)[/]");
            if (r.FilesRelinked.Count > 0)
                AnsiConsole.MarkupLine($"[dim]repointed links in {r.FilesRelinked.Count} file(s)[/]");
            if (r.CrossProjectItemIds.Count > 0)
                AnsiConsole.MarkupLine(
                    $"[yellow]{r.CrossProjectItemIds.Count} item(s) now link across a project boundary[/] " +
                    $"[dim](they stay in their own project — `knit verify` lists them)[/]");
            return 0;
        });
        return c;
    }

    private static Command BuildMilestoneDelete()
    {
        var c = new Command("delete", "Delete a milestone; back-links are left dangling unless --scrub-links");
        var projectArg = new Argument<string>("project");
        var slugArg = new Argument<string>("slug");
        var yesOpt = new Option<bool>("--yes") { Aliases = { "-y" }, Description = "Skip the confirmation prompt" };
        var scrubOpt = new Option<bool>("--scrub-links")
        {
            Description = "Also remove Milestone rows pointing here. Default: leave dangling (run `knit verify`)",
        };
        c.Arguments.Add(projectArg);
        c.Arguments.Add(slugArg);
        c.Options.Add(yesOpt);
        c.Options.Add(scrubOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var project = parse.GetValue(projectArg)!;
            var slug = parse.GetValue(slugArg)!;
            var scrub = parse.GetValue(scrubOpt);

            if (!File.Exists(ProjectsLayout.MilestonePath(ctx.Root, project, slug)))
                return Out.Error($"no milestone '{project}/{slug}'", ErrorCode.NotFound, json);

            var backlinks = Knit.Data.MilestoneResolver.LinkedItems(ctx.Store, project, slug);
            if (!parse.GetValue(yesOpt) && !json && !Console.IsErrorRedirected)
            {
                AnsiConsole.MarkupLine($"[yellow]Deleting milestone[/] {Markup.Escape(project)}/{Markup.Escape(slug)}:");
                if (backlinks.Count > 0)
                    AnsiConsole.MarkupLine(scrub
                        ? $"  [dim]{backlinks.Count} item back-link(s) will be scrubbed[/]"
                        : $"  [dim]{backlinks.Count} item(s) link here — left dangling (run `knit verify`)[/]");
                AnsiConsole.MarkupLine("  [dim]recoverable via git until committed[/]");
                if (!AnsiConsole.Confirm("Proceed?", defaultValue: false))
                    return Out.Error("aborted", ErrorCode.BadArgument, json);
            }

            var result = Knit.Data.MilestoneMutator.Delete(ctx.Store, ctx.Root, project, slug, scrub);
            if (result.IsError)
            {
                var e = result.GetErrorOrDefault()!;
                return Out.Error(e.Message, ErrorCodes.FromKind(e.Kind), json);
            }
            var r = result.GetValueOrThrow();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("project", r.Project);
                    w.WriteString("slug", r.Slug);
                    w.WriteString("deleted_path", r.DeletedPath);
                    WriteStringArray(w, "scrubbed_item_ids", r.ScrubbedItemIds);
                    WriteStringArray(w, "dangling_item_ids", r.DanglingItemIds);
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine($"[red]deleted[/] milestone {Markup.Escape(project)}/{Markup.Escape(slug)}");
            if (r.ScrubbedItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]scrubbed links in {r.ScrubbedItemIds.Count} item(s)[/]");
            else if (r.DanglingItemIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]{r.DanglingItemIds.Count} item(s) link here and are left dangling; run `knit verify`[/]");
            return 0;
        });
        return c;
    }

    private static void WriteDoc(Utf8JsonWriter w, MilestoneDoc d)
    {
        w.WriteStartObject();
        WriteDocFields(w, d);
        w.WriteEndObject();
    }

    private static void WriteDocFields(Utf8JsonWriter w, MilestoneDoc d)
    {
        w.WriteString("project", d.ProjectSlug);
        w.WriteString("slug", d.Slug);
        w.WriteString("title", d.Title);
        if (d.Target is { } t) w.WriteString("target", DueDates.ToWire(t));
        w.WriteString("state", d.State.ToWire());
        w.WriteString("path", d.Path);
    }
}
