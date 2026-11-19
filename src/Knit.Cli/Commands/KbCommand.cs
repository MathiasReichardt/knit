using System.CommandLine;
using System.Globalization;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Kb;
using Knit.Core.Refs;
using Knit.Core.Result;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class KbCommand
{
    public static Command Build()
    {
        var cmd = new Command("kb", "Knowledge-base operations");
        cmd.Subcommands.Add(BuildAdd());
        cmd.Subcommands.Add(BuildShow());
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildSet());
        cmd.Subcommands.Add(BuildEdit());
        cmd.Subcommands.Add(BuildLink());
        cmd.Subcommands.Add(BuildUnlink());
        cmd.Subcommands.Add(BuildIndex());
        cmd.Subcommands.Add(BuildRename());
        cmd.Subcommands.Add(BuildSupersede());
        cmd.Subcommands.Add(BuildSplit());
        cmd.Subcommands.Add(BuildDelete());
        return cmd;
    }

    private static Command BuildDelete()
    {
        var c = new Command("delete",
            "Delete a KB artifact and strip incoming kb_refs from items. " +
            "Folder-form entries with sub-docs/attachments need --delete-folder.");
        var refArg = new Argument<string>("ref");
        var forceOpt = new Option<bool>("--force") { Aliases = { "-f" }, Description = "Skip the confirmation prompt (still honours the folder-has-siblings guard)" };
        var deleteFolderOpt = new Option<bool>("--delete-folder") { Description = "For folder-form entries: also remove the entry's folder and its sibling files" };
        c.Arguments.Add(refArg);
        c.Options.Add(forceOpt);
        c.Options.Add(deleteFolderOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var refInput = parse.GetValue(refArg)!;
            var parsed = Ref.Parse(refInput);
            if (parsed.IsError) return RefParseError(ctx, refInput, parsed, json);
            var r = parsed.GetValueOrThrow();
            var force = parse.GetValue(forceOpt);
            var deleteFolder = parse.GetValue(deleteFolderOpt);

            var remover = new Knit.Data.KbRefRewriter(ctx.Store);
            var previewResult = KbDelete.Preview(ctx.Root, r, remover);
            if (previewResult.IsError)
            {
                var e = previewResult.GetErrorOrDefault()!;
                return Out.Error(e.Message, e.Kind == ErrorKind.NotFound ? ErrorCode.NotFound : ErrorCode.BadArgument, json);
            }
            var preview = previewResult.GetValueOrThrow();

            // Folder-has-siblings guard — independent of --force.
            if (preview.IsFolderForm && preview.BlockingFiles.Count > 0 && !deleteFolder)
            {
                var list = string.Join("\n", preview.BlockingFiles.Select(f => "  - " + Path.GetRelativePath(ctx.Root, f)));
                return Out.Error(
                    $"{preview.Ref} is a folder with {preview.BlockingFiles.Count} sibling file(s); pass --delete-folder to remove the whole folder:\n{list}",
                    ErrorCode.BadArgument, json);
            }

            // Confirmation. The CLI refuses a non-interactive delete (no TTY
            // or --json) unless --force is given — matches `knit pick`'s
            // TTY contract.
            if (!force)
            {
                if (json || Console.IsErrorRedirected)
                    return Out.Error("refusing to delete without --force (stderr is not a TTY)", ErrorCode.BadArgument, json);
                var label = $"Delete {preview.Ref}? {preview.ItemsAffected.Count} item(s) will have their kb_refs removed.";
                if (preview.KbEntriesAffected.Count > 0)
                    label += $" {preview.KbEntriesAffected.Count} KB entry(s) will have frontmatter refs cleared.";
                if (deleteFolder && preview.BlockingFiles.Count > 0)
                    label += $" The folder and {preview.BlockingFiles.Count} sibling file(s) go too.";
                if (!AnsiConsole.Confirm(label, defaultValue: false))
                    return Out.Error("aborted", ErrorCode.BadArgument, json);
            }

            var result = KbDelete.Delete(ctx.Root, r, deleteFolder, remover);
            if (result.IsError) return Out.Error(result.GetErrorOrDefault()!.Message, ErrorCode.BadArgument, json);
            var v = result.GetValueOrThrow();
            if (v.BlockingFiles.Count > 0)
                return Out.Error($"{v.Ref} has sibling files; pass --delete-folder", ErrorCode.BadArgument, json);

            var store = new KbStore(ctx.Root, ctx.Yaml);
            new KbIndexer(store).RebuildBucketAndMaster(v.Ref.Bucket);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("ref", v.Ref.ToString());
                    w.WriteString("deleted_path", v.DeletedPath);
                    w.WriteBoolean("deleted_folder", v.DeletedFolder);
                    w.WriteStartArray("items_affected");
                    foreach (var i in v.ItemsAffected) w.WriteStringValue(i);
                    w.WriteEndArray();
                    w.WriteStartArray("kb_entries_affected");
                    foreach (var i in v.KbEntriesAffected) w.WriteStringValue(i);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            var kbDelNote = v.KbEntriesAffected.Count > 0 ? $", {v.KbEntriesAffected.Count} KB entry(s)" : "";
            AnsiConsole.MarkupLine($"[red]deleted[/] {Markup.Escape(v.Ref.ToString())} ({v.ItemsAffected.Count} item(s){kbDelNote} updated)");
            return 0;
        }, exclusive: "kb delete");
        return c;
    }

    private static Command BuildRename()
    {
        var c = new Command("rename", "Rename a KB artifact and rewrite its refs and links (items, KB, designs, project docs)");
        var oldArg = new Argument<string>("old-ref");
        var newSlugArg = new Argument<string>("new-slug");
        c.Arguments.Add(oldArg);
        c.Arguments.Add(newSlugArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var oldInput = parse.GetValue(oldArg)!;
            var parsed = Ref.Parse(oldInput);
            if (parsed.IsError) return RefParseError(ctx, oldInput, parsed, json);
            var oldRef = parsed.GetValueOrThrow();
            // Accept either a bare slug or a fully-qualified ref for the new side.
            var newSlugInput = parse.GetValue(newSlugArg)!;
            var newSlug = newSlugInput.Contains(':')
                ? Ref.Parse(newSlugInput).GetValueOrDefault()?.Slug ?? newSlugInput
                : newSlugInput;
            var rewriter = new Knit.Data.KbRefRewriter(ctx.Store);
            var result = KbRename.Rename(ctx.Root, oldRef, newSlug, rewriter,
                new Knit.Data.ProseRefRewriter(ctx.Store, ctx.Root));
            if (result.IsError) return Out.Error(result.GetErrorOrDefault()!.Message, ErrorCode.BadArgument, json);
            var v = result.GetValueOrThrow();

            var store = new KbStore(ctx.Root, ctx.Yaml);
            new KbIndexer(store).RebuildBucketAndMaster(oldRef.Bucket);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("old_ref", v.OldRef.ToString());
                    w.WriteString("new_ref", v.NewRef.ToString());
                    w.WriteString("old_path", v.OldPath);
                    w.WriteString("new_path", v.NewPath);
                    w.WriteStartArray("items_rewritten");
                    foreach (var i in v.ItemsRewritten) w.WriteStringValue(i);
                    w.WriteEndArray();
                    w.WriteStartArray("kb_entries_affected");
                    foreach (var i in v.KbEntriesAffected) w.WriteStringValue(i);
                    w.WriteEndArray();
                    w.WriteStartArray("files_relinked");
                    foreach (var f in v.FilesRelinked) w.WriteStringValue(f);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            var kbNote = v.KbEntriesAffected.Count > 0 ? $", {v.KbEntriesAffected.Count} KB entry(s)" : "";
            var proseNote = v.FilesRelinked.Count > 0 ? $", {v.FilesRelinked.Count} file(s) with prose refs" : "";
            AnsiConsole.MarkupLine($"[green]renamed[/] {Markup.Escape(v.OldRef.ToString())} → {Markup.Escape(v.NewRef.ToString())} ({v.ItemsRewritten.Count} item(s){kbNote}{proseNote})");
            return 0;
        }, exclusive: "kb rename");
        return c;
    }

    private static Command BuildSupersede()
    {
        var c = new Command("supersede", "Wire up an ADR supersede link both ways");
        var newArg = new Argument<string>("new-id");
        var oldArg = new Argument<string>("old-id");
        c.Arguments.Add(newArg);
        c.Arguments.Add(oldArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var newRefIn = ParseAdrRefArg(parse.GetValue(newArg)!);
            var oldRefIn = ParseAdrRefArg(parse.GetValue(oldArg)!);
            if (newRefIn is null || oldRefIn is null)
                return Out.Error("expected ADR slugs or refs", ErrorCode.BadArgument, json);
            if (newRefIn.Bucket != Bucket.Adr || oldRefIn.Bucket != Bucket.Adr)
                return Out.Error("supersede only works on adr: refs", ErrorCode.BadArgument, json);

            // Resolve to the canonical full-slug Ref so writes don't
            // create a short-slug duplicate.
            var newResolved = RefResolver.Resolve(newRefIn, ctx.Root).GetValueOrThrow();
            var oldResolved = RefResolver.Resolve(oldRefIn, ctx.Root).GetValueOrThrow();
            if (!newResolved.Exists) return Out.Error($"unresolved {newRefIn}", ErrorCode.NotFound, json);
            if (!oldResolved.Exists) return Out.Error($"unresolved {oldRefIn}", ErrorCode.NotFound, json);
            var newRef = newResolved.Ref;
            var oldRef = oldResolved.Ref;

            var newRead = store.ReadPath(newResolved.Path)
                ?? throw new InvalidOperationException($"resolved but unreadable: {newResolved.Path}");
            var oldRead = store.ReadPath(oldResolved.Path)
                ?? throw new InvalidOperationException($"resolved but unreadable: {oldResolved.Path}");

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
            if (json)
            {
                Console.WriteLine($"{{\"new_ref\":\"{newRef}\",\"old_ref\":\"{oldRef}\"}}");
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]superseded[/] {Markup.Escape(oldRef.ToString())} by {Markup.Escape(newRef.ToString())}");
            return 0;
        });
        return c;
    }

    private static Ref? ParseAdrRefArg(string s)
    {
        if (s.Contains(':'))
        {
            var p = Ref.Parse(s);
            return p.IsError ? null : p.GetValueOrThrow();
        }
        return new Ref(Bucket.Adr, s);
    }

    private static Command BuildSplit()
    {
        var c = new Command("split",
            "Promote a single-file KB entry to a folder (slug.md → slug/readme.md). " +
            "After splitting: create sub-docs as <slug>/<sub>.md, link them as " +
            "bucket:slug/<sub>, and rename via 'knit kb rename bucket:slug/<old> " +
            "bucket:slug/<new>'.");
        var refArg = new Argument<string>("ref");
        c.Arguments.Add(refArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var refInput = parse.GetValue(refArg)!;
            var parsed = Ref.Parse(refInput);
            if (parsed.IsError) return RefParseError(ctx, refInput, parsed, json);
            var r = parsed.GetValueOrThrow();

            var result = KbSplit.Split(
                ctx.Root, r, new KbStore(ctx.Root, ctx.Yaml),
                new Knit.Data.KbRefRewriter(ctx.Store),
                new Knit.Data.ProseRefRewriter(ctx.Store, ctx.Root));
            if (result.IsError)
            {
                var err = result.GetErrorOrDefault()!;
                return Out.Error(err.Message,
                    err.Message.StartsWith("unresolved", StringComparison.Ordinal) ? ErrorCode.NotFound : ErrorCode.BadArgument,
                    json);
            }
            var v = result.GetValueOrThrow();

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("old_path", v.OldPath);
                    w.WriteString("new_path", v.NewPath);
                    w.WriteString("ref", v.Ref.ToString());
                    if (v.NoOp) w.WriteBoolean("noop", true);
                    w.WriteStartArray("items_rewritten");
                    foreach (var i in v.ItemsRewritten) w.WriteStringValue(i);
                    w.WriteEndArray();
                    w.WriteStartArray("files_relinked");
                    foreach (var f in v.FilesRelinked) w.WriteStringValue(f);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            if (v.NoOp)
            {
                AnsiConsole.MarkupLine($"[yellow]already split[/] {Markup.Escape(v.Ref.ToString())}");
                return 0;
            }
            var repaired = v.ItemsRewritten.Count + v.FilesRelinked.Count;
            var note = repaired > 0 ? $" ({repaired} reference(s) repointed)" : "";
            AnsiConsole.MarkupLine($"[green]split[/] {Markup.Escape(v.Ref.ToString())}: {Markup.Escape(v.NewPath)}{note}");
            return 0;
        }, exclusive: "kb split");
        return c;
    }

    private static Command BuildIndex()
    {
        var c = new Command("index", "Regenerate all KB index files");
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var written = new KbIndexer(store).RebuildAll();
            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteStartArray("files_written");
                    foreach (var p in written) w.WriteStringValue(p);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]regenerated[/] {written.Count} index file(s)");
            return 0;
        });
        return c;
    }

    // ---- add ------------------------------------------------------------

    private static Command BuildAdd()
    {
        var add = new Command("add", "Create a new KB artifact");

        add.Subcommands.Add(BuildAddAdr());
        add.Subcommands.Add(BuildAddDoc());
        add.Subcommands.Add(BuildAddFeature());
        add.Subcommands.Add(BuildAddLog());
        add.Subcommands.Add(BuildAddIdea());
        add.Subcommands.Add(BuildAddSource());
        return add;
    }

    private static Command BuildAddAdr()
    {
        var c = new Command("adr", "Create a new ADR");
        var titleArg = new Argument<string>("title");
        var summaryOpt = new Option<string?>("--summary");
        var featureOpt = new Option<string?>("--feature");
        var originOpt = new Option<string[]>("--origin")
        {
            Description = "Origin ref the decision emerged from: design:<slug> or item:<id> (repeatable). Must resolve.",
        };
        c.Arguments.Add(titleArg);
        c.Options.Add(summaryOpt);
        c.Options.Add(featureOpt);
        c.Options.Add(originOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var title = parse.GetValue(titleArg)!;
            var summary = parse.GetValue(summaryOpt);
            var feature = parse.GetValue(featureOpt);
            var (origin, originErr) = OriginRefs.Normalize(ctx.Root, parse.GetValue(originOpt) ?? []);
            if (originErr is not null) return Out.Error(originErr, ErrorCode.NotFound, json);
            var nnnn = NextAdrNumber(store);
            var mint = Slug.Describe(title);
            var slug = $"{nnnn:D4}-{mint.Value}";
            // Nygard / adr-tools convention: frontmatter title mirrors
            // the body h1 `# NNNN. Title`. Keeps `knit verify`'s title-
            // drift check happy and lets the number survive copy/paste
            // out of the file.
            var prefixedTitle = $"{nnnn:D4}. {title}";
            var fm = KbFrontmatterMap.ToMap(new AdrFrontmatter(
                Id: $"{nnnn:D4}",
                Title: prefixedTitle,
                Date: DateOnly.FromDateTime(DateTime.UtcNow),
                Status: "proposed",
                Supersedes: [],
                SupersededBy: null,
                Feature: string.IsNullOrEmpty(feature) ? [] : [feature],
                Origin: origin,
                Tags: [],
                Summary: summary));
            var body = RenderTemplate(Bucket.Adr, new Dictionary<string, string> { ["title"] = title, ["number"] = $"{nnnn:D4}" });
            var r = new Ref(Bucket.Adr, slug);
            var path = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm, mint, title);
        });
        return c;
    }

    private static Command BuildAddDoc()
    {
        var c = new Command("doc", "Create a new living doc");
        var nameArg = new Argument<string>("name");
        var titleOpt = new Option<string?>("--title");
        var summaryOpt = new Option<string?>("--summary");
        var featureOpt = new Option<string?>("--feature");
        c.Arguments.Add(nameArg);
        c.Options.Add(titleOpt);
        c.Options.Add(summaryOpt);
        c.Options.Add(featureOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var name = parse.GetValue(nameArg)!;
            var norm = Knit.Core.Kb.SubdocSlug.NormalizeDocPath(store, name);
            if (norm.Error is not null)
                return Out.Error(norm.Error, ErrorCode.BadArgument, json);
            var slug = norm.Slug!;
            var title = parse.GetValue(titleOpt) ?? name;
            var r = new Ref(Bucket.Doc, slug);
            if (File.Exists(store.ResolveWritePath(r)))
                return Out.Error($"doc:{slug} already exists", ErrorCode.BadArgument, json);
            var fm = KbFrontmatterMap.ToMap(new DocFrontmatter(title, parse.GetValue(featureOpt), [], parse.GetValue(summaryOpt)));
            var body = RenderTemplate(Bucket.Doc, new Dictionary<string, string> { ["title"] = title });
            var path = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm, new SlugResult(slug, norm.Mint), name);
        });
        return c;
    }

    private static Command BuildAddFeature()
    {
        var c = new Command("feature", "Create a new feature doc");
        var slugArg = new Argument<string>("slug");
        var titleOpt = new Option<string?>("--title");
        var summaryOpt = new Option<string?>("--summary");
        c.Arguments.Add(slugArg);
        c.Options.Add(titleOpt);
        c.Options.Add(summaryOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var rawSlug = parse.GetValue(slugArg)!;
            var norm = Knit.Core.Kb.SubdocSlug.NormalizeFeaturePath(store, rawSlug);
            if (norm.Error is not null)
                return Out.Error(norm.Error, ErrorCode.BadArgument, json);
            var slug = norm.Slug!;
            // Default the title to the raw typed slug (nicer than the
            // lowercased/dashed result) when no --title was given.
            var title = parse.GetValue(titleOpt) ?? rawSlug;
            var r = new Ref(Bucket.Feature, slug);
            if (File.Exists(store.ResolveWritePath(r)))
                return Out.Error($"feature:{slug} already exists", ErrorCode.BadArgument, json);
            var fm = KbFrontmatterMap.ToMap(new FeatureFrontmatter(title, slug, [], parse.GetValue(summaryOpt)));
            var body = RenderTemplate(Bucket.Feature, new Dictionary<string, string> { ["title"] = title });
            var path = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm, new SlugResult(slug, norm.Mint), rawSlug);
        });
        return c;
    }

    private static Command BuildAddLog()
    {
        var c = new Command("log", "Create a new dated log entry");
        var titleArg = new Argument<string>("title");
        var summaryOpt = new Option<string?>("--summary");
        var dateOpt = new Option<string?>("--date");
        c.Arguments.Add(titleArg);
        c.Options.Add(summaryOpt);
        c.Options.Add(dateOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var title = parse.GetValue(titleArg)!;
            var dateStr = parse.GetValue(dateOpt);
            var date = !string.IsNullOrEmpty(dateStr)
                && DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d : DateOnly.FromDateTime(DateTime.UtcNow);
            var mint = Slug.Describe(title);
            var slug = $"{date:yyyy-MM-dd}-{mint.Value}";
            var r = new Ref(Bucket.Log, slug);
            var fm = KbFrontmatterMap.ToMap(new LogFrontmatter(title, date, [], [], parse.GetValue(summaryOpt)));
            var body = RenderTemplate(Bucket.Log, new Dictionary<string, string> { ["title"] = title });
            var path = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm, mint, title);
        });
        return c;
    }

    private static Command BuildAddIdea()
    {
        var c = new Command("idea", "Add an idea entry");
        var textArg = new Argument<string>("text");
        var slugOpt = new Option<string?>("--slug");
        var featureOpt = new Option<string?>("--feature");
        c.Arguments.Add(textArg);
        c.Options.Add(slugOpt);
        c.Options.Add(featureOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var text = parse.GetValue(textArg)!;
            var rawSlug = parse.GetValue(slugOpt);
            // Slugify whether typed or derived from the text, so an idea slug
            // is always a filesystem-safe, lowercase token.
            var mintInput = string.IsNullOrWhiteSpace(rawSlug) ? text : rawSlug;
            var mint = Slug.Describe(mintInput);
            var slug = mint.Value;
            var r = new Ref(Bucket.Idea, slug);
            var fm = KbFrontmatterMap.ToMap(new IdeaFrontmatter(text, parse.GetValue(featureOpt), [], null));
            var body = RenderTemplate(Bucket.Idea, new Dictionary<string, string> { ["title"] = text, ["body"] = "" });
            var path = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm, mint, mintInput);
        });
        return c;
    }

    private static Command BuildAddSource()
    {
        var c = new Command("source", "Add an external source entry");
        var idArg = new Argument<string>("id");
        var titleOpt = new Option<string?>("--title");
        var kindOpt = new Option<string?>("--kind") { Description = "link | file" };
        var urlOpt = new Option<string?>("--url");
        var pathOpt = new Option<string?>("--path");
        var mountOpt = new Option<string?>("--mount")
        {
            Description = "Name of a config mount --path is relative to (see `knit mount`); default is the repo root",
        };
        var summaryOpt = new Option<string?>("--summary");
        c.Arguments.Add(idArg);
        c.Options.Add(titleOpt);
        c.Options.Add(kindOpt);
        c.Options.Add(urlOpt);
        c.Options.Add(pathOpt);
        c.Options.Add(mountOpt);
        c.Options.Add(summaryOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var rawId = parse.GetValue(idArg)!;
            var title = parse.GetValue(titleOpt) ?? rawId;
            // The id doubles as a filesystem-safe slug, so it may nest under a
            // folder-form source (`vendor/api-spec`); the stored `id:` is the
            // leaf segment. The raw input still seeds the default title.
            var norm = SubdocSlug.Normalize(store, Bucket.Source, rawId, title);
            if (norm.Error is not null) return Out.Error(norm.Error, ErrorCode.BadArgument, json);
            var slug = norm.Slug!;
            var id = KbIdentity.ExpectedId(Bucket.Source, slug)!;
            var kind = parse.GetValue(kindOpt) ?? (parse.GetValue(pathOpt) is not null ? "file" : "link");
            if (kind is not "link" and not "file")
                return Out.Error("source kind must be 'link' or 'file'", ErrorCode.BadArgument, json);
            var url = parse.GetValue(urlOpt);
            var path = parse.GetValue(pathOpt);
            if (kind == "link" && string.IsNullOrEmpty(url))
                return Out.Error("link sources require --url", ErrorCode.BadArgument, json);
            if (kind == "file" && string.IsNullOrEmpty(path))
                return Out.Error("file sources require --path", ErrorCode.BadArgument, json);
            var mount = parse.GetValue(mountOpt);
            if (!string.IsNullOrEmpty(mount))
            {
                if (kind != "file")
                    return Out.Error("--mount applies to file sources only", ErrorCode.BadArgument, json);
                // An unresolved mount is a config problem, not a bad entry, so
                // only the name is rejected here — resolution is reported by
                // `knit verify`.
                if (!MountResolver.IsValidName(mount))
                    return Out.Error($"invalid mount name '{mount}' — {MountResolver.NameRule}", ErrorCode.BadArgument, json);
                if (ctx.Config.Mounts is null || !ctx.Config.Mounts.ContainsKey(mount))
                    return Out.Error($"unknown mount '{mount}' — declare it with `knit mount add {mount} <path>`", ErrorCode.BadArgument, json);
            }
            if (kind == "file" && MountResolver.IsLiteralPath(ctx.Root, path!))
            {
                var rebased = RebaseOntoMount(ctx, path!, mount, json);
                if (rebased.Error is not null) return Out.Error(rebased.Error, rebased.Code, json);
                path = rebased.Path;
                mount = rebased.Mount;
            }
            var r = new Ref(Bucket.Source, slug);
            var fm = KbFrontmatterMap.ToMap(new SourceFrontmatter(id, title, kind, url, path, [], parse.GetValue(summaryOpt), mount));
            var body = RenderTemplate(Bucket.Source, new Dictionary<string, string> { ["title"] = title });
            var written = store.Write(r, fm, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, written, fm, new SlugResult(slug, norm.Mint), rawId);
        });
        return c;
    }

    /// <summary>
    /// A literal filesystem path (rooted, or climbing out of the repo with
    /// <c>..</c>) is turned into the stored mount-relative form: the caller
    /// passes the path they can actually see, and the mount it rides on is
    /// found rather than spelled out. A plain repo-relative path never
    /// reaches here and is stored verbatim.
    /// </summary>
    private static (string? Path, string? Mount, string? Error, ErrorCode Code) RebaseOntoMount(
        CliContext ctx, string declared, string? mount, bool json)
    {
        var rebase = MountResolver.RebaseLiteral(ctx.Root, ctx.Config, declared, mount);
        if (rebase.Error is not null)
            return (null, null, rebase.Error, rebase.Ambiguous ? ErrorCode.Conflict : ErrorCode.BadArgument);
        // Said on stderr: stdout stays capture-clean, and the entry no longer
        // holds the path the caller typed, so silence would be surprising.
        if (!json && string.IsNullOrEmpty(mount))
            Console.Error.WriteLine($"note: stored as mount '{rebase.Mount}' + '{rebase.Path}'");
        return (rebase.Path, rebase.Mount, null, default);
    }

    // ---- show -----------------------------------------------------------

    private static Command BuildShow()
    {
        var c = new Command("show", "Show a KB artifact");
        var refArg = new Argument<string>("ref");
        var pathOnlyOpt = new Option<bool>("--path-only");
        var sourcePathOpt = new Option<bool>("--source-path")
        {
            Description = "Print the absolute path a file source points at (mount resolved), for $(...) substitution. "
                + "--path-only prints the entry file instead.",
        };
        c.Arguments.Add(refArg);
        c.Options.Add(pathOnlyOpt);
        c.Options.Add(sourcePathOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var pathOnly = parse.GetValue(pathOnlyOpt);
            var sourcePath = parse.GetValue(sourcePathOpt);
            if (pathOnly && sourcePath)
                return Out.Error("--path-only and --source-path print different paths; pass one", ErrorCode.BadArgument, json);
            var refStr = parse.GetValue(refArg)!;
            var parsed = Ref.Parse(refStr);
            if (parsed.IsError) return RefParseError(ctx, refStr, parsed, json);
            var r = parsed.GetValueOrThrow();
            var resolved = RefResolver.Resolve(r, ctx.Root).GetValueOrThrow();
            if (!resolved.Exists)
                return Out.Error(
                    $"unresolved ref {r}: no file at {resolved.Path}. " +
                    KbResolveHint.For(new KbStore(ctx.Root, ctx.Yaml), r),
                    ErrorCode.NotFound, json);
            if (pathOnly)
            {
                Console.WriteLine(resolved.Path);
                return 0;
            }
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var read = store.ReadPath(resolved.Path);
            if (read is null) return Out.Error("unable to read artifact", ErrorCode.Internal, json);
            var (fm, body) = read.Value;
            var target = SourceTargetOf(ctx, r, fm);
            if (sourcePath)
            {
                if (r.Bucket != Bucket.Source)
                    return Out.Error("--source-path applies to source entries only", ErrorCode.BadArgument, json);
                if (target is null)
                    return Out.Error(
                        Str(fm, "source_kind") == "link"
                            ? $"source is kind 'link' — it has no file path (url: {Str(fm, "url")})"
                            : "source has no path",
                        ErrorCode.BadArgument, json);
                if (target.Reason is not null) return Out.Error(target.Reason, ErrorCode.NotFound, json);
                Console.WriteLine(target.FullPath);
                return 0;
            }
            if (json)
            {
                KbJson.WriteArtifact(r, resolved.Path, fm, body, target: target);
                return 0;
            }
            RenderHuman(r, resolved.Path, fm, body, target);
            return 0;
        });
        return c;
    }

    /// <summary>
    /// Where a <c>source</c> entry's <c>path</c> lands, or null when the entry
    /// has none (every other bucket, and a <c>link</c> source).
    /// </summary>
    private static MountResolver.SourceTarget? SourceTargetOf(
        CliContext ctx, Ref r, IReadOnlyDictionary<string, object?> fm)
    {
        if (r.Bucket != Bucket.Source) return null;
        var path = Str(fm, "path");
        return string.IsNullOrEmpty(path)
            ? null
            : MountResolver.ResolveSource(ctx.Root, ctx.Config, Str(fm, "mount"), path);
    }

    private static string? Str(IReadOnlyDictionary<string, object?> fm, string key)
        => fm.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static void RenderHuman(
        Ref r, string path, IReadOnlyDictionary<string, object?> fm, string body,
        MountResolver.SourceTarget? target = null)
    {
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(r.ToString())}[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(path)}[/]");
        AnsiConsole.WriteLine();
        var t = new Table().HideHeaders().Border(TableBorder.None);
        t.AddColumns("k", "v");
        foreach (var (k, v) in fm)
        {
            var rendered = v switch
            {
                null => "",
                List<object?> list => string.Join(", ", list.Select(x => x?.ToString() ?? "")),
                _ => v.ToString() ?? "",
            };
            t.AddRow($"[dim]{Markup.Escape(k)}[/]", Markup.Escape(rendered));
        }
        AnsiConsole.Write(t);
        // The stored path is mount-relative, so on its own it resolves against
        // nothing outside knit — say where it actually lands.
        if (target is not null)
        {
            AnsiConsole.MarkupLine(target.Reason is null
                ? $"[dim]resolved[/]  {Markup.Escape(target.FullPath)}"
                : $"[dim]resolved[/]  [yellow]{Markup.Escape(target.Reason)}[/]");
        }
        if (!string.IsNullOrWhiteSpace(body))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(new Text(body.TrimEnd())).Header("Body").Expand());
        }
    }

    // ---- list -----------------------------------------------------------

    private static Command BuildList()
    {
        var c = new Command("list", "List KB artifacts (with optional filters)");
        var bucketOpt = new Option<string?>("--bucket");
        var tagOpt = new Option<string?>("--tag");
        var featureOpt = new Option<string?>("--feature");
        var statusOpt = new Option<string?>("--status");
        c.Options.Add(bucketOpt);
        c.Options.Add(tagOpt);
        c.Options.Add(featureOpt);
        c.Options.Add(statusOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var store = new KbStore(ctx.Root, ctx.Yaml);

            IEnumerable<Bucket> buckets = BucketStrings.All;
            var bucketStr = parse.GetValue(bucketOpt);
            if (!string.IsNullOrEmpty(bucketStr))
            {
                if (!BucketStrings.TryParse(bucketStr, out var b))
                    return Out.Error($"unknown bucket '{bucketStr}'", ErrorCode.BadArgument, json);
                if (b == Bucket.Item)
                    return Out.Error("bucket 'item' is not a KB bucket; use 'knit list'", ErrorCode.BadArgument, json);
                buckets = [b];
            }
            else
            {
                buckets = BucketStrings.All.Where(b => b != Bucket.Item);
            }

            var tag = parse.GetValue(tagOpt);
            var feature = parse.GetValue(featureOpt);
            var status = parse.GetValue(statusOpt);

            var items = new List<(Ref, string, IReadOnlyDictionary<string, object?>)>();
            foreach (var b in buckets)
            {
                foreach (var (r, path) in store.ListBucket(b))
                {
                    var read = store.ReadPath(path);
                    if (read is null) continue;
                    var fm = read.Value.Frontmatter;
                    if (!MatchFilter(fm, tag, feature, status, b)) continue;
                    items.Add((r, path, fm));
                }
            }

            if (json)
            {
                KbJson.WriteList(items);
                return 0;
            }
            RenderListHuman(items);
            return 0;
        });
        return c;
    }

    private static bool MatchFilter(IReadOnlyDictionary<string, object?> fm, string? tag, string? feature, string? status, Bucket bucket)
    {
        if (!string.IsNullOrEmpty(tag))
        {
            var tags = fm.TryGetValue("tags", out var t) && t is List<object?> list
                ? list.Select(x => x?.ToString() ?? "") : [];
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrEmpty(feature))
        {
            if (!fm.TryGetValue("feature", out var f)) return false;
            var matches = f switch
            {
                string s => string.Equals(s, feature, StringComparison.OrdinalIgnoreCase),
                List<object?> list => list.Any(x => string.Equals(x?.ToString(), feature, StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
            if (!matches) return false;
        }
        if (!string.IsNullOrEmpty(status))
        {
            if (bucket != Bucket.Adr) return false;
            if (!fm.TryGetValue("status", out var s) || !string.Equals(s?.ToString(), status, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void RenderListHuman(IReadOnlyList<(Ref Ref, string Path, IReadOnlyDictionary<string, object?> Frontmatter)> items)
    {
        if (items.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no KB artifacts[/]");
            return;
        }
        var grouped = items.GroupBy(i => i.Ref.Bucket).OrderBy(g => g.Key.ToWire(), StringComparer.Ordinal);
        foreach (var g in grouped)
        {
            AnsiConsole.Write(new Rule($"[bold]{g.Key.ToWire()}s[/]").LeftJustified());
            var t = new Table().Border(TableBorder.Rounded);
            // The mount is what tells a reader a source lives outside the repo;
            // the column only appears where one is in play, and an unmounted
            // row stays blank rather than claiming a default.
            var withMount = g.Key == Bucket.Source && g.Any(i => !string.IsNullOrEmpty(Str(i.Frontmatter, "mount")));
            if (withMount) t.AddColumns("ref", "mount", "title", "summary");
            else t.AddColumns("ref", "title", "summary");
            foreach (var (r, _, fm) in g)
            {
                var title = fm.TryGetValue("title", out var tv) ? tv?.ToString() ?? "" : "";
                var summary = fm.TryGetValue("summary", out var sv) ? sv?.ToString() ?? "" : "";
                var cells = new List<IRenderable> { new Markup(Markup.Escape(r.ToString())) };
                if (withMount) cells.Add(new Markup($"[dim]{Markup.Escape(Str(fm, "mount") ?? "")}[/]"));
                cells.Add(Out.Prose(title));
                cells.Add(Out.Prose(summary));
                t.AddRow(cells);
            }
            AnsiConsole.Write(t);
        }
    }

    // ---- edit -----------------------------------------------------------

    private static Command BuildEdit()
    {
        var c = new Command("edit", "Write a body section on a KB artifact (non-interactive; for scripts and agents)");
        var refArg = new Argument<string>("ref");
        var setSectionOpt = new Option<string?>("--set-section") { Description = "h2 section name to replace or append. Forbidden: Links, Comments." };
        var valueOpt = new Option<string?>("--value") { Description = "Body text for the section (required). Empty string clears the content but keeps the heading." };
        var deleteSectionOpt = new Option<string[]>("--delete-section")
        {
            Description = "h2 section to remove, heading and all. Repeatable. Use this for template headings that don't apply; --set-section --value \"\" is the other operation (keeps the heading). Forbidden: Links, Comments.",
            AllowMultipleArgumentsPerToken = false,
        };
        c.Arguments.Add(refArg);
        c.Options.Add(setSectionOpt);
        c.Options.Add(valueOpt);
        c.Options.Add(deleteSectionOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var section = parse.GetValue(setSectionOpt);
            var deleteSections = parse.GetValue(deleteSectionOpt) ?? [];
            if (deleteSections.Length > 0 && !string.IsNullOrEmpty(section))
                return Out.Error("--delete-section and --set-section are separate operations; run them one at a time", ErrorCode.BadArgument, json);
            if (deleteSections.Length == 0 && string.IsNullOrEmpty(section))
                return Out.Error("--set-section or --delete-section is required", ErrorCode.BadArgument, json);
            var value = parse.GetValue(valueOpt);
            if (deleteSections.Length == 0 && value is null)
                return Out.Error("--value is required with --set-section", ErrorCode.BadArgument, json);

            var refInput = parse.GetValue(refArg)!;
            var parsed = Ref.Parse(refInput);
            if (parsed.IsError) return RefParseError(ctx, refInput, parsed, json);
            var r = parsed.GetValueOrThrow();
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var read = store.Read(r);
            if (read is null)
                return Out.Error($"unresolved ref {r}. " + KbResolveHint.For(store, r), ErrorCode.NotFound, json);
            var (fm, body) = read.Value;

            string newBody;
            if (deleteSections.Length > 0)
            {
                newBody = body;
                // Validate every name before writing: a typo in the third one
                // must not leave the first two deleted.
                foreach (var name in deleteSections)
                {
                    bool removed;
                    try { removed = BodySections.TryDeleteSection(newBody, name, out newBody); }
                    catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
                    if (!removed)
                    {
                        var present = BodySections.Headings(body);
                        var have = present.Count == 0 ? "none" : string.Join(", ", present);
                        return Out.Error($"no section '{name}' on {r} (has: {have})", ErrorCode.NotFound, json);
                    }
                }
            }
            else
            {
                try { newBody = BodySections.SetSection(body, section!, value!); }
                catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
            }

            var path = store.Write(r, fm, newBody);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm);
        });
        return c;
    }

    // ---- link / unlink ---------------------------------------------------

    private static Command BuildLink()
    {
        var c = new Command("link",
            "Add a Branch / Repo / PR / Commit row to a KB artifact — the same external relations items carry");
        var refArg = new Argument<string>("ref");
        var branchOpt = new Option<string?>("--branch") { Description = "Git branch name (stored as written; knit never checks git)" };
        var repoOpt = new Option<string?>("--repo") { Description = "A declared mount name, a directory, or a git URL" };
        var prOpt = new Option<string?>("--pr") { Description = "Full pull/merge request URL, displayed as org/repo#42" };
        var commitOpt = new Option<string?>("--commit") { Description = "A SHA (stored as written) or the full commit URL (displayed as org/repo@abc1234)" };
        var noteOpt = new Option<string?>("--note") { Description = "Notes column; on an existing row it replaces the note (\"\" clears it, omitted keeps it)" };
        c.Arguments.Add(refArg);
        c.Options.Add(branchOpt);
        c.Options.Add(repoOpt);
        c.Options.Add(prOpt);
        c.Options.Add(commitOpt);
        c.Options.Add(noteOpt);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var branch = parse.GetValue(branchOpt);
            var repo = parse.GetValue(repoOpt);
            var pr = parse.GetValue(prOpt);
            var commit = parse.GetValue(commitOpt);
            var given = new[] { branch, repo, pr, commit }.Count(v => !string.IsNullOrEmpty(v));
            if (given == 0) return Out.Error("specify --branch, --repo, --pr, or --commit", ErrorCode.BadArgument, json);
            if (given > 1) return Out.Error("specify exactly one link kind", ErrorCode.BadArgument, json);
            var (rel, raw) = !string.IsNullOrEmpty(branch) ? (LinkRel.Branch, branch)
                : !string.IsNullOrEmpty(repo) ? (LinkRel.Repo, repo)
                : !string.IsNullOrEmpty(pr) ? (LinkRel.Pr, pr)
                : (LinkRel.Commit, commit!);

            if (!Resolve(ctx, parse.GetValue(refArg)!, json, out var store, out var r, out var fm, out var body, out var err))
                return err;
            if (!ExternalRefs.TryResolve(rel, raw, ctx.Root, ctx.Config, out var target, out var extErr))
                return Out.Error(extErr!, ErrorCode.BadArgument, json);

            var existing = LinksTable.Parse(body);
            var kept = existing.FirstOrDefault(row => row.Rel == rel && string.Equals(row.TargetText, target.Text, StringComparison.Ordinal))?.Notes;
            var rows = existing
                .Where(row => !(row.Rel == rel && string.Equals(row.TargetText, target.Text, StringComparison.Ordinal)))
                .ToList();
            rows.Add(new LinkRow(rel, target.Text, target.Path, ItemUtils.Note(parse.GetValue(noteOpt), kept)));

            var path = store.Write(r, fm, LinksTable.Replace(body, rows));
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm);
        });
        return c;
    }

    private static Command BuildUnlink()
    {
        var c = new Command("unlink", "Remove a Branch / Repo / PR / Commit row from a KB artifact");
        var refArg = new Argument<string>("ref");
        var targetArg = new Argument<string>("target") { Description = "The row's target as displayed, or a PR's / commit's full URL" };
        c.Arguments.Add(refArg);
        c.Arguments.Add(targetArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            if (!Resolve(ctx, parse.GetValue(refArg)!, json, out var store, out var r, out var fm, out var body, out var err))
                return err;

            var targetStr = parse.GetValue(targetArg)!;
            var rows = LinksTable.Parse(body).ToList();
            var kept = rows.Where(row => !(ExternalRefs.IsExternal(row.Rel)
                && (string.Equals(row.TargetText, targetStr, StringComparison.Ordinal)
                    || string.Equals(row.TargetPath, targetStr, StringComparison.Ordinal)))).ToList();
            if (kept.Count == rows.Count)
                return Out.Error($"no branch / repo / PR / commit row on {r} targets '{targetStr}'", ErrorCode.NotFound, json);

            var path = store.Write(r, fm, LinksTable.Replace(body, kept));
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, fm);
        });
        return c;
    }

    /// <summary>Shared ref → (store, ref, frontmatter, body) resolution for the link verbs.</summary>
    private static bool Resolve(
        CliContext ctx, string refStr, bool json,
        out KbStore store, out Ref r,
        out IReadOnlyDictionary<string, object?> fm, out string body, out int error)
    {
        store = new KbStore(ctx.Root, ctx.Yaml);
        r = new Ref(Bucket.Doc, "");
        fm = new Dictionary<string, object?>();
        body = string.Empty;
        var parsed = Ref.Parse(refStr);
        if (parsed.IsError)
        {
            error = RefParseError(ctx, refStr, parsed, json);
            return false;
        }
        r = parsed.GetValueOrThrow();
        var read = store.Read(r);
        if (read is null)
        {
            error = Out.Error($"unresolved ref {r}. " + KbResolveHint.For(store, r), ErrorCode.NotFound, json);
            return false;
        }
        (fm, body) = read.Value;
        error = 0;
        return true;
    }

    // ---- set ------------------------------------------------------------

    private static Command BuildSet()
    {
        var c = new Command("set", "Edit a frontmatter field on a KB artifact");
        var refArg = new Argument<string>("ref");
        var keyArg = new Argument<string>("key");
        var valueArg = new Argument<string>("value");
        c.Arguments.Add(refArg);
        c.Arguments.Add(keyArg);
        c.Arguments.Add(valueArg);
        c.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var refStr = parse.GetValue(refArg)!;
            var parsed = Ref.Parse(refStr);
            if (parsed.IsError) return RefParseError(ctx, refStr, parsed, json);
            var r = parsed.GetValueOrThrow();
            var store = new KbStore(ctx.Root, ctx.Yaml);
            var read = store.Read(r);
            if (read is null)
                return Out.Error($"unresolved ref {r}. " + KbResolveHint.For(store, r), ErrorCode.NotFound, json);
            var (fm, body) = read.Value;
            var mutable = new Dictionary<string, object?>(fm, StringComparer.Ordinal);
            var key = parse.GetValue(keyArg)!;
            var value = parse.GetValue(valueArg)!;
            if (!IsKnownField(r.Bucket, key))
                return Out.Error($"unknown field '{key}' for bucket '{r.Bucket.ToWire()}'", ErrorCode.BadArgument, json);
            if (key == "tags")
            {
                // tags is list-typed; a scalar string here would be malformed
                // for AsStringList / FTS / the SPA filter. Comma-split + normalize.
                var normalized = Tags.Normalize(value.Split(','));
                if (normalized.Count == 0) mutable.Remove("tags");
                else mutable["tags"] = normalized.Select(s => (object?)s).ToList();
            }
            else if (key == "path" && r.Bucket == Bucket.Source
                     && MountResolver.IsLiteralPath(ctx.Root, value))
            {
                // Same rebase as `kb add source`, which is also how an entry
                // written before mounts existed is adopted into one:
                // set the real path, get the split form.
                // No mount passed even when the entry has one: `set path` is a
                // repoint, so the covering mount is found afresh.
                var rebased = RebaseOntoMount(ctx, value, null, json);
                if (rebased.Error is not null) return Out.Error(rebased.Error, rebased.Code, json);
                mutable["path"] = rebased.Path;
                mutable["mount"] = rebased.Mount;
            }
            else
            {
                mutable[key] = value;
            }
            var path = store.Write(r, mutable, body);
            new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
            return EmitArtifact(json, r, path, mutable);
        });
        return c;
    }

    private static bool IsKnownField(Bucket bucket, string key) => bucket switch
    {
        Bucket.Adr => key is "id" or "title" or "date" or "status" or "supersedes" or "superseded_by" or "feature" or "origin" or "tags" or "summary",
        Bucket.Doc => key is "title" or "feature" or "tags" or "summary",
        Bucket.Feature => key is "title" or "feature" or "tags" or "summary",
        Bucket.Log => key is "title" or "date" or "feature" or "tags" or "summary",
        Bucket.Idea => key is "title" or "feature" or "tags" or "summary",
        Bucket.Source => key is "id" or "title" or "source_kind" or "url" or "path" or "mount" or "tags" or "summary",
        _ => false,
    };

    // ---- shared helpers -------------------------------------------------

    internal static int RefParseError(CliContext ctx, string input, OperationResult<Ref> parsed, bool json)
    {
        var msg = !string.IsNullOrWhiteSpace(input) && !input.Contains(':')
            ? KbResolveHint.ForBareSlug(new KbStore(ctx.Root, ctx.Yaml), input)
            : parsed.GetErrorOrDefault()!.Message;
        return Out.Error(msg, ErrorCode.BadArgument, json);
    }

    // Validate ADR --origin tokens: each must parse as a design:/item: ref
    // and resolve to an existing target, else the ADR would record an origin
    // pointing at nothing. Returns canonical ref strings.
    private static int EmitArtifact(
        bool json, Ref r, string path, IReadOnlyDictionary<string, object?> fm,
        SlugResult? mint = null, string? mintInput = null)
    {
        if (mint is { } m && mintInput is not null) Out.MintNote(mintInput, m);
        if (json)
        {
            KbJson.WriteArtifact(r, path, fm, body: null, mint: mint, mintInput: mintInput);
            return 0;
        }
        AnsiConsole.MarkupLine($"[green]wrote[/] {Markup.Escape(r.ToString())} [dim]{Markup.Escape(path)}[/]");
        return 0;
    }

    private static int NextAdrNumber(KbStore store)
    {
        var max = 0;
        foreach (var (r, _) in store.ListBucket(Bucket.Adr))
        {
            var slug = r.Slug;
            var dash = slug.IndexOf('-');
            var prefix = dash > 0 ? slug[..dash] : slug;
            if (int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
        }
        return max + 1;
    }

    private static string RenderTemplate(Bucket bucket, IReadOnlyDictionary<string, string> values)
    {
        var template = KbTemplates.Load(bucket);
        return Knit.Core.Templates.TemplateEngine.Render(template, values);
    }
}
