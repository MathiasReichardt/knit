using Knit.Core.Discovery;

namespace Knit.Core.Tests;

public class KnitGitignoreTests : IDisposable
{
    private readonly string _root;

    public KnitGitignoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "knit-gitignore-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string GitignorePath => Path.Combine(_root, ".knit", ".gitignore");

    [Fact]
    public void Fresh_create_writes_both_entries_with_header()
    {
        RepoDiscovery.EnsureKnitGitignore(_root);

        Assert.True(File.Exists(GitignorePath));
        var content = File.ReadAllText(GitignorePath);
        Assert.Contains("config.local.json", content);
        Assert.Contains("knit-feedback.md", content);
        Assert.DoesNotContain("index.jsonl", content);
        Assert.Contains("# Managed by `knit init`", content);
    }

    [Fact]
    public void Rerun_with_both_present_is_noop()
    {
        RepoDiscovery.EnsureKnitGitignore(_root);
        var first = File.ReadAllText(GitignorePath);

        RepoDiscovery.EnsureKnitGitignore(_root);
        var second = File.ReadAllText(GitignorePath);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rerun_with_only_one_entry_appends_the_other()
    {
        File.WriteAllText(GitignorePath, "config.local.json\n");

        RepoDiscovery.EnsureKnitGitignore(_root);

        var content = File.ReadAllText(GitignorePath);
        Assert.Contains("knit-feedback.md", content);
        var localCount = content.Split('\n').Count(l => l.Trim() == "config.local.json");
        Assert.Equal(1, localCount);
    }

    [Fact]
    public void Rerun_preserves_user_content_and_appends_missing_entries()
    {
        File.WriteAllText(GitignorePath, "# my notes\nsecret.env\nlocal-cache/\n");

        RepoDiscovery.EnsureKnitGitignore(_root);

        var content = File.ReadAllText(GitignorePath);
        Assert.Contains("# my notes", content);
        Assert.Contains("secret.env", content);
        Assert.Contains("local-cache/", content);
        Assert.Contains("config.local.json", content);
        Assert.Contains("knit-feedback.md", content);
    }

    [Fact]
    public void Rerun_handles_missing_trailing_newline()
    {
        File.WriteAllText(GitignorePath, "secret.env");

        RepoDiscovery.EnsureKnitGitignore(_root);

        var content = File.ReadAllText(GitignorePath);
        Assert.Contains("secret.env\n", content);
        Assert.Contains("knit-feedback.md\n", content);
    }
}
