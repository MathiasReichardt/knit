using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>Branch</c> / <c>Repo</c> / <c>PR</c> rows on items and KB
/// entries, plus <c>Commit</c>, which alone doesn't inherit. The driving case is worktrees — pointing at the branch an item is
/// being worked on — so the target lives outside the repo and nothing here
/// resolves to a file.
/// </summary>
[Collection(nameof(CliCollection))]
public class ExternalLinkTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string Add(TempKnitRoot root, params string[] args)
    {
        var r = root.Run([.. args, "--json"]);
        Assert.True(r.ExitCode == 0, $"add failed: {r.Stderr}");
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("id").GetString()!;
    }

    private static JsonElement Links(TempKnitRoot root, string id)
    {
        var r = root.Run("show", id, "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("links").Clone();
    }

    [Fact]
    public void A_branch_row_round_trips_and_carries_no_path()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        Assert.Equal(0, root.Run("link", id, "--branch", "feature/checkout").ExitCode);

        var link = Links(root, id).EnumerateArray().Single();
        Assert.Equal("Branch", link.GetProperty("rel").GetString());
        Assert.Equal("feature/checkout", link.GetProperty("target_text").GetString());
        Assert.Equal("", link.GetProperty("target_path").GetString());
    }

    [Fact]
    public void A_pr_is_displayed_short_and_keeps_the_url()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        Assert.Equal(0, root.Run("link", id, "--pr", "https://github.com/org/repo/pull/42").ExitCode);

        var link = Links(root, id).EnumerateArray().Single();
        Assert.Equal("PR", link.GetProperty("rel").GetString());
        Assert.Equal("org/repo#42", link.GetProperty("target_text").GetString());
        Assert.Equal("https://github.com/org/repo/pull/42", link.GetProperty("target_path").GetString());
    }

    [Fact]
    public void Several_rows_of_one_rel_are_legitimate()
    {
        // Work splits across branches; a feature gets a follow-up PR.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        Assert.Equal(0, root.Run("link", id, "--branch", "feature/one").ExitCode);
        Assert.Equal(0, root.Run("link", id, "--branch", "feature/two").ExitCode);

        Assert.Equal(2, Links(root, id).GetArrayLength());
    }

    [Fact]
    public void The_same_target_twice_does_not_duplicate_the_row()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        Assert.Equal(0, root.Run("link", id, "--branch", "feature/one").ExitCode);
        Assert.Equal(0, root.Run("link", id, "--branch", "feature/one", "--note", "second thought").ExitCode);

        var link = Links(root, id).EnumerateArray().Single();
        Assert.Equal("second thought", link.GetProperty("notes").GetString());
    }

    [Fact]
    public void Unlink_matches_the_displayed_target_and_the_url()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");
        Assert.Equal(0, root.Run("link", id, "--branch", "feature/one").ExitCode);
        Assert.Equal(0, root.Run("link", id, "--pr", "https://github.com/org/repo/pull/42").ExitCode);

        Assert.Equal(0, root.Run("unlink", id, "feature/one").ExitCode);
        Assert.Equal(0, root.Run("unlink", id, "https://github.com/org/repo/pull/42").ExitCode);

        Assert.Equal(0, Links(root, id).GetArrayLength());
    }

    [Fact]
    public void Verify_stays_silent_on_external_rows()
    {
        // Nothing here is a path in this repo, so a path scan must not claim
        // they are broken targets.
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");
        Assert.Equal(0, root.Run("link", id, "--branch", "feature/one").ExitCode);
        Assert.Equal(0, root.Run("link", id, "--pr", "https://github.com/org/repo/pull/42").ExitCode);

        var r = root.Run("verify");

        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("broken", r.Stdout);
    }

    [Fact]
    public void A_pr_target_that_is_not_a_url_is_refused()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        var r = root.Run("link", id, "--pr", "org/repo#42");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("URL", r.Stderr);
        Assert.Equal(0, Links(root, id).GetArrayLength());
    }

    [Fact]
    public void A_repo_target_resolves_against_the_declared_mounts()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");
        var unknown = root.Run("link", id, "--repo", "company-docs");
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Contains("not a declared mount", unknown.Stderr);

        Assert.Equal(0, root.Run("mount", "add", "company-docs", ".").ExitCode);

        Assert.Equal(0, root.Run("link", id, "--repo", "company-docs").ExitCode);
        Assert.Equal("company-docs", Links(root, id).EnumerateArray().Single()
            .GetProperty("target_text").GetString());
    }

    [Fact]
    public void A_git_url_needs_no_mount()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        Assert.Equal(0, root.Run("link", id, "--repo", "git@host:org/x.git").ExitCode);

        Assert.Equal("git@host:org/x.git", Links(root, id).EnumerateArray().Single()
            .GetProperty("target_text").GetString());
    }

    [Fact]
    public void Two_link_kinds_at_once_are_refused()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "task", "Rewrite checkout");

        var r = root.Run("link", id, "--branch", "one", "--repo", "git@host:org/x.git");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("exactly one", r.Stderr);
    }

    [Fact]
    public void Context_reports_external_refs_and_inherits_them_from_the_parent()
    {
        // The branch a parent feature is built on is the branch its tasks
        // belong on too.
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Checkout");
        var task = Add(root, "add", "task", "Payment form", "--parent", epic);
        Assert.Equal(0, root.Run("link", epic, "--branch", "feature/checkout").ExitCode);
        Assert.Equal(0, root.Run("link", task, "--pr", "https://github.com/org/repo/pull/42").ExitCode);

        var r = root.Run("context", task, "--json");

        Assert.Equal(0, r.ExitCode);
        var refs = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("external_refs").EnumerateArray().ToList();
        Assert.Equal(2, refs.Count);
        Assert.Equal("org/repo#42", refs[0].GetProperty("target").GetString());
        Assert.Equal(JsonValueKind.Null, refs[0].GetProperty("inherited_from").ValueKind);
        Assert.Equal("feature/checkout", refs[1].GetProperty("target").GetString());
        Assert.Equal($"item:{epic}", refs[1].GetProperty("inherited_from").GetString());
        Assert.Equal(JsonValueKind.Null, refs[1].GetProperty("url").ValueKind);
    }

    [Fact]
    public void A_kb_entry_carries_the_same_rows()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("kb", "add", "doc", "Payment provider").ExitCode);

        Assert.Equal(0, root.Run("kb", "link", "doc:payment-provider", "--branch", "spike/provider").ExitCode);

        var shown = root.Run("kb", "show", "doc:payment-provider");
        Assert.Equal(0, shown.ExitCode);
        Assert.Contains("spike/provider", shown.Stdout);
        Assert.Equal(0, root.Run("verify").ExitCode);

        Assert.Equal(0, root.Run("kb", "unlink", "doc:payment-provider", "spike/provider").ExitCode);
        Assert.DoesNotContain("spike/provider", root.Run("kb", "show", "doc:payment-provider").Stdout);
    }

    [Fact]
    public void Unlinking_a_kb_row_that_is_not_there_is_refused()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("kb", "add", "doc", "Payment provider").ExitCode);

        var r = root.Run("kb", "unlink", "doc:payment-provider", "spike/provider");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no branch / repo / PR / commit row", r.Stderr);
    }

    [Fact]
    public void A_commit_is_displayed_short_and_unlinks_by_either_form()
    {
        const string url = "https://github.com/org/repo/commit/abcdef0123456789";
        using var root = NewRepo();
        var id = Add(root, "add", "bug", "Crash on empty cart");

        Assert.Equal(0, root.Run("link", id, "--commit", url).ExitCode);

        var link = Links(root, id).EnumerateArray().Single();
        Assert.Equal("Commit", link.GetProperty("rel").GetString());
        Assert.Equal("org/repo@abcdef0", link.GetProperty("target_text").GetString());
        Assert.Equal(url, link.GetProperty("target_path").GetString());
        Assert.Equal(0, root.Run("verify").ExitCode);

        Assert.Equal(0, root.Run("unlink", id, "org/repo@abcdef0").ExitCode);
        Assert.Equal(0, Links(root, id).GetArrayLength());
        Assert.Equal(0, root.Run("link", id, "--commit", url).ExitCode);
        Assert.Equal(0, root.Run("unlink", id, url).ExitCode);
        Assert.Equal(0, Links(root, id).GetArrayLength());
    }

    [Fact]
    public void A_bare_sha_is_stored_as_written_and_unlinks_by_it()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "bug", "Crash on empty cart");

        Assert.Equal(0, root.Run("link", id, "--commit", "abcdef0123").ExitCode);

        var link = Links(root, id).EnumerateArray().Single();
        Assert.Equal("Commit", link.GetProperty("rel").GetString());
        Assert.Equal("abcdef0123", link.GetProperty("target_text").GetString());
        Assert.Equal("", link.GetProperty("target_path").GetString());
        Assert.Equal(0, root.Run("verify").ExitCode);

        Assert.Equal(0, root.Run("unlink", id, "abcdef0123").ExitCode);
        Assert.Equal(0, Links(root, id).GetArrayLength());
    }

    [Fact]
    public void A_commit_that_is_neither_sha_nor_url_is_refused()
    {
        using var root = NewRepo();
        var id = Add(root, "add", "bug", "Crash on empty cart");

        var r = root.Run("link", id, "--commit", "main");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("SHA", r.Stderr);
    }

    [Fact]
    public void Context_does_not_inherit_a_parents_commit()
    {
        // A fix commit belongs to the item that names it, not its subtree.
        using var root = NewRepo();
        var epic = Add(root, "add", "epic", "Checkout");
        var task = Add(root, "add", "task", "Payment form", "--parent", epic);
        Assert.Equal(0, root.Run("link", epic, "--commit", "https://github.com/org/repo/commit/abcdef0123").ExitCode);
        Assert.Equal(0, root.Run("link", epic, "--branch", "feature/checkout").ExitCode);
        Assert.Equal(0, root.Run("link", task, "--commit", "https://github.com/org/repo/commit/1234567abc").ExitCode);

        var r = root.Run("context", task, "--json");

        Assert.Equal(0, r.ExitCode);
        var targets = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("external_refs")
            .EnumerateArray().Select(x => x.GetProperty("target").GetString()).ToList();
        Assert.Equal(["org/repo@1234567", "feature/checkout"], targets);
    }

    [Fact]
    public void A_kb_entry_takes_a_commit_row()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("kb", "add", "doc", "Payment provider").ExitCode);

        Assert.Equal(0, root.Run("kb", "link", "doc:payment-provider", "--commit",
            "https://gitlab.com/org/repo/-/commit/abcdef0123").ExitCode);

        Assert.Contains("org/repo@abcdef0", root.Run("kb", "show", "doc:payment-provider").Stdout);
        Assert.Equal(0, root.Run("kb", "unlink", "doc:payment-provider", "org/repo@abcdef0").ExitCode);
    }
}
