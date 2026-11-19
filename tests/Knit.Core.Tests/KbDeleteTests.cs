using System.Diagnostics;
using Knit.Core.Kb;
using Knit.Core.Refs;

namespace Knit.Core.Tests;

public class KbDeleteContainmentTests
{
    private sealed class NoopRemover : ItemRefRemover
    {
        public IReadOnlyList<string> RemoveKbRefs(IReadOnlyCollection<string> oldTargetPaths) => [];
        public IReadOnlyList<string> FindItemsReferencing(IReadOnlyCollection<string> oldTargetPaths) => [];
    }

    // A directory reparse point pointing outside the repo. Symlinks need
    // Developer Mode / root on Windows, so fall back to a junction (no
    // privilege required) — the guard keys on the ReparsePoint attribute,
    // which both carry. Returns false when neither could be created.
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

    [Fact]
    public void Delete_refuses_folder_form_entry_containing_a_symlink()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"knit-del-{Guid.NewGuid():N}");
        var root = Path.Combine(tmp, "repo");
        var outside = Path.Combine(tmp, "outside");
        try
        {
            Directory.CreateDirectory(outside);
            var secret = Path.Combine(outside, "secret.md");
            File.WriteAllText(secret, "do-not-delete\n");

            var fooDir = Path.Combine(root, ".knit", "kb", "features", "foo");
            Directory.CreateDirectory(fooDir);
            File.WriteAllText(Path.Combine(fooDir, "readme.md"),
                "---\ntitle: Foo\nslug: foo\n---\n\nBody.\n");

            // Directory reparse point inside the entry's folder pointing
            // outside the repo. If the platform refuses both a symlink and a
            // junction, we can't exercise the guard, so skip cleanly.
            var link = Path.Combine(fooDir, "escape");
            if (!TryCreateDirLink(link, outside)) return;

            var result = KbDelete.Delete(root, new Ref(Bucket.Feature, "foo"),
                deleteFolder: true, new NoopRemover());

            Assert.True(result.IsError, "delete must refuse a folder containing a symlink/junction");
            Assert.True(File.Exists(secret), "outside file must survive");
            Assert.True(Directory.Exists(fooDir), "entry folder must be left intact on refusal");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
