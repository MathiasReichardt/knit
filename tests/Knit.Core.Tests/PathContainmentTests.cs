using Knit.Core.Paths;

namespace Knit.Core.Tests;

public class PathContainmentTests
{
    // Build an absolute, canonicalised path the way the endpoints do.
    private static string Full(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    [Fact]
    public void Root_itself_is_contained()
    {
        var root = Full(Path.GetTempPath(), "knit-root");
        Assert.True(PathContainment.IsContained(root, root));
    }

    [Fact]
    public void Child_is_contained()
    {
        var root = Full(Path.GetTempPath(), "knit-root");
        var child = Full(root, "kb", "adr", "index.md");
        Assert.True(PathContainment.IsContained(root, child));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("../sibling")]
    [InlineData("kb/../../escape")]
    public void Dotdot_escape_is_rejected(string rel)
    {
        var root = Full(Path.GetTempPath(), "knit-root");
        var candidate = Full(root, rel);
        Assert.False(PathContainment.IsContained(root, candidate));
    }

    [Fact]
    public void Sibling_with_shared_prefix_is_rejected()
    {
        // "knit-root-evil" must not pass as a child of "knit-root".
        var root = Full(Path.GetTempPath(), "knit-root");
        var sibling = Full(Path.GetTempPath(), "knit-root-evil", "secret");
        Assert.False(PathContainment.IsContained(root, sibling));
    }

    [Fact]
    public void Plain_nested_path_has_no_symlink()
    {
        using var root = new TempDir();
        var child = Path.Combine(root.Path, "kb", "adr");
        Directory.CreateDirectory(child);
        Assert.False(PathContainment.HasSymlinkBelow(root.Path, Path.Combine(child, "index.md")));
    }

    [Fact]
    public void Root_itself_is_not_inspected()
    {
        using var root = new TempDir();
        Assert.False(PathContainment.HasSymlinkBelow(root.Path, root.Path));
    }

    [Fact]
    public void Symlinked_folder_is_detected()
    {
        using var outside = new TempDir();
        using var root = new TempDir();
        var link = Path.Combine(root.Path, "evil");
        // Conditional skip: Windows denies symlink creation without privilege /
        // Developer Mode. xUnit v2 has no dynamic skip, so bail as a no-op pass;
        // the check still runs wherever symlinks can be created (CI, *nix, dev mode).
        if (!TryCreateDirSymlink(link, outside.Path))
            return;

        // A file *through* the symlinked folder must be flagged even though
        // the lexical path (root/evil/secret.md) is contained in root.
        var through = Path.Combine(link, "secret.md");
        Assert.True(PathContainment.IsContained(root.Path, through));
        Assert.True(PathContainment.HasSymlinkBelow(root.Path, through));
    }

    private static bool TryCreateDirSymlink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "knit-pc-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
