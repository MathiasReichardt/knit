using System.Text.Json;
using Knit.Cli.Output;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class MoveVerifyRelinkTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Move_rewrites_incoming_item_links_in_kb()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "rename me").ExitCode);
        var itemFile = ItemFixtures.FirstItemFile(root);
        var id = ItemFixtures.FirstItemId(root);
        var oldEntry = ItemFixtures.EntryName(itemFile);

        // Add a KB doc that links to this item. Items are flat by default
        // (<id-slug>.md), so the link target is `../../items/<entry>.md`.
        Assert.Equal(0, root.Run("kb", "add", "doc", "ref-the-task").ExitCode);
        var docPath = Path.Combine(root.Path, ".knit", "kb", "docs", "ref-the-task.md");
        var doc = File.ReadAllText(docPath);
        doc += "\n## Links\n\n" +
            "| Rel     | Target                              | Notes |\n" +
            "|---------|-------------------------------------|-------|\n" +
            $"| Related | [the task](../../items/{oldEntry}.md) |       |\n";
        File.WriteAllText(docPath, doc);

        var r = root.Run("move", id, "renamed-task");
        Assert.Equal(0, r.ExitCode);

        var newDoc = File.ReadAllText(docPath);
        Assert.DoesNotContain(oldEntry + ".md", newDoc);
        Assert.Contains($"{id}-renamed-task.md", newDoc);
    }

    [Fact]
    public void Verify_flags_malformed_item_link_id()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "source").ExitCode);
        var srcFile = ItemFixtures.FirstItemFile(root);
        var srcId = ItemFixtures.FirstItemId(root);
        var itemsDir = Path.GetDirectoryName(srcFile)!;

        // A sibling file whose id-part ("bogusid") isn't a valid NanoID — the
        // target exists on disk, so a path-existence-only scan stays green.
        File.WriteAllText(Path.Combine(itemsDir, "bogusid-mangled.md"), "# mangled\n");

        // Rewrite the source item with a ## Links row pointing at it.
        File.WriteAllText(srcFile,
            "---\n" +
            $"id: {srcId}\n" +
            "title: source\ntype: task\nstate: todo\n---\n\n" +
            "# source\n\n" +
            "## Links\n\n" +
            "| Rel        | Target                          | Notes |\n" +
            "|------------|---------------------------------|-------|\n" +
            "| Blocked by | [mangled](./bogusid-mangled.md) |       |\n");

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode); // hard failure
        Assert.Contains("\"malformed_item_links\":[{", r.Stdout);
        Assert.Contains("bogusid-mangled.md", r.Stdout);
    }

    [Fact]
    public void Verify_flags_dangling_prose_code_span_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        var docPath = Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md");
        var doc = File.ReadAllText(docPath) +
            "\nSee `adr:9999-missing` (dangling) and `doc:architecture` (live).\n" +
            "\n```\n`adr:8888-fenced`\n```\n";
        File.WriteAllText(docPath, doc);

        // Prose findings are warnings: exit 0 by default.
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"prose_refs\"", r.Stdout);
        Assert.Contains("adr:9999-missing", r.Stdout);
        Assert.DoesNotContain("doc:architecture", r.Stdout); // live ref not flagged
        Assert.DoesNotContain("adr:8888-fenced", r.Stdout);   // inside a code fence

        // --strict promotes the warning to a hard failure (exit 4).
        Assert.Equal((int)ErrorCode.Conflict, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Verify_skips_placeholder_and_wildcard_code_spans()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        var docPath = Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md");
        File.AppendAllText(docPath,
            "\nSub-docs go under `doc:shop/<sub>`, all of them `doc:shop/*`, sources `source:*`.\n" +
            "Still checked: `doc:shop-missing`.\n");

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("doc:shop-missing", r.Stdout);
        Assert.DoesNotContain("doc:shop/", r.Stdout);
        Assert.DoesNotContain("source:*", r.Stdout);
    }

    [Fact]
    public void Verify_flags_dangling_prose_md_link_and_image()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        var docDir = Path.Combine(root.Path, ".knit", "kb", "docs");
        var docPath = Path.Combine(docDir, "architecture.md");
        // A present attachment alongside the entry — must NOT be flagged.
        File.WriteAllText(Path.Combine(docDir, "diagram.png"), "png");
        var doc = File.ReadAllText(docPath) +
            "\nBroken [adr](../adrs/9999-missing.md), valid [self](architecture.md).\n" +
            "External [site](https://example.com), anchor [s](#sec).\n" +
            "Missing image ![d](./missing.png), present image ![ok](./diagram.png).\n";
        File.WriteAllText(docPath, doc);

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"prose_links\"", r.Stdout);
        Assert.Contains("../adrs/9999-missing.md", r.Stdout);
        Assert.Contains("./missing.png", r.Stdout);
        Assert.DoesNotContain("\"target\":\"architecture.md\"", r.Stdout); // valid in-repo link not flagged
        Assert.DoesNotContain("example.com", r.Stdout);       // external skipped
        Assert.DoesNotContain("./diagram.png", r.Stdout);     // present attachment
    }

    [Fact]
    public void Verify_checks_prose_links_into_the_repo_and_flags_ones_leaving_it()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "My Lib"));
        File.WriteAllText(Path.Combine(root.Path, "src", "My Lib", "a.cs"), "x");
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        // .knit/kb/docs/architecture.md → three levels up is the repo root.
        File.AppendAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md"),
            "\nFile [a](../../../src/My%20Lib/a.cs), folder [src](../../../src/), " +
            "gone [b](../../../src/b.cs), outside [x](../../../../elsewhere/x.md).\n");

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        var rows = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("prose_links").EnumerateArray()
            .Select(l => $"{l.GetProperty("target").GetString()} {l.GetProperty("reason").GetString()}").ToList();
        // One `../` too many is the usual slip: it leaves the repo whether or
        // not something happens to exist there on this machine.
        Assert.Equal(["../../../src/b.cs missing", "../../../../elsewhere/x.md outside_repo"], rows);
    }

    [Fact]
    public void Verify_flags_a_source_path_that_leaves_the_repo_without_a_mount()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "source", "spec", "--kind", "file", "--path", "spec.md").ExitCode);
        // `kb add source` rebases such a path onto a mount or refuses it, so it
        // only arrives by hand.
        var file = Path.Combine(root.Path, ".knit", "kb", "sources", "spec.md");
        File.WriteAllText(file, File.ReadAllText(file).Replace("path: spec.md", "path: ../elsewhere/spec.md"));

        var row = Assert.Single(JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement
            .GetProperty("source_paths").EnumerateArray());
        Assert.Equal("../elsewhere/spec.md", row.GetProperty("path").GetString());
        Assert.Contains("no mount covers it", row.GetProperty("reason").GetString());
    }

    [Fact]
    public void Verify_flags_a_repo_relative_source_path_that_is_gone()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Directory.CreateDirectory(Path.Combine(root.Path, "specs"));
        File.WriteAllText(Path.Combine(root.Path, "specs", "here.md"), "x");
        Assert.Equal(0, root.Run("kb", "add", "source", "here", "--kind", "file", "--path", "specs/here.md").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "gone", "--kind", "file", "--path", "specs/gone.md").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "source", "web", "--url", "https://example.com/spec").ExitCode);

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        var row = Assert.Single(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("source_paths").EnumerateArray());
        Assert.Equal("specs/gone.md", row.GetProperty("path").GetString());
        Assert.False(row.TryGetProperty("mount", out _));
        Assert.Equal((int)ErrorCode.Conflict, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Verify_clean_repo_reports_ok()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        var r = root.Run("verify");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("ok", r.Stdout);
    }

    [Fact]
    public void Verify_finds_broken_target()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "with broken link").ExitCode);
        var indexPath = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(indexPath);
        content += "\n## Links\n\n" +
            "| Rel    | Target                                  | Notes |\n" +
            "|--------|-----------------------------------------|-------|\n" +
            "| KB ref | [doc:missing](../kb/docs/missing.md) |       |\n";
        File.WriteAllText(indexPath, content);

        var r = root.Run("verify", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("\"link_targets\"", r.Stdout);
        Assert.Contains("doc:missing", r.Stdout);
    }

    [Fact]
    public void Verify_finds_broken_design_prose_ref()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "with design ref").ExitCode);
        var indexPath = ItemFixtures.FirstItemFile(root);
        // A bare `design:ghost` code-span in prose — no such design exists.
        File.AppendAllText(indexPath, "\nSee `design:ghost` for the plan.\n");

        var r = root.Run("verify", "--json", "--strict");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"prose_refs\"", r.Stdout);
        Assert.Contains("design:ghost", r.Stdout);
    }

    [Fact]
    public void Verify_flags_dangling_adr_origin()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui", "--title", "Web UI").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Adopt loopback", "--origin", "design:web-ui").ExitCode);

        // Origin resolves now — clean.
        Assert.Equal(0, root.Run("verify").ExitCode);

        // Remove the design out-of-band; the ADR's origin now dangles.
        File.Delete(Path.Combine(root.Path, ".knit", "designs", "web-ui-design.md"));

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"origins\"", r.Stdout);
        Assert.Contains("design:web-ui", r.Stdout);
    }

    [Fact]
    public void Verify_flags_duplicate_kb_id()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Switch supplier").ExitCode);

        var adrsDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        var real = Directory.EnumerateFiles(adrsDir, "*.md")
            .First(f => !string.Equals(Path.GetFileName(f), "index.md", StringComparison.OrdinalIgnoreCase));

        // Clean before the fork.
        Assert.Equal(0, root.Run("verify").ExitCode);

        // Hand-fork a twin with the same frontmatter id but a divergent slug —
        // the exact shape the old `kb set adr:NNNN` write bug produced.
        var twin = Path.Combine(adrsDir, "0001.md");
        File.Copy(real, twin);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"duplicate_ids\"", r.Stdout);
        Assert.Contains("\"0001\"", r.Stdout);
    }

    [Fact]
    public void Verify_valid_design_prose_ref_passes()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("design", "new", "web-ui").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "with design ref").ExitCode);
        var indexPath = ItemFixtures.FirstItemFile(root);
        File.AppendAllText(indexPath, "\nSee `design:web-ui` for the plan.\n");

        var r = root.Run("verify", "--strict");
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Verify_finds_hierarchy_violation()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // Create a task; hand-edit its frontmatter type to `epic` so the
        // (epic, parent=task) check below fires. Use a parent task so the
        // child epic has an invalid parent.
        Assert.Equal(0, root.Run("add", "task", "real parent").ExitCode);
        var parentFile = ItemFixtures.FirstItemFile(root);
        var parentEntry = ItemFixtures.EntryName(parentFile);
        Assert.Equal(0, root.Run("add", "task", "bad child").ExitCode);
        var childFile = ItemFixtures.AllItemFiles(root).First(f => f != parentFile);

        // Hand-edit child: switch type to epic + point parent at the task.
        var content = File.ReadAllText(childFile);
        content = content.Replace("type: task", "type: epic");
        content += $"\n## Links\n\n" +
            "| Rel    | Target                                              | Notes |\n" +
            "|--------|-----------------------------------------------------|-------|\n" +
            $"| Parent | [real parent](./{parentEntry}.md)                   |       |\n";
        File.WriteAllText(childFile, content);

        var r = root.Run("verify", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("\"hierarchy\"", r.Stdout);
        Assert.Contains("\"child_type\":\"epic\"", r.Stdout);
        Assert.Contains("\"parent_type\":\"task\"", r.Stdout);
    }

    [Fact]
    public void Verify_clean_repo_passes()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "ok").ExitCode);
        var r = root.Run("verify");
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Verify_title_drift_in_item_is_reported()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "Original Title").ExitCode);

        var itemFile = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(itemFile);
        // Inject a body h1 that disagrees with the frontmatter title.
        // The item template starts the body with no leading h1, so we
        // prepend one to the post-frontmatter region.
        var marker = "---\n\n";
        var idx = content.IndexOf(marker, 4, StringComparison.Ordinal);
        Assert.True(idx > 0);
        var insertAt = idx + marker.Length;
        var injected = content[..insertAt] + "# Drifted Title\n\n" + content[insertAt..];
        File.WriteAllText(itemFile, injected);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"titles\"", r.Stdout);
        Assert.Contains("Drifted Title", r.Stdout);
        Assert.Contains("Original Title", r.Stdout);
    }

    [Fact]
    public void Verify_title_drift_in_kb_is_reported()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "auth-notes").ExitCode);

        var docPath = Path.Combine(root.Path, ".knit", "kb", "docs", "auth-notes.md");
        var content = File.ReadAllText(docPath);
        var marker = "---\n\n";
        var idx = content.IndexOf(marker, 4, StringComparison.Ordinal);
        Assert.True(idx > 0);
        var insertAt = idx + marker.Length;
        var withH1 = content[..insertAt] + "# Auth Notes v2\n\n" + content[insertAt..];
        File.WriteAllText(docPath, withH1);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"titles\"", r.Stdout);
        Assert.Contains("Auth Notes v2", r.Stdout);
    }

    [Fact]
    public void Verify_matching_title_and_h1_is_ok()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "Same Title").ExitCode);

        var itemFile = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(itemFile);
        var marker = "---\n\n";
        var idx = content.IndexOf(marker, 4, StringComparison.Ordinal);
        var insertAt = idx + marker.Length;
        var withH1 = content[..insertAt] + "# Same Title\n\n" + content[insertAt..];
        File.WriteAllText(itemFile, withH1);

        var r = root.Run("verify");
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Verify_no_body_h1_is_ok()
    {
        // Default template has no body h1 -> frontmatter title alone is canonical.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "lonely title").ExitCode);
        var r = root.Run("verify");
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Verify_json_envelope_carries_both_classes()
    {
        using var root = new TempKnitRoot();
        Init(root);

        // Item with broken link target.
        Assert.Equal(0, root.Run("add", "task", "broken").ExitCode);
        var brokenFile = ItemFixtures.FirstItemFile(root);
        var bc = File.ReadAllText(brokenFile);
        bc += "\n## Links\n\n" +
            "| Rel    | Target                                  | Notes |\n" +
            "|--------|-----------------------------------------|-------|\n" +
            "| KB ref | [doc:missing](../kb/docs/missing.md) |       |\n";
        File.WriteAllText(brokenFile, bc);

        // Second item with title drift.
        Assert.Equal(0, root.Run("add", "task", "Drift Title").ExitCode);
        var allFiles = ItemFixtures.AllItemFiles(root);
        var driftFile = allFiles.First(f => f != brokenFile);
        var dc = File.ReadAllText(driftFile);
        var marker = "---\n\n";
        var idx = dc.IndexOf(marker, 4, StringComparison.Ordinal);
        var insertAt = idx + marker.Length;
        dc = dc[..insertAt] + "# Different\n\n" + dc[insertAt..];
        File.WriteAllText(driftFile, dc);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"link_targets\"", r.Stdout);
        Assert.Contains("\"titles\"", r.Stdout);
        Assert.Contains("doc:missing", r.Stdout);
        Assert.Contains("Different", r.Stdout);
    }

    [Fact]
    public void Verify_flags_item_file_with_bad_id_name()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "real").ExitCode);
        var itemsDir = Path.GetDirectoryName(ItemFixtures.FirstItemFile(root))!;

        // A flat file whose id-part ("notanid") isn't a valid NanoID — it
        // enumerates but its identity is broken.
        File.WriteAllText(Path.Combine(itemsDir, "notanid-thing.md"),
            "---\nid: notanid\ntitle: thing\ntype: task\nstate: todo\n---\n\n# thing\n");

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"invalid_items\":[{", r.Stdout);
        Assert.Contains("notanid-thing.md", r.Stdout);
    }

    [Fact]
    public void Verify_flags_item_id_folder_mismatch()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "mislabelled").ExitCode);
        var itemFile = ItemFixtures.FirstItemFile(root);
        var id = ItemFixtures.FirstItemId(root);

        // Hand-edit the frontmatter id to a *different* valid NanoID: the file
        // name says one id, the frontmatter another — split-brain identity.
        var content = File.ReadAllText(itemFile).Replace($"id: {id}", "id: ABCDEFGHIJKLMNOPQR");
        File.WriteAllText(itemFile, content);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"invalid_items\":[{", r.Stdout);
        Assert.Contains("id mismatch", r.Stdout);
    }

    [Fact]
    public void Verify_flags_a_folder_in_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "real").ExitCode);
        var itemsDir = Path.GetDirectoryName(ItemFixtures.FirstItemFile(root))!;
        var folder = Path.Combine(itemsDir, "ABCDEFGHIJKLMNOPQR-orphan");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.md"), "---\nid: ABCDEFGHIJKLMNOPQR\ntitle: orphan\n---\n");

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"invalid_items\":[{", r.Stdout);
        Assert.Contains("items are flat", r.Stdout);
        Assert.DoesNotContain("orphan", root.Run("list").Stdout);
    }

    [Fact]
    public void Verify_flags_item_missing_frontmatter_id()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "no id").ExitCode);
        var itemFile = ItemFixtures.FirstItemFile(root);
        var id = ItemFixtures.FirstItemId(root);

        // Drop the id: line from frontmatter — the item loads with a blank id.
        var content = File.ReadAllText(itemFile).Replace($"id: {id}\n", "");
        File.WriteAllText(itemFile, content);

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"invalid_items\":[{", r.Stdout);
        Assert.Contains("missing 'id'", r.Stdout);
    }

    [Fact]
    public void Verify_flags_design_file_missing_suffix()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // A design doc without the `-design.md` suffix — invisible to
        // `design list`, which only enumerates `*-design.md` at the root.
        var designsDir = Path.Combine(root.Path, ".knit", "designs");
        Directory.CreateDirectory(designsDir);
        File.WriteAllText(Path.Combine(designsDir, "orphan-notes.md"),
            "---\ntitle: Notes\n---\n\n# Notes\n");

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"stray_files\":[{", r.Stdout);
        Assert.Contains("orphan-notes.md", r.Stdout);
    }

    [Fact]
    public void Verify_flags_leftover_jsonl_index()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "alpha").ExitCode);
        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "index.jsonl")));

        File.WriteAllText(Path.Combine(root.Path, ".knit", "index.jsonl"), "{}\n");
        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("index.jsonl", r.Stdout);
        Assert.Contains("safe to delete", r.Stdout);
    }

    [Fact]
    public void Verify_flags_kb_file_outside_a_bucket()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var kbDir = Path.Combine(root.Path, ".knit", "kb");

        // (a) a .md directly under kb/ (no bucket folder), and
        // (b) a .md under an unknown bucket folder — both unaddressable.
        Directory.CreateDirectory(kbDir);
        File.WriteAllText(Path.Combine(kbDir, "loose.md"), "---\ntitle: Loose\n---\n\n# Loose\n");
        Directory.CreateDirectory(Path.Combine(kbDir, "bogus"));
        File.WriteAllText(Path.Combine(kbDir, "bogus", "x.md"), "---\ntitle: X\n---\n\n# X\n");

        var r = root.Run("verify", "--json");
        Assert.Equal((int)ErrorCode.Conflict, r.ExitCode);
        Assert.Contains("\"stray_files\":[{", r.Stdout);
        Assert.Contains("loose.md", r.Stdout);
        Assert.Contains("no bucket folder", r.Stdout);          // the kb/loose.md reason
        Assert.Contains("unknown KB bucket folder 'bogus'", r.Stdout); // the kb/bogus/x.md reason
    }

    [Fact]
    public void Verify_does_not_flag_valid_kb_entry_as_stray()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // A normal KB doc lives under a known bucket (kb/docs/) — must not be
        // flagged as stray.
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"stray_files\":[]", r.Stdout);
    }

    [Fact]
    public void Relink_non_interactive_repairs_when_uuid_known()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "alpha").ExitCode);
        var alphaFile = ItemFixtures.FirstItemFile(root);
        var alphaId = ItemFixtures.FirstItemId(root);
        var alphaEntry = ItemFixtures.EntryName(alphaFile);

        Assert.Equal(0, root.Run("add", "task", "beta").ExitCode);
        var allFiles = ItemFixtures.AllItemFiles(root);
        var betaFile = allFiles.First(f => f != alphaFile);

        // Inject a link in beta that uses the right uuid but wrong slug.
        // Both items are flat: target path is `<entry>.md` (no `../`,
        // since both live directly under `.knit/items/`).
        var betaContent = File.ReadAllText(betaFile);
        betaContent += "\n## Links\n\n" +
            "| Rel     | Target                              | Notes |\n" +
            "|---------|-------------------------------------|-------|\n" +
            $"| Related | [alpha]({alphaId}-wrong-slug.md)    |       |\n";
        File.WriteAllText(betaFile, betaContent);

        // Broken before relink.
        Assert.NotEqual(0, root.Run("verify").ExitCode);

        var r = root.Run("relink", "--non-interactive");
        Assert.Equal(0, r.ExitCode);

        var fixedContent = File.ReadAllText(betaFile);
        Assert.Contains($"{alphaEntry}.md", fixedContent);
        Assert.DoesNotContain("wrong-slug", fixedContent);
    }
}
