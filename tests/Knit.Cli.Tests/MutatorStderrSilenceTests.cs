namespace Knit.Cli.Tests;

/// <summary>
/// Every mutator must leave stderr empty on its success
/// path. Errors stay on stderr (verified by other tests). This invariant
/// matters for the demo seed script and for any agent or shell
/// pipeline that distinguishes "ran clean" from "ran with warnings."
///
/// Runs one long lifecycle on a single repo: each mutator is asserted
/// for zero stderr right after its call, so a regression names the
/// offending command.
/// </summary>
[Collection(nameof(CliCollection))]
public class MutatorStderrSilenceTests
{
    [Fact]
    public void Every_mutator_leaves_stderr_empty_on_success()
    {
        using var root = new TempKnitRoot();

        AssertSilent(root.Run("init", "--handle", "demo", "--display", "Demo", "--non-interactive"), "init");

        // ---- items: add / state / assign / set / reorder / move / comment
        AssertSilent(root.Run("add", "epic", "Top epic"), "add epic");
        var epicId = FirstId(root);

        AssertSilent(root.Run("add", "feature", "Feature A", "--parent", epicId), "add feature");
        var featureId = LastId(root, "Feature A");

        AssertSilent(root.Run("add", "task", "Task one", "--parent", featureId), "add task one");
        var t1 = LastId(root, "Task one");
        AssertSilent(root.Run("add", "task", "Task two", "--parent", featureId), "add task two");
        var t2 = LastId(root, "Task two");

        AssertSilent(root.Run("start", t1), "start");
        AssertSilent(root.Run("done", t1), "done");
        AssertSilent(root.Run("assign", t2, "alice"), "assign");
        AssertSilent(root.Run("set", t2, "effort=M"), "set effort");
        AssertSilent(root.Run("reorder", t2, "--before", t1), "reorder");
        AssertSilent(root.Run("comment", t2, "looking at this now"), "comment");

        // ---- links
        AssertSilent(root.Run("link", t2, "--related", t1), "link --related");
        AssertSilent(root.Run("unlink", t2, t1), "unlink");
        AssertSilent(root.Run("link", t2, "--blocks", t1), "link --blocks");
        AssertSilent(root.Run("unlink", t2, t1), "unlink (blocks)");

        // ---- block (uses --by, also writes blocked state)
        AssertSilent(root.Run("add", "task", "Blocker", "--parent", featureId), "add blocker");
        var blockerId = LastId(root, "Blocker");
        AssertSilent(root.Run("block", t2, "--by", blockerId), "block");

        // ---- KB add (all 6 buckets)
        AssertSilent(root.Run("kb", "add", "adr", "First decision"), "kb add adr");
        AssertSilent(root.Run("kb", "add", "doc", "architecture", "--title", "Architecture"), "kb add doc");
        AssertSilent(root.Run("kb", "add", "feature", "feature-a", "--title", "Feature A"), "kb add feature");
        AssertSilent(root.Run("kb", "add", "log", "first thing happened"), "kb add log");
        AssertSilent(root.Run("kb", "add", "idea", "rolling-dice tournament"), "kb add idea");
        AssertSilent(root.Run("kb", "add", "source", "spec-v1", "--url", "https://example.com/spec"), "kb add source");

        // ---- KB set / rename / index
        AssertSilent(root.Run("kb", "set", "doc:architecture", "summary", "How the thing fits together"), "kb set");
        AssertSilent(root.Run("kb", "rename", "doc:architecture", "system-architecture"), "kb rename");
        AssertSilent(root.Run("kb", "index"), "kb index");

        // ---- KB supersede (ADR-only). Add a second ADR to supersede the first.
        AssertSilent(root.Run("kb", "add", "adr", "Second decision"), "kb add adr 2");
        AssertSilent(root.Run("kb", "supersede", "0002", "0001"), "kb supersede");

        // ---- KB split (feature/doc only)
        AssertSilent(root.Run("kb", "split", "feature:feature-a"), "kb split");

        // ---- link --kb-ref (item → KB)
        AssertSilent(root.Run("link", t2, "--kb-ref", "doc:system-architecture", "--note", "see §3"),
            "link --kb-ref");
        AssertSilent(root.Run("unlink", t2, "doc:system-architecture"), "unlink kb-ref");

        // ---- item move (slug rename)
        AssertSilent(root.Run("move", t2, "renamed-task-two"), "move");

        // ---- design new / archive
        AssertSilent(root.Run("design", "new", "demo-cycle", "--title", "Demo"), "design new");
        AssertSilent(root.Run("design", "archive", "demo-cycle"), "design archive");
    }

    private static void AssertSilent(CliResult r, string label)
    {
        Assert.True(r.ExitCode == 0,
            $"{label} returned exit {r.ExitCode}\nstdout: {r.Stdout}\nstderr: {r.Stderr}");
        Assert.True(string.IsNullOrEmpty(r.Stderr),
            $"{label} wrote to stderr on success:\n{r.Stderr}");
    }

    private static string FirstId(TempKnitRoot root)
    {
        var listed = root.Run("list", "--json");
        return ExtractFirstId(listed.Stdout);
    }

    private static string LastId(TempKnitRoot root, string title)
    {
        var listed = root.Run("list", "--json");
        return IdForTitle(listed.Stdout, title);
    }

    private static string ExtractFirstId(string json)
    {
        const string marker = "\"id\": \"";
        var i = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, "no id field found in:\n" + json);
        var start = i + marker.Length;
        var end = json.IndexOf('"', start);
        return json.Substring(start, end - start);
    }

    private static string IdForTitle(string json, string title)
    {
        // Items in list --json are an array; walk in order, pair id→title.
        const string idMarker = "\"id\": \"";
        const string titleMarker = "\"title\": \"";
        var ids = new List<string>();
        var pos = 0;
        while ((pos = json.IndexOf(idMarker, pos, StringComparison.Ordinal)) >= 0)
        {
            pos += idMarker.Length;
            var end = json.IndexOf('"', pos);
            ids.Add(json.Substring(pos, end - pos));
            pos = end;
        }
        pos = 0; var idx = 0;
        while ((pos = json.IndexOf(titleMarker, pos, StringComparison.Ordinal)) >= 0)
        {
            pos += titleMarker.Length;
            var end = json.IndexOf('"', pos);
            var t = json.Substring(pos, end - pos);
            if (t == title) return ids[idx];
            pos = end;
            idx++;
        }
        throw new Xunit.Sdk.XunitException($"title '{title}' not found in list --json output:\n{json}");
    }
}
