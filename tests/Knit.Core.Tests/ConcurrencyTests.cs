using System.Diagnostics;
using System.Text.Json.Nodes;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Data;

namespace Knit.Core.Tests;

/// <summary>
/// Atomic writes, the same-item collision guard and the repo lock
/// for multi-file mutations.
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly string _root;

    public ConcurrencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "knit-concurrency-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ".knit", "items"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly Item Basis = new()
    {
        Id = "abcd1234",
        Title = "Seed",
        Type = ItemType.Task,
        State = ItemState.Todo,
        Body = "Original body.\n",
        Tags = ["a"],
        Comments = [new Comment("c1", "ada", DateTimeOffset.UnixEpoch, "first")]
    };

    [Fact]
    public void Merge_keeps_both_writers_list_additions_and_the_other_writers_unrelated_field()
    {
        var mine = Basis with
        {
            Tags = ["a", "mine"],
            Comments = [.. Basis.Comments, new Comment("c2", "bob", DateTimeOffset.UnixEpoch, "mine")]
        };
        var theirs = Basis with
        {
            State = ItemState.Done,
            Tags = ["a", "theirs"],
            Comments = [.. Basis.Comments, new Comment("c3", "cy", DateTimeOffset.UnixEpoch, "theirs")]
        };

        var merged = ItemMerge.ThreeWay(Basis, mine, theirs)!;

        Assert.Equal(ItemState.Done, merged.State);
        Assert.Equal(["a", "theirs", "mine"], merged.Tags);
        Assert.Equal(["c1", "c3", "c2"], merged.Comments.Select(c => c.Id));
    }

    [Fact]
    public void Merge_applies_removals_and_re_races_a_field_both_changed()
    {
        var mine = Basis with { Priority = Priority.Critical, Tags = [] };
        var theirs = Basis with { Priority = Priority.Someday };

        var merged = ItemMerge.ThreeWay(Basis, mine, theirs)!;

        Assert.Equal(Priority.Critical, merged.Priority);
        Assert.Empty(merged.Tags);
    }

    [Fact]
    public void Merge_takes_a_one_sided_body_edit_and_refuses_when_both_edited_it()
    {
        var mine = Basis with { Body = "Mine.\n" };

        Assert.Equal("Mine.\n", ItemMerge.ThreeWay(Basis, mine, Basis with { State = ItemState.Done })!.Body);
        Assert.Equal("Theirs.\n", ItemMerge.ThreeWay(Basis, Basis with { State = ItemState.Done }, Basis with { Body = "Theirs.\n" })!.Body);
        Assert.Null(ItemMerge.ThreeWay(Basis, mine, Basis with { Body = "Theirs.\n" }));
    }

    [Fact]
    public void Merge_keeps_one_parent_when_both_writers_reparented()
    {
        var p0 = new LinkRow(LinkRel.Parent, "P0", "../p0.md", null);
        var basis = Basis with { LinkRows = [p0], Parent = "p0" };
        var mine = basis with { LinkRows = [new LinkRow(LinkRel.Parent, "P1", "../p1.md", null)], Parent = "p1" };
        var theirs = basis with { LinkRows = [new LinkRow(LinkRel.Parent, "P2", "../p2.md", null)], Parent = "p2" };

        var merged = ItemMerge.ThreeWay(basis, mine, theirs)!;

        Assert.Equal("p1", merged.Parent);
        Assert.Equal("P1", Assert.Single(merged.LinkRows).TargetText);
    }

    [Fact]
    public void Store_re_applies_a_field_change_onto_an_item_changed_since_it_was_read()
    {
        var store = new ItemStore(_root);
        store.Write(Basis);
        var mine = store.Read(Basis.Id)! with { Priority = Priority.Critical };

        var other = new ItemStore(_root);
        other.Write(other.Read(Basis.Id)! with { State = ItemState.Done });
        store.Write(mine);

        var result = new ItemStore(_root).Read(Basis.Id)!;
        Assert.Equal(ItemState.Done, result.State);
        Assert.Equal(Priority.Critical, result.Priority);
    }

    [Fact]
    public void Store_refuses_a_body_edit_over_another_writers_body_edit_and_writes_nothing()
    {
        var store = new ItemStore(_root);
        var file = store.Write(Basis);
        var mine = store.Read(Basis.Id)! with { Body = "Mine.\n" };

        var other = new ItemStore(_root);
        other.Write(other.Read(Basis.Id)! with { Body = "Theirs.\n" });
        var before = File.ReadAllText(file);

        var ex = Assert.Throws<WriteRefusedException>(() => store.Write(mine));
        Assert.Contains("edited its body too", ex.Message);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void Store_writes_twice_in_a_row_without_seeing_its_own_write_as_a_collision()
    {
        var store = new ItemStore(_root);
        store.Write(Basis);
        store.Write(store.Read(Basis.Id)! with { Body = "One.\n" });
        store.Write(store.Read(Basis.Id)! with { Body = "Two.\n" });

        Assert.Contains("Two.", new ItemStore(_root).Read(Basis.Id)!.Body);
    }

    [Fact]
    public void Atomic_write_replaces_a_file_a_reader_holds_open_and_leaves_no_temp()
    {
        var path = Path.Combine(_root, "f.md");
        AtomicFile.WriteAllText(path, "old");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", AtomicFile.ReadAllText(path));
        Assert.DoesNotContain(Directory.EnumerateFiles(_root), AtomicFile.IsTemp);
    }

    [Fact]
    public void Config_write_replaces_a_config_a_reader_holds_open_and_leaves_no_temp()
    {
        var knitDir = Path.Combine(_root, ".knit");
        ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName, new JsonObject { ["handle"] = "ada" });
        using (new FileStream(Path.Combine(knitDir, ConfigLoader.FileName), FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
            ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.FileName, new JsonObject { ["handle"] = "bob" });

        Assert.Equal("bob", ConfigLoader.Load(knitDir).Handle);
        Assert.DoesNotContain(Directory.EnumerateFiles(knitDir), AtomicFile.IsTemp);
    }

    [Fact]
    public void Config_with_a_bom_still_reads()
    {
        var knitDir = Path.Combine(_root, ".knit");
        File.WriteAllText(Path.Combine(knitDir, ConfigLoader.FileName), """{ "handle": "ada" }""",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal("ada", ConfigLoader.Load(knitDir).Handle);
    }

    [Fact]
    public async Task Atomic_write_retries_past_a_reader_that_blocks_the_rename_briefly()
    {
        var path = Path.Combine(_root, "f.md");
        AtomicFile.WriteAllText(path, "old");
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(60);
            await held.DisposeAsync();
        });

        AtomicFile.WriteAllText(path, "new");
        await release;

        Assert.Equal("new", AtomicFile.ReadAllText(path));
    }

    [Fact]
    public async Task Read_retries_past_a_writer_that_holds_the_file_briefly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "f.md");
        AtomicFile.WriteAllText(path, "content");
        var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(60);
            await held.DisposeAsync();
        });

        var read = AtomicFile.ReadAllText(path);
        await release;

        Assert.Equal("content", read);
    }

    [Fact]
    public void Read_of_a_file_held_past_the_retries_names_the_lock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "f.md");
        AtomicFile.WriteAllText(path, "content");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<FileLockedException>(() => AtomicFile.ReadAllText(path));

        Assert.Contains(path, ex.Message);
        Assert.Contains(FileLockedException.Reason, ex.Message);
    }

    [Fact]
    public void Lock_is_exclusive_until_released()
    {
        using (RepoLock.Acquire(_root, "move"))
        {
            var ex = Assert.Throws<WriteRefusedException>(() =>
                RepoLock.Acquire(_root, "relink", wait: TimeSpan.Zero));
            Assert.Contains($"{Environment.MachineName} (pid {Environment.ProcessId}) running 'move'", ex.Message);
        }
        Assert.False(File.Exists(RepoLock.PathFor(_root)));
        RepoLock.Acquire(_root, "relink").Dispose();
    }

    [Fact]
    public void A_lock_whose_process_is_gone_is_broken_and_reported()
    {
        WriteLock(Environment.MachineName, DeadPid(), DateTimeOffset.UtcNow);
        var notes = new List<string>();

        using (RepoLock.Acquire(_root, "move", notes.Add, TimeSpan.Zero)) { }

        Assert.Contains("broke a stale repo lock left by", Assert.Single(notes));
    }

    [Fact]
    public void Another_hosts_lock_is_trusted_until_it_is_old()
    {
        WriteLock("some-other-host", 1, DateTimeOffset.UtcNow);
        Assert.Throws<WriteRefusedException>(() => RepoLock.Acquire(_root, "move", wait: TimeSpan.Zero));

        WriteLock("some-other-host", 1, DateTimeOffset.UtcNow - RepoLock.StaleAfter - TimeSpan.FromSeconds(1));
        RepoLock.Acquire(_root, "move", wait: TimeSpan.Zero).Dispose();
    }

    private void WriteLock(string host, int pid, DateTimeOffset since) =>
        File.WriteAllText(RepoLock.PathFor(_root),
            $$"""{"host":"{{host}}","pid":{{pid}},"operation":"move","since":"{{since:O}}","token":"t"}""");

    private static int DeadPid()
    {
        var live = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        var pid = 4_000_000;
        while (live.Contains(pid)) pid++;
        return pid;
    }
}
