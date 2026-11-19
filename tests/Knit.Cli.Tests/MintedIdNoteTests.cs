namespace Knit.Cli.Tests;

/// <summary>
/// Slugification silently reshaped minted ids, so an agent that
/// asked for one id got another with no way to notice. The note goes to
/// stderr (stdout stays capture-clean for <c>$(knit ...)</c>) and to
/// <c>--json</c> as fields, and stays quiet when only lowercasing and spacing
/// changed.
/// </summary>
[Collection(nameof(CliCollection))]
public class MintedIdNoteTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Kb_source_add_reports_a_truncated_id_on_stderr()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var raw = new string('x', 60);

        var r = root.Run("kb", "add", "source", raw, "--url", "https://example.com");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("note:", r.Stderr);
        Assert.Contains("truncated", r.Stderr);
        // The id the caller has to use is still the one on stdout.
        Assert.Contains(new string('x', 40), r.Stdout);
    }

    [Fact]
    public void Kb_source_add_json_carries_the_change_as_fields()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "source", "RFC 7231 §6.5.1", "--url", "https://example.com", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"id_trimmed\": true", r.Stdout);
        Assert.Contains("\"id_input\"", r.Stdout);
        Assert.Contains("\"id_reason\"", r.Stdout);
    }

    [Fact]
    public void Plain_slugification_stays_silent()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "source", "Rate Limit Spec", "--url", "https://example.com");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
    }

    [Fact]
    public void Kb_adr_add_reports_folded_accents()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("kb", "add", "adr", "Übergang to queues");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("folded accents", r.Stderr);
    }

    [Fact]
    public void Slate_add_reports_stripped_punctuation()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("slate", "add", "q3/hardening");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("note:", r.Stderr);
        Assert.Contains("q3-hardening", r.Stdout);
    }
}
