using Knit.Core.Config;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

/// <summary>
/// The Branch / Repo / PR rels, plus Commit. Their target is not a file in this
/// repo, which is what the row shape and the validation both turn on.
/// </summary>
public class ExternalRefsTests
{
    private static readonly string Root = Path.GetTempPath();

    [Fact]
    public void A_branch_row_round_trips_without_a_path()
    {
        var rows = new List<LinkRow> { new(LinkRel.Branch, "feature/checkout", "", null) };

        var table = LinksTable.Render(rows);

        // Bare, not `[x](x)` — a markdown link would point at a file that
        // isn't there, and every path scan would then read it as broken.
        Assert.Contains("| Branch | feature/checkout |", table);
        var parsed = LinksTable.Parse(table);
        Assert.Equal(LinkRel.Branch, parsed[0].Rel);
        Assert.Equal("feature/checkout", parsed[0].TargetText);
        Assert.Equal("", parsed[0].TargetPath);
    }

    [Fact]
    public void A_pr_row_keeps_the_full_url_behind_the_short_display()
    {
        var rows = new List<LinkRow> { new(LinkRel.Pr, "org/repo#42", "https://github.com/org/repo/pull/42", null) };

        var parsed = LinksTable.Parse(LinksTable.Render(rows));

        Assert.Equal("org/repo#42", parsed[0].TargetText);
        Assert.Equal("https://github.com/org/repo/pull/42", parsed[0].TargetPath);
    }

    [Theory]
    [InlineData("https://github.com/org/repo/pull/42", "org/repo#42")]
    [InlineData("https://gitlab.com/org/repo/-/merge_requests/7", "org/repo#7")]
    [InlineData("https://gitea.example.com/org/repo/pulls/3", "org/repo#3")]
    [InlineData("https://example.com/whatever", "https://example.com/whatever")]
    public void A_pr_url_shortens_per_forge(string url, string expected)
        => Assert.Equal(expected, ExternalRefs.ShortenPr(url));

    [Fact]
    public void A_branch_is_stored_as_written()
    {
        // Knit never shells out to git, so existence can't be checked at all.
        Assert.True(ExternalRefs.TryResolve(LinkRel.Branch, "  feature/x ", Root, null, out var t, out _));
        Assert.Equal("feature/x", t.Text);
        Assert.Equal("", t.Path);
    }

    [Fact]
    public void A_branch_name_with_whitespace_is_refused()
    {
        Assert.False(ExternalRefs.TryResolve(LinkRel.Branch, "my branch", Root, null, out _, out var err));
        Assert.Contains("whitespace", err);
    }

    [Fact]
    public void A_pr_target_must_be_a_url()
    {
        Assert.False(ExternalRefs.TryResolve(LinkRel.Pr, "org/repo#42", Root, null, out _, out var err));
        Assert.Contains("URL", err);
    }

    [Theory]
    [InlineData("https://github.com/org/repo/commit/0123456789abcdef0123456789abcdef01234567", "org/repo@0123456")]
    [InlineData("https://gitea.example.com/org/repo/commit/abcdef1", "org/repo@abcdef1")]
    [InlineData("https://gitlab.com/group/sub/repo/-/commit/ABCDEF0123", "sub/repo@ABCDEF0")]
    [InlineData("https://bitbucket.org/org/repo/commits/abcdef0123", "org/repo@abcdef0")]
    [InlineData("https://github.com/org/repo/pull/42/commits/abcdef0123", "org/repo@abcdef0")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo/commit/abcdef0123", "https://dev.azure.com/org/proj/_git/repo/commit/abcdef0123")]
    [InlineData("https://github.com/org/repo/commit/main", "https://github.com/org/repo/commit/main")]
    [InlineData("https://github.com/org/repo/commits/main/src", "https://github.com/org/repo/commits/main/src")]
    [InlineData("https://example.com/whatever", "https://example.com/whatever")]
    public void A_commit_url_shortens_per_forge(string url, string expected)
        => Assert.Equal(expected, ExternalRefs.ShortenCommit(url));

    [Theory]
    [InlineData("abcdef0")]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    public void A_bare_sha_is_stored_as_written_and_is_not_a_link(string sha)
    {
        Assert.True(ExternalRefs.TryResolve(LinkRel.Commit, $" {sha} ", Root, null, out var t, out _));
        Assert.Equal(sha, t.Text);
        Assert.Equal("", t.Path);
    }

    [Theory]
    [InlineData("abcdef")]
    [InlineData("main")]
    [InlineData("abcdefg0123")]
    [InlineData("org/repo@abcdef0")]
    [InlineData("https://github.com/org/repo/commit/abc def")]
    public void A_commit_target_is_a_sha_or_a_url(string raw)
    {
        Assert.False(ExternalRefs.TryResolve(LinkRel.Commit, raw, Root, null, out _, out var err));
        Assert.Contains("SHA", err);
    }

    [Fact]
    public void A_sha_longer_than_sha256_is_refused()
        => Assert.False(ExternalRefs.TryResolve(LinkRel.Commit, new string('a', 65), Root, null, out _, out _));

    [Fact]
    public void A_commit_keeps_the_full_url_behind_the_short_display()
    {
        const string url = "https://github.com/org/repo/commit/abcdef0123";
        Assert.True(ExternalRefs.TryResolve(LinkRel.Commit, $" {url} ", Root, null, out var t, out _));

        var parsed = LinksTable.Parse(LinksTable.Render([new LinkRow(LinkRel.Commit, t.Text, t.Path, null)]));

        Assert.Equal(LinkRel.Commit, parsed[0].Rel);
        Assert.Equal("org/repo@abcdef0", parsed[0].TargetText);
        Assert.Equal(url, parsed[0].TargetPath);
    }

    [Fact]
    public void Every_external_rel_inherits_except_commit()
    {
        Assert.True(ExternalRefs.Inherits(LinkRel.Branch));
        Assert.True(ExternalRefs.Inherits(LinkRel.Repo));
        Assert.True(ExternalRefs.Inherits(LinkRel.Pr));
        Assert.False(ExternalRefs.Inherits(LinkRel.Commit));
        Assert.False(ExternalRefs.Inherits(LinkRel.KbRef));
    }

    [Fact]
    public void A_repo_target_may_be_a_declared_mount()
    {
        var cfg = new KnitConfig { Mounts = new Dictionary<string, string> { ["docs"] = "../company-docs" } };

        Assert.True(ExternalRefs.TryResolve(LinkRel.Repo, "docs", Root, cfg, out var t, out _));
        Assert.Equal("docs", t.Text);
        Assert.Equal("", t.Path);
    }

    [Fact]
    public void A_browsable_repo_url_is_shortened_but_keeps_its_href()
    {
        Assert.True(ExternalRefs.TryResolve(
            LinkRel.Repo, "https://github.com/botco/robots.git", Root, null, out var t, out _));
        Assert.Equal("github.com/botco/robots", t.Text);
        Assert.Equal("https://github.com/botco/robots.git", t.Path);
    }

    [Fact]
    public void An_ssh_remote_is_displayed_as_written_and_is_not_a_link()
    {
        Assert.True(ExternalRefs.TryResolve(
            LinkRel.Repo, "git@github.com:botco/robots.git", Root, null, out var t, out _));
        Assert.Equal("git@github.com:botco/robots.git", t.Text);
        Assert.Equal("", t.Path);
    }

    [Theory]
    [InlineData("git@host:org/x.git")]
    [InlineData("ssh://git@host/org/x.git")]
    [InlineData("https://github.com/org/x")]
    public void A_repo_target_may_be_a_git_url(string url)
        => Assert.True(ExternalRefs.TryResolve(LinkRel.Repo, url, Root, null, out _, out _));

    [Fact]
    public void A_repo_target_that_is_neither_mount_nor_url_nor_directory_is_refused()
    {
        Assert.False(ExternalRefs.TryResolve(LinkRel.Repo, "no-such-thing", Root, null, out _, out var err));
        Assert.Contains("not a declared mount", err);
    }

    [Fact]
    public void A_pipe_is_refused_because_it_would_split_the_row()
    {
        Assert.False(ExternalRefs.TryResolve(LinkRel.Branch, "feat|ure", Root, null, out _, out var err));
        Assert.Contains("'|'", err);
    }

    [Fact]
    public void Replace_writes_the_section_after_a_leading_h1_and_keeps_the_rest()
    {
        var body = "# Title\n\nSome prose.\n\n## Notes\n\nmore\n";

        var updated = LinksTable.Replace(body, [new LinkRow(LinkRel.Branch, "main", "", null)]);

        var lines = updated.Split('\n');
        Assert.Equal("# Title", lines[0]);
        Assert.Equal("## Links", lines[2]);
        Assert.Contains("Some prose.", updated);
        Assert.Contains("## Notes", updated);
        // A second write replaces the section rather than appending one.
        var again = LinksTable.Replace(updated, [new LinkRow(LinkRel.Branch, "other", "", null)]);
        Assert.Equal(2, again.Split("## Links").Length);
        Assert.Contains("other", again);
        Assert.DoesNotContain("| main", again);
    }
}
