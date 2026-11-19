using System.Text.Json;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SmokeTests
{
    [Fact]
    public void Init_creates_knit_dir_with_config_and_templates()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive");

        Assert.Equal(0, r.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(root.Path, ".knit")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "config.json")));
        // Bucket folders are created by the first write, not by init.
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit", "items")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, ".knit", "templates")));
        var templates = Directory.GetFiles(Path.Combine(root.Path, ".knit", "templates"), "*.md");
        Assert.NotEmpty(templates);
    }

    [Fact]
    public void Init_fails_in_non_interactive_without_handle()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit")));
    }

    [Fact]
    public void Add_then_list_json_includes_the_new_item()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI Bot", "--non-interactive").ExitCode);

        Assert.Equal(0, root.Run("add", "feature", "smoke test").ExitCode);
        var list = root.Run("list", "--json");

        Assert.Equal(0, list.ExitCode);
        Assert.Contains("\"title\": \"smoke test\"", list.Stdout);
        Assert.Contains("\"type\": \"feature\"", list.Stdout);
    }

    [Fact]
    public void Start_then_done_transitions_state()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "do the thing").ExitCode);

        var listed = root.Run("list", "--json");
        var id = ExtractFirstId(listed.Stdout);

        Assert.Equal(0, root.Run("start", id).ExitCode);
        var afterStart = root.Run("list", "--json");
        Assert.Contains("\"state\": \"in-progress\"", afterStart.Stdout);
        Assert.Contains("\"assignee\": \"alice\"", afterStart.Stdout);

        Assert.Equal(0, root.Run("done", id).ExitCode);
        var afterDone = root.Run("list", "--json");
        Assert.Contains("\"state\": \"done\"", afterDone.Stdout);

        Assert.Equal(0, root.Run("reopen", id).ExitCode);
        var afterReopen = root.Run("list", "--json");
        Assert.Contains("\"state\": \"todo\"", afterReopen.Stdout);
    }

    [Fact]
    public void Block_marks_blocked_and_hides_from_ready()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocker").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocked one").ExitCode);

        var ids = ExtractAllIds(root.Run("list", "--json").Stdout);
        Assert.Equal(2, ids.Count);
        // list ordering is not guaranteed by title — match by title
        var (blockerId, blockedId) = MatchIdsByTitle(root.Run("list", "--json").Stdout, "blocker", "blocked one");

        var blockResult = root.Run("block", blockedId, "--by", blockerId);
        Assert.Equal(0, blockResult.ExitCode);

        // State is untouched; the orthogonal `blocked` flag carries the reason.
        var afterBlock = root.Run("list", "--json");
        Assert.DoesNotContain("\"state\": \"blocked\"", afterBlock.Stdout);
        var showBlocked = root.Run("show", "--json", blockedId);
        Assert.Contains("\"blocked\":", showBlocked.Stdout);

        var ready = root.Run("ready", "--json");
        // ready filter excludes items with the blocked flag set; the blocker
        // (todo, unblocked) is still ready and its link target_text shows
        // "blocked one" so check by title field, not raw substring.
        Assert.DoesNotContain("\"title\": \"blocked one\"", ready.Stdout);
        Assert.Contains("\"title\": \"blocker\"", ready.Stdout);

        // Unblock resolves every impediment — the Blocked-by row goes with it.
        Assert.Equal(0, root.Run("unblock", blockedId, "--json").ExitCode);
        var afterUnblock = root.Run("show", "--json", blockedId);
        Assert.DoesNotContain("\"blocked\":", afterUnblock.Stdout);
        Assert.DoesNotContain("\"rel\": \"Blocked by\"", afterUnblock.Stdout);
    }

    [Fact]
    public void Unlinking_the_last_blocker_unblocks_an_item_without_a_reason()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocker").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocked one").ExitCode);
        var (blockerId, blockedId) = MatchIdsByTitle(root.Run("list", "--json").Stdout, "blocker", "blocked one");
        Assert.Equal(0, root.Run("block", blockedId, "--by", blockerId).ExitCode);

        var unlink = root.Run("unlink", blockedId, blockerId);
        Assert.Equal(0, unlink.ExitCode);
        Assert.Contains("unblocked", unlink.Stdout);
        Assert.DoesNotContain("\"blocked\":", root.Run("show", "--json", blockedId).Stdout);
    }

    [Fact]
    public void A_reason_given_with_a_blocker_lives_on_the_link_and_goes_with_it()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocker").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocked one").ExitCode);
        var (blockerId, blockedId) = MatchIdsByTitle(root.Run("list", "--json").Stdout, "blocker", "blocked one");
        Assert.Equal(0, root.Run("block", blockedId, "--by", blockerId, "--reason", "needs its API | v2").ExitCode);

        var shown = JsonDocument.Parse(root.Run("show", "--json", blockedId).Stdout).RootElement;
        Assert.Equal("blocked by blocker — needs its API | v2", shown.GetProperty("blocked").GetString());
        var row = shown.GetProperty("links").EnumerateArray().Single(l => l.GetProperty("rel").GetString() == "Blocked by");
        Assert.Equal("needs its API | v2", row.GetProperty("notes").GetString());

        // Same blocker again replaces the note.
        var again = root.Run("block", blockedId, "--by", blockerId, "--reason", "needs its API v3", "--json");
        Assert.Equal("needs its API | v2", JsonDocument.Parse(again.Stdout).RootElement.GetProperty("replaced_reason").GetString());

        var unlink = root.Run("unlink", blockedId, blockerId);
        Assert.Equal(0, unlink.ExitCode);
        Assert.Contains("unblocked", unlink.Stdout);
        Assert.DoesNotContain("needs its API", root.Run("show", "--json", blockedId).Stdout);
    }

    [Fact]
    public void Relinking_edits_the_note_set_keep_replace_clear()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "other").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "mine").ExitCode);
        var (otherId, mineId) = MatchIdsByTitle(root.Run("list", "--json").Stdout, "other", "mine");

        string? Note()
        {
            var links = JsonDocument.Parse(root.Run("show", "--json", mineId).Stdout).RootElement.GetProperty("links");
            var row = links.EnumerateArray().Single(l => l.GetProperty("rel").GetString() == "Related");
            return row.TryGetProperty("notes", out var n) ? n.GetString() : null;
        }

        Assert.Equal(0, root.Run("link", mineId, "--related", otherId, "--note", "same area").ExitCode);
        Assert.Equal("same area", Note());
        Assert.Equal(0, root.Run("link", mineId, "--related", otherId).ExitCode);
        Assert.Equal("same area", Note());
        Assert.Equal(0, root.Run("link", mineId, "--related", otherId, "--note", "shared schema").ExitCode);
        Assert.Equal("shared schema", Note());
        Assert.Equal(0, root.Run("link", mineId, "--related", otherId, "--note", "").ExitCode);
        Assert.Null(Note());
    }

    [Fact]
    public void A_free_text_reason_survives_its_blockers()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocker").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "blocked one").ExitCode);
        var (blockerId, blockedId) = MatchIdsByTitle(root.Run("list", "--json").Stdout, "blocker", "blocked one");
        Assert.Equal(0, root.Run("block", blockedId, "--reason", "waiting on legal").ExitCode);

        var byBlocker = root.Run("block", blockedId, "--by", blockerId);
        Assert.Equal(0, byBlocker.ExitCode);
        Assert.DoesNotContain("Replaced previous reason", byBlocker.Stdout);

        var unlink = root.Run("unlink", blockedId, blockerId);
        Assert.Equal(0, unlink.ExitCode);
        Assert.Contains("still blocked", unlink.Stdout);
        Assert.Contains("waiting on legal", unlink.Stdout);
        Assert.Contains("knit unblock", unlink.Stdout);
        Assert.Contains("\"blocked\": \"waiting on legal\"", root.Run("show", "--json", blockedId).Stdout);
    }

    [Fact]
    public void Dismiss_sets_state_dismissed()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "to abandon").ExitCode);
        var id = ExtractFirstId(root.Run("list", "--json").Stdout);
        Assert.Equal(0, root.Run("dismiss", id).ExitCode);
        var show = root.Run("show", "--json", id);
        Assert.Contains("\"state\": \"dismissed\"", show.Stdout);
    }

    [Fact]
    public void Set_state_blocked_is_rejected()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "a task").ExitCode);
        var id = ExtractFirstId(root.Run("list", "--json").Stdout);
        var setResult = root.Run("set", id, "state=blocked");
        Assert.NotEqual(0, setResult.ExitCode);
        Assert.Contains("no longer a valid state", setResult.Stderr);
    }

    private static string ExtractFirstId(string json)
    {
        var marker = "\"id\": \"";
        var i = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, "no id field found in:\n" + json);
        var start = i + marker.Length;
        var end = json.IndexOf('"', start);
        return json.Substring(start, end - start);
    }

    private static List<string> ExtractAllIds(string json)
    {
        var ids = new List<string>();
        var marker = "\"id\": \"";
        var pos = 0;
        while ((pos = json.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
        {
            pos += marker.Length;
            var end = json.IndexOf('"', pos);
            ids.Add(json.Substring(pos, end - pos));
            pos = end;
        }
        return ids;
    }

    private static (string, string) MatchIdsByTitle(string json, string firstTitle, string secondTitle)
    {
        // crude pairing: scan items in order, capture id immediately preceding the matching title
        var ids = ExtractAllIds(json);
        string? first = null, second = null;
        var titleMarker = "\"title\": \"";
        var pos = 0; var idx = 0;
        while ((pos = json.IndexOf(titleMarker, pos, StringComparison.Ordinal)) >= 0)
        {
            pos += titleMarker.Length;
            var end = json.IndexOf('"', pos);
            var title = json.Substring(pos, end - pos);
            if (title == firstTitle) first = ids[idx];
            else if (title == secondTitle) second = ids[idx];
            pos = end;
            idx++;
        }
        Assert.NotNull(first); Assert.NotNull(second);
        return (first!, second!);
    }
}
