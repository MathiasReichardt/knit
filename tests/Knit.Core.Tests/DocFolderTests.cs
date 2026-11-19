using System.Diagnostics;
using Knit.Core.DocFolders;

namespace Knit.Core.Tests;

public sealed class DocFolderTempDir : IDisposable
{
    public string Root { get; }

    public DocFolderTempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), $"knit-docfolder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Base(params string[] parts)
    {
        var dir = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

public class DocFolderTests
{
    [Fact]
    public void Resolve_prefers_flat_then_folder_then_null()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");

        Assert.Null(DocFolder.Resolve(baseDir, "missing"));

        var folderReadme = DocFolder.FolderReadmePath(baseDir, "grown");
        Directory.CreateDirectory(Path.GetDirectoryName(folderReadme)!);
        File.WriteAllText(folderReadme, "# grown\n");
        Assert.Equal(folderReadme, DocFolder.Resolve(baseDir, "grown"));

        var flat = DocFolder.FlatPath(baseDir, "flat");
        File.WriteAllText(flat, "# flat\n");
        Assert.Equal(flat, DocFolder.Resolve(baseDir, "flat"));
    }

    [Fact]
    public void IsFolderForm_keys_on_readme_filename()
    {
        Assert.True(DocFolder.IsFolderForm(Path.Combine("x", "grown", "readme.md")));
        Assert.False(DocFolder.IsFolderForm(Path.Combine("x", "flat.md")));
    }
}

public class DocFolderDeleteTests
{
    [Fact]
    public void Flat_entry_deletes_the_file()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var file = DocFolder.FlatPath(baseDir, "note");
        File.WriteAllText(file, "# note\n");

        var plan = DocFolderDelete.Plan(baseDir, file).GetValueOrThrow();
        Assert.False(plan.IsFolderForm);
        Assert.Equal(new[] { file }, plan.MarkdownTargets);

        var deleted = DocFolderDelete.Execute(baseDir, plan, deleteFolder: false).GetValueOrThrow();
        Assert.True(deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Folder_entry_with_no_blockers_deletes_without_deleteFolder()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var readme = DocFolder.FolderReadmePath(baseDir, "grown");
        Directory.CreateDirectory(Path.GetDirectoryName(readme)!);
        File.WriteAllText(readme, "# grown\n");
        // A generated index is part of the entry, not a blocker.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(readme)!, DocFolder.IndexFileName), "toc\n");

        var plan = DocFolderDelete.Plan(baseDir, readme).GetValueOrThrow();
        Assert.True(plan.IsFolderForm);
        Assert.Empty(plan.Blockers);

        var deleted = DocFolderDelete.Execute(baseDir, plan, deleteFolder: false).GetValueOrThrow();
        Assert.True(deleted);
        Assert.False(Directory.Exists(Path.GetDirectoryName(readme)!));
    }

    [Fact]
    public void Folder_entry_with_blockers_needs_deleteFolder()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var folder = Path.GetDirectoryName(DocFolder.FolderReadmePath(baseDir, "grown"))!;
        Directory.CreateDirectory(folder);
        var readme = Path.Combine(folder, DocFolder.ReadmeFileName);
        File.WriteAllText(readme, "# grown\n");
        var sub = Path.Combine(folder, "attachment.md");
        File.WriteAllText(sub, "# attachment\n");

        var plan = DocFolderDelete.Plan(baseDir, readme).GetValueOrThrow();
        Assert.True(plan.IsFolderForm);
        Assert.Single(plan.Blockers);
        Assert.Equal(sub, plan.Blockers[0]);
        Assert.Equal(2, plan.MarkdownTargets.Count); // readme + attachment

        // Without deleteFolder → refused, nothing touched.
        var refused = DocFolderDelete.Execute(baseDir, plan, deleteFolder: false).GetValueOrThrow();
        Assert.False(refused);
        Assert.True(Directory.Exists(folder));

        // With deleteFolder → gone.
        var deleted = DocFolderDelete.Execute(baseDir, plan, deleteFolder: true).GetValueOrThrow();
        Assert.True(deleted);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Plan_refuses_a_target_outside_the_containment_root()
    {
        using var tmp = new DocFolderTempDir();
        var inside = tmp.Base("inside");
        var outside = tmp.Base("outside");
        var stray = Path.Combine(outside, "stray.md");
        File.WriteAllText(stray, "x\n");

        var result = DocFolderDelete.Plan(inside, stray);
        Assert.True(result.IsError);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public void Plan_refuses_a_folder_containing_a_symlink()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var folder = Path.GetDirectoryName(DocFolder.FolderReadmePath(baseDir, "grown"))!;
        Directory.CreateDirectory(folder);
        var readme = Path.Combine(folder, DocFolder.ReadmeFileName);
        File.WriteAllText(readme, "# grown\n");

        var outside = tmp.Base("outside");
        var link = Path.Combine(folder, "escape");
        if (!TryCreateDirLink(link, outside)) return; // platform can't link — skip

        var result = DocFolderDelete.Plan(baseDir, readme);
        Assert.True(result.IsError);
        Assert.True(Directory.Exists(folder));
    }

    private static bool TryCreateDirLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                p!.WaitForExit();
                return p.ExitCode == 0 && Directory.Exists(link);
            }
            catch { return false; }
        }
    }
}

public class DocFolderMoveTests
{
    [Fact]
    public void Flat_move_relocates_the_file()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var old = DocFolder.FlatPath(baseDir, "old");
        File.WriteAllText(old, "# old\n");
        var @new = DocFolder.FlatPath(baseDir, "new");

        var result = DocFolderMove.Move(old, @new);
        Assert.False(result.WasFolderForm);
        Assert.Empty(result.SubDocs);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(@new));
    }

    [Fact]
    public void Folder_move_relocates_the_folder_and_reports_subdocs()
    {
        using var tmp = new DocFolderTempDir();
        var baseDir = tmp.Base("docs");
        var oldFolder = Path.GetDirectoryName(DocFolder.FolderReadmePath(baseDir, "old"))!;
        Directory.CreateDirectory(oldFolder);
        var oldReadme = Path.Combine(oldFolder, DocFolder.ReadmeFileName);
        File.WriteAllText(oldReadme, "# old\n");
        var sub = Path.Combine(oldFolder, "sub.md");
        File.WriteAllText(sub, "# sub\n");
        // Generated index must not surface as a sub-doc.
        File.WriteAllText(Path.Combine(oldFolder, DocFolder.IndexFileName), "toc\n");

        var newReadme = DocFolder.FolderReadmePath(baseDir, "new");
        var result = DocFolderMove.Move(oldReadme, newReadme);

        Assert.True(result.WasFolderForm);
        var moved = Assert.Single(result.SubDocs);
        Assert.Equal("sub", moved.RelativeSuffix);
        Assert.Equal(sub, moved.OldPath);
        Assert.True(File.Exists(moved.NewPath));
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(File.Exists(newReadme));
    }
}
