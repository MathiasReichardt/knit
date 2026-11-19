using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Core.Tests;

public class ItemContextResolverTests
{
    private static LinkRow KbRow(string text, string path, string? note = null) =>
        new(LinkRel.KbRef, text, path, note);

    private static Item Mk(
        string id, string? parent = null,
        IReadOnlyList<LinkRow>? rows = null,
        IReadOnlyList<string>? skills = null,
        string? blocked = null,
        string? design = null)
        => new()
        {
            Id = id,
            Title = id,
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Parent = parent,
            LinkRows = rows ?? [],
            AgentSkills = skills ?? [],
            BlockedReason = blocked,
            Design = design,
        };

    [Fact]
    public void Parent_chain_walks_up()
    {
        var epic = Mk("epic");
        var feat = Mk("feat", parent: "epic");
        var task = Mk("task", parent: "feat");
        var graph = new LinkGraph([epic, feat, task]);
        var ctx = ItemContextResolver.Resolve(graph, task);
        Assert.Equal(2, ctx.ParentChain.Count);
        Assert.Equal("feat", ctx.ParentChain[0].Id);
        Assert.Equal("epic", ctx.ParentChain[1].Id);
    }

    [Fact]
    public void KbRefs_union_dedups_and_marks_inherited()
    {
        var feat = Mk("feat", rows: [KbRow("adr:0001", "../../kb/adrs/0001.md")]);
        var task = Mk("task", parent: "feat", rows: [KbRow("doc:arch", "../../kb/docs/arch.md")]);
        var ctx = ItemContextResolver.Resolve(new LinkGraph([feat, task]), task);
        Assert.Equal(2, ctx.KbRefs.Count);
        Assert.Contains(ctx.KbRefs, r => r.TargetText == "doc:arch" && r.InheritedFrom == "");
        Assert.Contains(ctx.KbRefs, r => r.TargetText == "adr:0001" && r.InheritedFrom == "feat");
    }

    [Fact]
    public void KbRefs_closer_note_wins()
    {
        var feat = Mk("feat", rows: [KbRow("adr:0001", "../../kb/adrs/0001.md", "from feature")]);
        var task = Mk("task", parent: "feat", rows: [KbRow("adr:0001", "../../kb/adrs/0001.md", "from task")]);
        var ctx = ItemContextResolver.Resolve(new LinkGraph([feat, task]), task);
        var entry = Assert.Single(ctx.KbRefs);
        Assert.Equal("from task", entry.Note);
    }

    [Fact]
    public void AgentSkills_union_preserves_first_seen_order()
    {
        var feat = Mk("feat", skills: ["a", "b"]);
        var task = Mk("task", parent: "feat", skills: ["c", "a"]);
        var ctx = ItemContextResolver.Resolve(new LinkGraph([feat, task]), task);
        Assert.Equal(["c", "a", "b"], ctx.AgentSkills.Select(s => s.Skill).ToList());
    }

    [Fact]
    public void AgentSkills_marks_inherited_source()
    {
        var feat = Mk("feat", skills: ["a", "b"]);
        var task = Mk("task", parent: "feat", skills: ["c"]);
        var ctx = ItemContextResolver.Resolve(new LinkGraph([feat, task]), task);
        Assert.Equal("", ctx.AgentSkills.Single(s => s.Skill == "c").InheritedFrom);
        Assert.Equal("feat", ctx.AgentSkills.Single(s => s.Skill == "a").InheritedFrom);
        Assert.Equal("feat", ctx.AgentSkills.Single(s => s.Skill == "b").InheritedFrom);
    }

    [Fact]
    public void Blocked_own()
    {
        var task = Mk("task", blocked: "own block");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([task]), task);
        var b = Assert.Single(ctx.Blocked);
        Assert.Equal("own block", b.Reason);
        Assert.Equal("", b.InheritedFrom);
    }

    [Fact]
    public void Blocked_own_comes_first_and_keeps_the_holding_ancestor()
    {
        var epic = Mk("epic", blocked: "epic-block");
        var task = Mk("task", parent: "epic", blocked: "own block");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, task]), task);
        Assert.Equal(["own block", "epic-block"], ctx.Blocked.Select(b => b.Reason));
        Assert.Equal(["", "epic"], ctx.Blocked.Select(b => b.InheritedFrom));
    }

    [Fact]
    public void Blocked_inherited_from_ancestor()
    {
        var epic = Mk("epic", blocked: "epic-block");
        var task = Mk("task", parent: "epic");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, task]), task);
        Assert.Equal("epic", Assert.Single(ctx.Blocked).InheritedFrom);
    }

    [Fact]
    public void Blocked_sibling_not_propagated()
    {
        var epic = Mk("epic");
        var a = Mk("a", parent: "epic", blocked: "a-block");
        var b = Mk("b", parent: "epic");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, a, b]), b);
        Assert.Empty(ctx.Blocked);
    }

    [Fact]
    public void Design_inherited_from_ancestor()
    {
        var epic = Mk("epic", design: "web-ui");
        var feat = Mk("feat", parent: "epic");
        var task = Mk("task", parent: "feat");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, feat, task]), task);
        Assert.NotNull(ctx.Design);
        Assert.Equal("web-ui", ctx.Design!.Slug);
        Assert.Equal("epic", ctx.Design.InheritedFrom);
    }

    [Fact]
    public void Design_own_wins_over_ancestor()
    {
        var epic = Mk("epic", design: "web-ui");
        var task = Mk("task", parent: "epic", design: "task-specific");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, task]), task);
        Assert.NotNull(ctx.Design);
        Assert.Equal("task-specific", ctx.Design!.Slug);
        Assert.Equal("", ctx.Design.InheritedFrom);
    }

    [Fact]
    public void Design_nearest_ancestor_wins()
    {
        var epic = Mk("epic", design: "epic-design");
        var feat = Mk("feat", parent: "epic", design: "feat-design");
        var task = Mk("task", parent: "feat");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, feat, task]), task);
        Assert.NotNull(ctx.Design);
        Assert.Equal("feat-design", ctx.Design!.Slug);
        Assert.Equal("feat", ctx.Design.InheritedFrom);
    }

    [Fact]
    public void Design_none_when_unset_anywhere()
    {
        var epic = Mk("epic");
        var task = Mk("task", parent: "epic");
        var ctx = ItemContextResolver.Resolve(new LinkGraph([epic, task]), task);
        Assert.Null(ctx.Design);
    }
}
