using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Project doc folders + the `Milestone` relation:
/// `knit project` scaffolding, the `project:` item pointer, `link/unlink
/// --milestone` (bare + qualified slug forms), the `--milestone` filter on
/// list/tree, milestone backlinks, and the new verify classes.
/// </summary>
[Collection(nameof(CliCollection))]
public class ProjectMilestoneTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, string type, string title, string? parent = null)
    {
        var args = parent is null
            ? new[] { "add", type, title, "--json" }
            : new[] { "add", type, title, "--parent", parent, "--json" };
        var r = root.Run(args);
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static string ItemFileOf(TempKnitRoot root, string id) =>
        ItemFixtures.AllItemFiles(root).First(f => ItemFixtures.EntryName(f).StartsWith(id, StringComparison.Ordinal));

    [Fact]
    public void Project_new_list_and_milestone_scaffold_round_trip()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--title", "Website relaunch", "--roadmap", "--json").ExitCode);
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "projects", "relaunch", "readme.md")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "projects", "relaunch", "roadmap.md")));

        // Refuses to overwrite without --force.
        Assert.NotEqual(0, root.Run("project", "new", "relaunch", "--json").ExitCode);

        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut", "--target", "2026-09-01", "--json").ExitCode);

        var list = root.Run("project", "list", "--json");
        Assert.Equal(0, list.ExitCode);
        var entry = Assert.Single(JsonDocument.Parse(list.Stdout).RootElement.EnumerateArray().ToList());
        Assert.Equal("relaunch", entry.GetProperty("slug").GetString());
        Assert.True(entry.GetProperty("has_roadmap").GetBoolean());
        Assert.Equal(1, entry.GetProperty("milestones").GetInt32());

        var ms = root.Run("project", "milestone", "list", "--json");
        Assert.Equal(0, ms.ExitCode);
        var doc = Assert.Single(JsonDocument.Parse(ms.Stdout).RootElement.EnumerateArray().ToList());
        Assert.Equal("relaunch", doc.GetProperty("project").GetString());
        Assert.Equal("beta", doc.GetProperty("slug").GetString());
        Assert.Equal("Beta cut", doc.GetProperty("title").GetString());
        Assert.Equal("2026-09-01", doc.GetProperty("target").GetString());
        Assert.Equal("planned", doc.GetProperty("state").GetString());
    }

    [Fact]
    public void Set_project_pointer_validates_type_and_folder()
    {
        using var root = Init();
        var task = Add(root, "task", "t");
        var project = Add(root, "project", "Relaunch");

        var wrongType = root.Run("set", task, "project=relaunch", "--json");
        Assert.Equal(2, wrongType.ExitCode);
        Assert.Contains("project-only", wrongType.Stdout + wrongType.Stderr);

        var dangling = root.Run("set", project, "project=relaunch", "--json");
        Assert.NotEqual(0, dangling.ExitCode);
        Assert.Contains("knit project new", dangling.Stdout + dangling.Stderr);

        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        var set = root.Run("set", project, "project=relaunch", "--json");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal("relaunch", JsonDocument.Parse(set.Stdout).RootElement.GetProperty("project").GetString());

        var shown = root.Run("show", project, "--json");
        Assert.Equal("relaunch", JsonDocument.Parse(shown.Stdout).RootElement.GetProperty("project").GetString());

        var clear = root.Run("set", project, "project=", "--json");
        Assert.Equal(0, clear.ExitCode);
        Assert.False(JsonDocument.Parse(clear.Stdout).RootElement.TryGetProperty("project", out _));
    }

    [Fact]
    public void Link_milestone_bare_slug_resolves_within_own_project()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut").ExitCode);
        var project = Add(root, "project", "Relaunch");
        Assert.Equal(0, root.Run("set", project, "project=relaunch").ExitCode);
        var epic = Add(root, "epic", "Content", project);

        var link = root.Run("link", epic, "--milestone", "beta", "--json");
        Assert.Equal(0, link.ExitCode);
        var links = JsonDocument.Parse(link.Stdout).RootElement.GetProperty("links").EnumerateArray().ToList();
        var row = Assert.Single(links, l => l.GetProperty("rel").GetString() == "Milestone");
        Assert.Contains("projects/relaunch/milestones/beta.md", row.GetProperty("target_path").GetString());
        Assert.Equal("Beta cut", row.GetProperty("target_text").GetString());

        // A bare slug that isn't in the item's own project errors with the
        // qualified-form hint instead of silently searching elsewhere.
        // (JSON error output \u-escapes quotes and angle brackets — assert
        // on plain-ascii fragments of the scoped message + hint.)
        var missing = root.Run("link", epic, "--milestone", "nope", "--json");
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("in project", missing.Stdout + missing.Stderr);
        Assert.Contains("across projects", missing.Stdout + missing.Stderr);
    }

    [Fact]
    public void Link_milestone_qualified_form_links_across_projects_and_replaces()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        Assert.Equal(0, root.Run("project", "new", "other", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "other", "final").ExitCode);
        var task = Add(root, "task", "shared deliverable");

        // Qualified form works with no project scope at all.
        Assert.Equal(0, root.Run("link", task, "--milestone", "other/final", "--json").ExitCode);

        // Linking again replaces the row — one milestone per item.
        var relink = root.Run("link", task, "--milestone", "relaunch/beta", "--json");
        Assert.Equal(0, relink.ExitCode);
        var rows = JsonDocument.Parse(relink.Stdout).RootElement.GetProperty("links").EnumerateArray()
            .Where(l => l.GetProperty("rel").GetString() == "Milestone").ToList();
        var row = Assert.Single(rows);
        Assert.Contains("relaunch/milestones/beta.md", row.GetProperty("target_path").GetString());

        // Unlink by milestone ref removes the row.
        var unlink = root.Run("unlink", task, "relaunch/beta", "--json");
        Assert.Equal(0, unlink.ExitCode);
        Assert.DoesNotContain(JsonDocument.Parse(unlink.Stdout).RootElement.GetProperty("links").EnumerateArray(),
            l => l.GetProperty("rel").GetString() == "Milestone");
    }

    [Fact]
    public void List_and_tree_filter_by_milestone()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        var linked = Add(root, "task", "in the cut");
        Add(root, "task", "not in the cut");
        Assert.Equal(0, root.Run("link", linked, "--milestone", "relaunch/beta").ExitCode);

        // Bare slug is unambiguous here — one project carries it.
        var list = root.Run("list", "--milestone", "beta", "--json");
        Assert.Equal(0, list.ExitCode);
        var items = JsonDocument.Parse(list.Stdout).RootElement.GetProperty("items").EnumerateArray().ToList();
        var only = Assert.Single(items);
        Assert.Equal(linked, only.GetProperty("id").GetString());

        var tree = root.Run("tree", "--milestone", "relaunch/beta");
        Assert.Equal(0, tree.ExitCode);
        Assert.Contains("in the cut", tree.Stdout);
        Assert.DoesNotContain("not in the cut", tree.Stdout);

        // Ambiguous bare slug across projects → error naming both.
        Assert.Equal(0, root.Run("project", "new", "other", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "other", "beta").ExitCode);
        var ambiguous = root.Run("list", "--milestone", "beta", "--json");
        Assert.Equal(2, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.Stdout + ambiguous.Stderr);
    }

    [Fact]
    public void Milestone_inherits_down_the_tree_and_an_own_link_wins()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "ga").ExitCode);
        var epic = Add(root, "epic", "the cut");
        var feature = Add(root, "feature", "inherits", epic);
        var task = Add(root, "task", "deep", feature);
        var moved = Add(root, "feature", "moved on", epic);
        var movedTask = Add(root, "task", "follows its feature", moved);
        Add(root, "task", "elsewhere");
        Assert.Equal(0, root.Run("link", epic, "--milestone", "relaunch/beta").ExitCode);
        Assert.Equal(0, root.Run("link", moved, "--milestone", "relaunch/ga").ExitCode);

        static HashSet<string> Ids(string stdout) => JsonDocument.Parse(stdout).RootElement
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToHashSet();

        var beta = root.Run("list", "--milestone", "beta", "--json");
        Assert.Equal(0, beta.ExitCode);
        Assert.Equal(new HashSet<string> { epic, feature, task }, Ids(beta.Stdout));
        Assert.Equal(new HashSet<string> { moved, movedTask }, Ids(root.Run("list", "--milestone", "ga", "--json").Stdout));

        var row = JsonDocument.Parse(beta.Stdout).RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == task);
        Assert.Equal("relaunch/beta", row.GetProperty("effective_milestone").GetString());

        var status = root.Run("status", "--milestone", "relaunch/beta", "--json");
        Assert.Equal(3, JsonDocument.Parse(status.Stdout).RootElement.GetProperty("total").GetInt32());

        // The backlinks surface stays where links are written.
        var show = root.Run("project", "milestone", "show", "relaunch/beta", "--json");
        var backlink = Assert.Single(JsonDocument.Parse(show.Stdout).RootElement.GetProperty("backlinks").EnumerateArray().ToList());
        Assert.Equal(epic, backlink.GetProperty("id").GetString());

        var context = root.Run("context", task, "--json");
        Assert.Equal(0, context.ExitCode);
        var ms = JsonDocument.Parse(context.Stdout).RootElement.GetProperty("milestone");
        Assert.Equal("relaunch/beta", ms.GetProperty("ref").GetString());
        Assert.Equal($"item:{epic}", ms.GetProperty("inherited_from").GetString());
        var own = JsonDocument.Parse(root.Run("context", moved, "--json").Stdout).RootElement.GetProperty("milestone");
        Assert.Equal(JsonValueKind.Null, own.GetProperty("inherited_from").ValueKind);

        var human = root.Run("context", task);
        Assert.Contains("Milestone", human.Stdout);
        Assert.Contains("relaunch/beta", human.Stdout);
    }

    [Fact]
    public void Milestone_show_surfaces_backlinks()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut").ExitCode);
        var task = Add(root, "task", "linked work");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        var show = root.Run("project", "milestone", "show", "relaunch/beta", "--json");
        Assert.Equal(0, show.ExitCode);
        var doc = JsonDocument.Parse(show.Stdout).RootElement;
        Assert.Equal("Beta cut", doc.GetProperty("title").GetString());
        var backlink = Assert.Single(doc.GetProperty("backlinks").EnumerateArray().ToList());
        Assert.Equal(task, backlink.GetProperty("id").GetString());
        Assert.Equal("linked work", backlink.GetProperty("title").GetString());

        var human = root.Run("project", "milestone", "show", "beta");
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("linked work", human.Stdout);
    }

    [Fact]
    public void Milestone_set_edits_state_target_title_and_clears_target()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut", "--target", "2026-09-01").ExitCode);

        var set = root.Run("project", "milestone", "set", "relaunch/beta", "--state", "reached", "--title", "Beta shipped", "--json");
        Assert.Equal(0, set.ExitCode);
        var doc = JsonDocument.Parse(set.Stdout).RootElement;
        Assert.Equal("reached", doc.GetProperty("state").GetString());
        Assert.Equal("Beta shipped", doc.GetProperty("title").GetString());
        Assert.Equal("2026-09-01", doc.GetProperty("target").GetString());

        // created preserved; body H1 mirrors the new title.
        var body = File.ReadAllText(Path.Combine(root.Path, ".knit", "projects", "relaunch", "milestones", "beta.md"));
        Assert.Contains("created:", body);
        Assert.Contains("# Beta shipped", body);

        // Empty --target clears the field; bare slug resolves the one project.
        var clear = root.Run("project", "milestone", "set", "beta", "--target", "", "--json");
        Assert.Equal(0, clear.ExitCode);
        Assert.False(JsonDocument.Parse(clear.Stdout).RootElement.TryGetProperty("target", out _));

        // Unknown state rejected; no-op set rejected.
        Assert.NotEqual(0, root.Run("project", "milestone", "set", "relaunch/beta", "--state", "shipped", "--json").ExitCode);
        Assert.NotEqual(0, root.Run("project", "milestone", "set", "relaunch/beta", "--json").ExitCode);
    }

    [Fact]
    public void Milestone_rename_repoints_prose_links()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut").ExitCode);
        var roadmap = Path.Combine(root.Path, ".knit", "projects", "relaunch", "roadmap.md");
        File.AppendAllText(roadmap, "\nNext: [Beta cut](milestones/beta.md).\n");

        var rn = root.Run("project", "milestone", "rename", "relaunch/beta", "beta-2", "--json");
        Assert.Equal(0, rn.ExitCode);
        var relinked = JsonDocument.Parse(rn.Stdout).RootElement.GetProperty("files_relinked");
        Assert.Equal(roadmap, Path.GetFullPath(Assert.Single(relinked.EnumerateArray()).GetString()!));
        Assert.Contains("[Beta cut](milestones/beta-2.md)", File.ReadAllText(roadmap));
    }

    [Fact]
    public void Milestone_rename_moves_file_and_repoints_backlinks()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta", "--title", "Beta cut").ExitCode);
        var task = Add(root, "task", "linked work");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        var rn = root.Run("project", "milestone", "rename", "relaunch/beta", "beta-2", "--json");
        Assert.Equal(0, rn.ExitCode);
        var doc = JsonDocument.Parse(rn.Stdout).RootElement;
        Assert.Equal("beta-2", doc.GetProperty("new_slug").GetString());
        var repointed = doc.GetProperty("repointed_item_ids").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(task, Assert.Single(repointed));

        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "projects", "relaunch", "milestones", "beta.md")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "projects", "relaunch", "milestones", "beta-2.md")));

        // Row now points at the new file → no broken target, and show by the
        // new ref works.
        Assert.Equal(0, root.Run("verify", "--json").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "show", "relaunch/beta-2", "--json").ExitCode);
    }

    [Fact]
    public void Milestone_delete_leaves_backlinks_dangling_by_default_and_scrubs_on_flag()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        var task = Add(root, "task", "linked work");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        var del = root.Run("project", "milestone", "delete", "relaunch", "beta", "-y", "--json");
        Assert.Equal(0, del.ExitCode);
        var doc = JsonDocument.Parse(del.Stdout).RootElement;
        Assert.Equal(task, Assert.Single(doc.GetProperty("dangling_item_ids").EnumerateArray().Select(e => e.GetString()).ToList()));
        Assert.Empty(doc.GetProperty("scrubbed_item_ids").EnumerateArray().ToList());
        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "projects", "relaunch", "milestones", "beta.md")));

        // Row left dangling → verify now reports a broken target.
        Assert.NotEqual(0, root.Run("verify", "--json").ExitCode);

        // Recreate + relink, then delete with --scrub-links: the row is removed.
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);
        var scrub = root.Run("project", "milestone", "delete", "relaunch", "beta", "-y", "--scrub-links", "--json");
        Assert.Equal(0, scrub.ExitCode);
        var sdoc = JsonDocument.Parse(scrub.Stdout).RootElement;
        Assert.Equal(task, Assert.Single(sdoc.GetProperty("scrubbed_item_ids").EnumerateArray().Select(e => e.GetString()).ToList()));
        Assert.Empty(sdoc.GetProperty("dangling_item_ids").EnumerateArray().ToList());
        Assert.Equal(0, root.Run("verify", "--json").ExitCode);

        Assert.NotEqual(0, root.Run("project", "milestone", "delete", "relaunch", "beta", "-y", "--json").ExitCode); // gone
    }

    [Fact]
    public void Verify_flags_bad_project_pointers()
    {
        using var root = Init();
        // Co-create a joined project (item + folder + link) in one action.
        var created = root.Run("project", "new", "relaunch", "--json");
        Assert.Equal(0, created.ExitCode);
        var project = JsonDocument.Parse(created.Stdout).RootElement.GetProperty("item_id").GetString()!;

        // Dangling: the folder disappears after the pointer was set.
        Directory.Delete(Path.Combine(root.Path, ".knit", "projects", "relaunch"), recursive: true);
        var dangling = root.Run("verify", "--json");
        Assert.Equal(4, dangling.ExitCode);
        using (var doc = JsonDocument.Parse(dangling.Stdout))
        {
            var finding = Assert.Single(doc.RootElement.GetProperty("project_pointers").EnumerateArray().ToList());
            Assert.Equal(project, finding.GetProperty("item_id").GetString());
            Assert.Equal("relaunch", finding.GetProperty("slug").GetString());
            Assert.Contains("dangling", finding.GetProperty("reason").GetString());
        }

        // Field on a non-project (hand-edit past the CLI guard).
        Assert.Equal(0, root.Run("set", project, "project=").ExitCode);
        var task = Add(root, "task", "hand-edited");
        var file = ItemFileOf(root, task);
        File.WriteAllText(file, File.ReadAllText(file)
            .Replace("state: todo", "state: todo\nproject: relaunch"));
        var wrongType = root.Run("verify", "--json");
        Assert.Equal(4, wrongType.ExitCode);
        using var doc2 = JsonDocument.Parse(wrongType.Stdout);
        var f2 = Assert.Single(doc2.RootElement.GetProperty("project_pointers").EnumerateArray().ToList());
        Assert.Equal(task, f2.GetProperty("item_id").GetString());
        Assert.Contains("project-only", f2.GetProperty("reason").GetString());

        var human = root.Run("verify");
        Assert.Equal(4, human.ExitCode);
        Assert.Contains("project doc-folder pointers", human.Stdout);
    }

    [Fact]
    public void Verify_flags_malformed_milestones_and_stray_projects_root_files()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        var milestonesDir = Path.Combine(root.Path, ".knit", "projects", "relaunch", "milestones");
        Directory.CreateDirectory(milestonesDir);
        File.WriteAllText(Path.Combine(milestonesDir, "bad.md"), "---\ntitle: Bad\nstate: shipped\n---\n");
        File.WriteAllText(Path.Combine(milestonesDir, "notes.txt"), "not a milestone");
        File.WriteAllText(Path.Combine(root.Path, ".knit", "projects", "loose.md"), "# orphan");

        var r = root.Run("verify", "--json");
        Assert.Equal(4, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var malformed = doc.RootElement.GetProperty("malformed_milestones").EnumerateArray().ToList();
        Assert.Contains(malformed, m => m.GetProperty("source").GetString()!.EndsWith("bad.md")
            && m.GetProperty("reason").GetString()!.Contains("state"));
        Assert.Contains(malformed, m => m.GetProperty("source").GetString()!.EndsWith("notes.txt"));
        Assert.Contains(doc.RootElement.GetProperty("stray_files").EnumerateArray(),
            s => s.GetProperty("source").GetString()!.EndsWith("loose.md"));
    }

    [Fact]
    public void Verify_warns_on_multiple_milestone_rows()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("project", "new", "relaunch", "--roadmap").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "beta").ExitCode);
        Assert.Equal(0, root.Run("project", "milestone", "new", "relaunch", "go-live").ExitCode);
        var task = Add(root, "task", "greedy");
        Assert.Equal(0, root.Run("link", task, "--milestone", "relaunch/beta").ExitCode);

        // Hand-edit a second Milestone row past the replace-on-link rule.
        var file = ItemFileOf(root, task);
        var content = File.ReadAllText(file);
        var msLine = content.Split('\n').First(l => l.Contains("| Milestone"));
        File.WriteAllText(file, content.Replace(msLine, msLine + "\n" + msLine.Replace("beta", "go-live")));

        // Warning class: exit 0 without --strict, 4 with it.
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var finding = Assert.Single(doc.RootElement.GetProperty("multiple_milestones").EnumerateArray().ToList());
        Assert.Equal(task, finding.GetProperty("item_id").GetString());
        Assert.Equal(2, finding.GetProperty("count").GetInt32());

        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);
    }
}
