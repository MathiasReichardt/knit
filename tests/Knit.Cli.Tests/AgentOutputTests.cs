using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// Agent-usage polish: --json on mutators, pipe-clean stdout,
/// adaptive short id rendering. Ids are 18-char NanoIDs (upper+digits).
/// </summary>
[Collection(nameof(CliCollection))]
public class AgentOutputTests
{
    [Fact]
    public void Add_json_emits_full_id_and_no_ansi_escapes()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);

        var add = root.Run("add", "feature", "alpha", "--json");
        Assert.Equal(0, add.ExitCode);
        Assert.DoesNotContain("\x1b", add.Stdout);

        using var doc = JsonDocument.Parse(add.Stdout);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(18, id.Length);
        Assert.Matches("^[0-9A-Z]{18}$", id);
        Assert.Equal("feature", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("todo", doc.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void Mutator_chain_via_json_round_trips()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);

        var add = root.Run("add", "task", "round trip", "--json");
        var id = JsonDocument.Parse(add.Stdout).RootElement.GetProperty("id").GetString()!;

        var started = root.Run("start", id, "--json");
        Assert.Equal(0, started.ExitCode);
        Assert.Equal("in-progress",
            JsonDocument.Parse(started.Stdout).RootElement.GetProperty("state").GetString());

        var doneR = root.Run("done", id, "--json");
        Assert.Equal(0, doneR.ExitCode);
        Assert.Equal("done",
            JsonDocument.Parse(doneR.Stdout).RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void Piped_human_output_contains_no_ansi_escapes()
    {
        // Console.IsOutputRedirected is true in this in-process test (we
        // SetOut to a StringWriter), so styling should auto-disable.
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "feature", "no escapes please").ExitCode);

        var listed = root.Run("list");
        Assert.Equal(0, listed.ExitCode);
        Assert.DoesNotContain("\x1b", listed.Stdout);
        Assert.Contains("no escapes please", listed.Stdout);
    }

    [Fact]
    public void Short_id_in_human_output_meets_floor()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "feature", "one").ExitCode);
        Assert.Equal(0, root.Run("add", "feature", "two").ExitCode);

        var listed = root.Run("list");
        // Each row begins with the short id; NanoID floor is 4, display
        // lowercases for calm. We don't assert exact length (could be longer
        // on collision), only that a short id-shaped blob shows up.
        Assert.Matches(@"[0-9a-z]{4}", listed.Stdout);
    }

    [Fact]
    public void Error_in_json_mode_goes_to_stderr_as_json()
    {
        using var root = new TempKnitRoot();
        // No init — `add` should fail with json-shaped error on stderr.
        var r = root.Run("add", "task", "x", "--json");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Equal("", r.Stdout); // no stdout on error
        Assert.DoesNotContain("\x1b", r.Stderr);
        using var doc = JsonDocument.Parse(r.Stderr);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }
}
