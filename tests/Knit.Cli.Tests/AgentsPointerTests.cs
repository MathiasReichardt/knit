namespace Knit.Cli.Tests;

/// <summary>
/// `init`'s Knit section in <c>CLAUDE.md</c> / <c>AGENTS.md</c>: one copy per
/// pre-checked file, refreshed when it differs, never added unasked.
/// </summary>
[Collection(nameof(CliCollection))]
public class AgentsPointerTests
{
    private static readonly string[] Init = ["init", "--handle", "a", "--display", "A", "--non-interactive"];

    private static string Claude(TempKnitRoot root) => Path.Combine(root.Path, "CLAUDE.md");
    private static string Agents(TempKnitRoot root) => Path.Combine(root.Path, "AGENTS.md");

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void Both_detected_files_carry_the_block_and_a_rerun_changes_nothing()
    {
        using var root = new TempKnitRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, ".claude"));
        Directory.CreateDirectory(Path.Combine(root.Path, ".codex"));

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);
        var claude = File.ReadAllText(Claude(root));
        var agents = File.ReadAllText(Agents(root));
        Assert.Contains("## Knit", claude);
        Assert.Contains(".knit/items/", agents);
        Assert.DoesNotContain("/knit`", claude);

        var r = root.Run([.. Init, "--agents-file", "project"]);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(claude, File.ReadAllText(Claude(root)));
        Assert.Equal(agents, File.ReadAllText(Agents(root)));
        Assert.Contains("already present", r.Stdout);
    }

    [Fact]
    public void A_repo_with_one_instruction_file_keeps_to_it_when_another_agent_is_detected()
    {
        using var root = new TempKnitRoot();
        File.WriteAllText(Claude(root), "# Rules\n");
        Directory.CreateDirectory(Path.Combine(root.Path, ".codex"));

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        Assert.Contains("## Knit", File.ReadAllText(Claude(root)));
        Assert.False(File.Exists(Agents(root)));
    }

    [Fact]
    public void Nothing_detected_writes_CLAUDE_md()
    {
        using var root = new TempKnitRoot();

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        Assert.Contains("## Knit", File.ReadAllText(Claude(root)));
        Assert.False(File.Exists(Agents(root)));
    }

    [Fact]
    public void Existing_file_without_a_block_gets_it_appended()
    {
        using var root = new TempKnitRoot();
        File.WriteAllText(Agents(root), "# Rules\n\nbe kind\n");

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        var text = File.ReadAllText(Agents(root));
        Assert.StartsWith("# Rules\n\nbe kind\n\n## Knit\n", text);
    }

    [Fact]
    public void An_outdated_block_is_replaced_up_to_the_next_section()
    {
        using var root = new TempKnitRoot();
        File.WriteAllText(Agents(root), "# Top\n\nintro\n\n## Knit\n\nold text\n\n## Other\n\nkeep\n");

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        var text = File.ReadAllText(Agents(root));
        Assert.StartsWith("# Top\n\nintro\n\n## Knit\n", text);
        Assert.EndsWith("workflow.\n\n## Other\n\nkeep\n", text);
        Assert.DoesNotContain("old text", text);
        Assert.Equal(1, Count(text, "## Knit"));
    }

    [Fact]
    public void An_outdated_block_at_the_end_is_replaced_and_keeps_crlf()
    {
        using var root = new TempKnitRoot();
        File.WriteAllText(Claude(root), "# Top\r\n\r\n## Knit\r\n\r\nold text\r\n");

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        var text = File.ReadAllText(Claude(root));
        Assert.DoesNotContain("old text", text);
        Assert.Contains(".knit/items/", text);
        Assert.Equal(Count(text, "\n"), Count(text, "\r\n"));
    }

    [Fact]
    public void No_tty_without_the_flag_writes_nothing_and_names_an_outdated_block()
    {
        using var root = new TempKnitRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, ".claude"));
        const string old = "## Knit\n\nold text\n";
        File.WriteAllText(Agents(root), old);

        var r = root.Run(Init);

        Assert.Equal(0, r.ExitCode);
        Assert.False(File.Exists(Claude(root)));
        Assert.Equal(old, File.ReadAllText(Agents(root)));
        Assert.Contains("Knit section outdated", r.Stdout);
    }

    [Fact]
    public void A_CLAUDE_md_importing_AGENTS_md_gets_the_block_in_AGENTS_md_only()
    {
        using var root = new TempKnitRoot();
        const string claude = "# Claude\n\n@AGENTS.md\n";
        File.WriteAllText(Claude(root), claude);

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        Assert.Equal(claude, File.ReadAllText(Claude(root)));
        Assert.Contains("## Knit", File.ReadAllText(Agents(root)));
    }

    [Fact]
    public void Reinit_adds_the_pointer_to_an_existing_repo()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run(Init).ExitCode);
        Assert.False(File.Exists(Claude(root)));

        Assert.Equal(0, root.Run([.. Init, "--agents-file", "project"]).ExitCode);

        Assert.Contains("## Knit", File.ReadAllText(Claude(root)));
    }

    [Fact]
    public void Agents_file_global_fails_with_the_valid_values_before_scaffolding()
    {
        using var root = new TempKnitRoot();

        var r = root.Run([.. Init, "--agents-file", "global"]);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("project, skip", r.Stderr);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit")));
    }

    [Fact]
    public void Agents_file_skip_writes_nothing_and_asks_nothing()
    {
        using var root = new TempKnitRoot();
        File.WriteAllText(Agents(root), "## Knit\n\nold text\n");

        var r = root.RunWithInput("", "init", "--handle", "a", "--display", "A", "--agents-file", "skip");

        Assert.Equal(0, r.ExitCode);
        Assert.False(File.Exists(Claude(root)));
        Assert.Equal("## Knit\n\nold text\n", File.ReadAllText(Agents(root)));
        Assert.DoesNotContain("Knit section", r.Stdout);
    }
}
