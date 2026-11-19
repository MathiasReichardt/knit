using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;
using Knit.Data.StatusPage;

namespace Knit.Cli.Tests;

/// <summary>
/// The status page renderer over a synthetic repo, so every id,
/// path and date is fixed and the output can be pinned to the byte.
/// </summary>
public class StatusPageRendererTests
{
    private static readonly DateOnly Today = new(2030, 3, 15);

    /// <summary>An in-memory repo: items, doc folders, designs and ADRs under a fake root.</summary>
    private sealed class Repo
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "status-page-fixture");
        private List<Item> Items { get; } = [];
        private List<StatusPageFolder> Folders { get; } = [];
        private List<StatusPageDesign> Designs { get; } = [];
        private List<StatusPageAdr> Adrs { get; } = [];
        private int _rank;

        public static string Id(string key) => key.ToUpperInvariant().PadRight(18, '0');

        private string ItemPath(string key) => Path.Combine(Root, ".knit", "items", $"{Id(key)}-{key}.md");

        public void Add(
            string key, ItemType type, string title, string? parent = null, ItemState state = ItemState.Todo,
            Priority priority = Priority.Normal, DateOnly? due = null, string? assignee = null,
            string? blocked = null, string? milestone = null, string? project = null,
            string? design = null, string? effort = null, Health? health = null, IEnumerable<string>? blocks = null)
        {
            var rows = new List<LinkRow>();
            if (milestone is not null)
            {
                var (p, s) = (milestone.Split('/')[0], milestone.Split('/')[1]);
                rows.Add(new LinkRow(LinkRel.Milestone, s, $"../projects/{p}/milestones/{s}.md", null));
            }
            foreach (var b in blocks ?? []) rows.Add(new LinkRow(LinkRel.Blocks, b, $"{Id(b)}-{b}.md", null));
            var item = new Item
            {
                Id = Id(key),
                Title = title,
                Type = type,
                State = state,
                Parent = parent is null ? null : Id(parent),
                Rank = $"m{_rank++:D4}",
                Priority = priority,
                Due = due,
                Assignee = assignee,
                BlockedReason = blocked,
                LinkRows = rows,
                Project = project,
                Design = design,
                Effort = effort,
                Health = health,
            };
            Items.Add(item);
        }

        public void Folder(string slug, string title, bool roadmap, params MilestoneDoc[] milestones)
        {
            var dir = Path.Combine(Root, ".knit", "projects", slug);
            var f = new StatusPageFolder(slug, title, Path.Combine(dir, "readme.md"), roadmap ? Path.Combine(dir, "roadmap.md") : null, milestones);
            Folders.Add(f);
        }

        public MilestoneDoc Milestone(string project, string slug, string title, DateOnly? target, MilestoneState state = MilestoneState.Planned) =>
            new(project, slug, title, target, state, null, null,
                Path.Combine(Root, ".knit", "projects", project, "milestones", $"{slug}.md"));

        public void Design(string slug, string title, string? status) =>
            Designs.Add(new StatusPageDesign(slug, title, Path.Combine(Root, ".knit", "designs", $"{slug}-design.md"), status));

        public void Adr(string label, string title, DateOnly? date, string status) =>
            Adrs.Add(new StatusPageAdr(label, title, date, status, Path.Combine(Root, ".knit", "kb", "adrs", $"{label}-x.md")));

        /// <summary>The page with its non-breaking spaces shown as plain ones, so expectations stay readable.</summary>
        public string Render(string? reportDir = null) => RenderRaw(reportDir).Replace((char)0xA0, ' ');

        public string RenderRaw(string? reportDir = null) => StatusPageRenderer.Render(new StatusPageInput(
            "demo", Today, reportDir ?? Root, Items,
            Items.ToDictionary(i => i.Id, i => ItemPath(i.Id[..i.Id.TrimEnd('0').Length].ToLowerInvariant())),
            id => id.TrimEnd('0').ToLowerInvariant(), Folders, Designs, Adrs));
    }

    private static string Section(string page, string heading)
    {
        var start = page.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no section '{heading}'");
        var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return page[(start + 1)..(end < 0 ? page.Length : end + 1)];
    }

    /// <summary>
    /// Two projects, a nested project, items outside any project, and a doc
    /// folder no project item points at — every section pinned once.
    /// </summary>
    private static Repo Fixture()
    {
        var r = new Repo();
        var beta = r.Milestone("web", "beta", "Beta", new DateOnly(2030, 3, 1));
        var ga = r.Milestone("web", "ga", "GA", new DateOnly(2030, 4, 10));
        r.Folder("web", "Website", roadmap: true, beta, ga);
        r.Folder("ops", "Operations", roadmap: false, r.Milestone("ops", "audit", "Audit", new DateOnly(2030, 4, 1)));

        r.Add("web", ItemType.Project, "Website", project: "web", health: Health.AtRisk);
        r.Add("shop", ItemType.Epic, "Shop", "web", milestone: "web/beta");
        r.Add("cart", ItemType.Feature, "Cart", "shop", state: ItemState.InProgress, assignee: "ann");
        r.Add("c1", ItemType.Task, "Cart model", "cart", state: ItemState.Done, assignee: "ann");
        r.Add("c2", ItemType.Task, "Cart view", "cart", due: new DateOnly(2030, 3, 10), assignee: "ann");
        r.Add("pay", ItemType.Feature, "Payments", "shop", blocked: "vendor contract");
        r.Add("p1", ItemType.Task, "Card form", "pay");
        r.Add("crash", ItemType.Bug, "Checkout crash", "shop", priority: Priority.Critical, assignee: "bo");
        r.Add("w1", ItemType.Todo, "Pick a font", "web", state: ItemState.Done);
        r.Add("w2", ItemType.Todo, "Buy domain", "web");
        r.Add("docs", ItemType.Project, "Docs", "web");
        r.Add("guide", ItemType.Epic, "User guide", "docs", due: new DateOnly(2030, 4, 20), assignee: "cy");

        r.Add("app", ItemType.Project, "Mobile app");
        r.Add("login", ItemType.Feature, "Login", "app", state: ItemState.Review, effort: "S");

        r.Add("loose", ItemType.Bug, "Loose bug");
        r.Add("chore", ItemType.Task, "Rotate keys", priority: Priority.Important, due: new DateOnly(2030, 3, 5), assignee: "dee");

        r.Design("search", "Search", "Draft — under review");
        r.Adr("0001", "Use Postgres", new DateOnly(2030, 3, 1), "proposed");
        return r;
    }

    [Fact]
    public void The_fixture_renders_to_the_expected_page()
    {
        const string expected = """
            <!-- generated by knit status report — do not edit -->
            # demo — project status

            Generated 2030-03-15 · for the full tracker run `knit web` in this repo

            [In one minute](#in-one-minute) · [Needs attention](#needs-attention) · [Projects](#projects) · [Deadlines](#deadlines) · [Designs](#designs)

            🎯 project · ⚡ epic · ✨ feature · 🐞 bug · 🔧 task · 📝 todo · 🏁 milestone · ⛔ blocked · ⚠ overdue / at risk

            ## In one minute

            - 🏁 **Beta is past its target:** 2030-03-01, 14 days ago, with 6 of 7 items open.
            - ⚠ **2 items are overdue.** The oldest is [Rotate keys](.knit/items/CHORE0000000000000-chore.md), 10 days late.
            - ⛔ **1 item is blocked**, holding up 1 more.
            - 👤 **9 of 14 open items have no owner.**
            - 🐞 **2 open bugs:** 1 critical · 1 normal.

            ## Needs attention

            1. 🏁 **Beta is past its target with work open.** The target was 2030-03-01; 6 items are open.
               [Milestone](.knit/projects/web/milestones/beta.md)
            2. ⛔ **Payments is blocked and holds up 1 open item.** Reason: "vendor contract".
               [pay](.knit/items/PAY000000000000000-pay.md)
            3. ⚠ **Rotate keys is important and 10 days overdue.** Owner: dee.
               [chore](.knit/items/CHORE0000000000000-chore.md)
            4. 👀 **Login is in review with no assignee.**
               [login](.knit/items/LOGIN0000000000000-login.md)
            5. 🐞 **Loose bug is a bug with no owner and no parent.**
               [loose](.knit/items/LOOSE0000000000000-loose.md)
            6. 📜 **The decision "Use Postgres" has been proposed since 2030-03-01** (14 days).
               [ADR 0001](.knit/kb/adrs/0001-x.md)

            ## Projects

            Epics and features with their progress. Bugs get a row only when they are late or critical; the rest are counted.

            - [Website](#website)
            - [Docs](#docs)
            - [Mobile app](#mobile-app)
            - [Operations](#operations)
            - [Outside any project](#outside-any-project)

            ### Website

            [Readme](.knit/projects/web/readme.md) · [Roadmap](.knit/projects/web/roadmap.md) · health: at risk · [web](.knit/items/WEB000000000000000-web.md) · 🟩⬜⬜⬜⬜ 2 / 11 done

            | Milestone | Target | State | Progress |
            |---|---|---|---|
            | 🏁 [Beta](.knit/projects/web/milestones/beta.md) | 2030-03-01 | planned · ⚠ target passed | 🟩⬜⬜⬜⬜ 1 / 7 |
            | 🏁 [GA](.knit/projects/web/milestones/ga.md) | 2030-04-10 | planned | nothing linked |

            | Work | Progress | Due | Notes |
            |---|---|---|---|
            | ⚡ [Shop](.knit/items/SHOP00000000000000-shop.md) | 🟩⬜⬜⬜⬜ 1 / 6 | | ⚠ at risk: needed by 2030-03-01 |
            | └ ✨ [Cart](.knit/items/CART00000000000000-cart.md) | 🟩🟩🟩⬜⬜ 1 / 2 | | in progress · ⚠ at risk: needed by 2030-03-01 · 1 task overdue |
            | └ ✨ [Payments](.knit/items/PAY000000000000000-pay.md) | ⬜⬜⬜⬜⬜ 0 / 1 | | ⛔ vendor contract · ⚠ at risk: needed by 2030-03-01 · 1 task at risk |
            | └ 🐞 [Checkout crash](.knit/items/CRASH0000000000000-crash.md) | | | critical · ⚠ at risk: needed by 2030-03-01 |
            | 🐞 1 open bug: 1 critical | | | |
            | 📝 2 todos | 🟩🟩🟩⬜⬜ 1 / 2 | | |

            ### Docs

            [docs](.knit/items/DOCS00000000000000-docs.md) · ⬜⬜⬜⬜⬜ 0 / 1 done

            | Work | Progress | Due | Notes |
            |---|---|---|---|
            | ⚡ [User guide](.knit/items/GUIDE0000000000000-guide.md) | nothing planned | 2030-04-20 | |

            ### Mobile app

            [app](.knit/items/APP000000000000000-app.md) · ⬜⬜⬜⬜⬜ 0 / 1 done

            | Work | Progress | Due | Notes |
            |---|---|---|---|
            | ✨ [Login](.knit/items/LOGIN0000000000000-login.md) | nothing planned | | effort S · review |

            ### Operations

            [Readme](.knit/projects/ops/readme.md)

            | Milestone | Target | State | Progress |
            |---|---|---|---|
            | 🏁 [Audit](.knit/projects/ops/milestones/audit.md) | 2030-04-01 | planned | nothing linked |

            ### Outside any project

            | Work | Progress | Due | Notes |
            |---|---|---|---|
            | 🐞 1 open bug: 1 normal | | | |
            | 🔧 1 task | ⬜⬜⬜⬜⬜ 0 / 1 | | |

            ## Deadlines

            Overdue, at risk, or due before 2030-04-26. Late tasks are counted on the feature, epic or bug they belong to.

            | Date | What | Priority | Risk |
            |---|---|---|---|
            | 2030-03-01 | 🏁 [Beta](.knit/projects/web/milestones/beta.md) | | ⚠ target passed · 6 open |
            | 2030-03-01 | ⚡ [Shop](.knit/items/SHOP00000000000000-shop.md) | | ⚠ at risk: needed by 2030-03-01 (Beta) |
            | 2030-03-01 | ✨ [Cart](.knit/items/CART00000000000000-cart.md) | | ⚠ at risk: needed by 2030-03-01 (Beta) · 1 task overdue |
            | 2030-03-01 | ✨ [Payments](.knit/items/PAY000000000000000-pay.md) | | ⚠ at risk: needed by 2030-03-01 (Beta) · ⛔ · 1 task at risk |
            | 2030-03-01 | 🐞 [Checkout crash](.knit/items/CRASH0000000000000-crash.md) | critical | ⚠ at risk: needed by 2030-03-01 (Beta) |
            | 2030-03-05 | 🔧 [Rotate keys](.knit/items/CHORE0000000000000-chore.md) | important | ⚠ overdue |
            | 2030-04-01 | 🏁 [Audit](.knit/projects/ops/milestones/audit.md) | | |
            | 2030-04-10 | 🏁 [GA](.knit/projects/web/milestones/ga.md) | | |
            | 2030-04-20 | ⚡ [User guide](.knit/items/GUIDE0000000000000-guide.md) | | |

            ## Designs

            | Design | Status |
            |---|---|
            | [Search](.knit/designs/search-design.md) | Draft — under review |

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Fixture().Render());
    }

    [Fact]
    public void Same_data_on_the_same_day_renders_the_same_bytes()
    {
        var a = Fixture().Render();
        var b = Fixture().Render();
        Assert.Equal(a, b);
    }

    // ------------------------------------------------------------ projects TOC

    private static bool HasProjectsToc(string page) => Section(page, "Projects").Contains("\n- [", StringComparison.Ordinal);

    [Fact]
    public void The_projects_toc_appears_for_one_project_plus_outside_work()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Alpha");
        r.Add("e", ItemType.Epic, "Outside epic");
        Assert.True(HasProjectsToc(r.Render()));
    }

    [Fact]
    public void The_projects_toc_appears_for_two_projects()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Alpha");
        r.Add("q", ItemType.Project, "Beta");
        Assert.True(HasProjectsToc(r.Render()));
    }

    [Fact]
    public void The_projects_toc_is_absent_for_a_single_project_with_nothing_outside()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Alpha");
        r.Add("e", ItemType.Epic, "Inside", "p");
        Assert.False(HasProjectsToc(r.Render()));
    }

    [Fact]
    public void Two_projects_with_the_same_title_get_distinct_anchors()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Alpha");
        r.Add("q", ItemType.Project, "Alpha");
        var toc = Section(r.Render(), "Projects");
        Assert.Contains("- [Alpha](#alpha)\n", toc);
        Assert.Contains("- [Alpha](#alpha-1)\n", toc);
    }

    [Fact]
    public void Anchors_follow_the_forge_rule_and_count_the_fixed_headings()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Projects");
        r.Add("q", ItemType.Project, "A -- B (v2)");
        var toc = Section(r.Render(), "Projects");
        Assert.Contains("- [Projects](#projects-1)\n", toc);
        Assert.Contains("- [A -- B (v2)](#a----b-v2)\n", toc);
    }

    [Theory]
    [InlineData("A -- B (v2)", "a----b-v2")]
    [InlineData("Outside any project", "outside-any-project")]
    [InlineData("Ünïcode 한글 v1.2", "ünïcode-한글-v12")]
    [InlineData("snake_case & more!", "snake_case--more")]
    [InlineData("🎯 Goal", "-goal")]
    public void Anchor_base_keeps_letters_digits_hyphens_and_underscores(string heading, string anchor) =>
        Assert.Equal(anchor, GfmText.AnchorBase(heading));

    // --------------------------------------------------------- needs attention

    public static TheoryData<string, string> Rules => new()
    {
        { "R1", "is past its target with work open." },
        { "R2", "is blocked and holds up 1 open item." },
        { "R3", "is important and 5 days overdue." },
        { "R4", "is in review with no assignee." },
        { "R5", "is a bug with no owner and no parent." },
        { "R6", "has been proposed since 2030-03-01" },
    };

    private static Repo RuleFixture(string rule)
    {
        var r = new Repo();
        switch (rule)
        {
            case "R1":
                r.Folder("web", "Web", false, r.Milestone("web", "beta", "Beta", new DateOnly(2030, 3, 1)));
                r.Add("t", ItemType.Task, "Linked", assignee: "a", milestone: "web/beta");
                break;
            case "R2":
                r.Add("f", ItemType.Feature, "Held", assignee: "a", blocked: "waiting");
                r.Add("t", ItemType.Task, "Child", "f", assignee: "a");
                break;
            case "R3":
                r.Add("t", ItemType.Task, "Late", assignee: "a", priority: Priority.Important, due: new DateOnly(2030, 3, 10));
                break;
            case "R4":
                r.Add("t", ItemType.Task, "Waiting", state: ItemState.Review);
                break;
            case "R5":
                r.Add("b", ItemType.Bug, "Loose");
                break;
            case "R6":
                r.Adr("0001", "Pick a store", new DateOnly(2030, 3, 1), "proposed");
                break;
        }
        return r;
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Each_rule_fires_on_its_fixture_alone(string rule, string sentence)
    {
        var attention = Section(RuleFixture(rule).Render(), "Needs attention");
        Assert.Contains(sentence, attention);
        Assert.Contains("\n1. ", attention);
        Assert.DoesNotContain("\n2. ", attention);
    }

    [Fact]
    public void The_rules_stay_quiet_when_their_condition_is_not_met()
    {
        var r = new Repo();
        r.Folder("web", "Web", false,
            r.Milestone("web", "done", "Done", new DateOnly(2030, 3, 1)),
            r.Milestone("web", "won", "Won", new DateOnly(2030, 3, 1), MilestoneState.Reached));
        r.Add("t1", ItemType.Task, "Closed under passed", assignee: "a", state: ItemState.Done, milestone: "web/done");
        r.Add("t2", ItemType.Task, "Open under reached", assignee: "a", milestone: "web/won");
        r.Add("f", ItemType.Feature, "Blocked leaf", assignee: "a", blocked: "waiting");
        r.Add("t3", ItemType.Task, "Normal and late", assignee: "a", due: new DateOnly(2030, 3, 1));
        r.Add("t4", ItemType.Task, "Reviewed", assignee: "a", state: ItemState.Review);
        r.Add("b", ItemType.Bug, "Owned bug", assignee: "a");
        r.Add("b2", ItemType.Bug, "Parented bug", "t4");
        r.Adr("0001", "Settled", new DateOnly(2030, 3, 1), "accepted");
        Assert.Contains("Nothing needs attention.", Section(r.Render(), "Needs attention"));
    }

    [Fact]
    public void The_eleventh_hit_becomes_and_1_more()
    {
        var r = new Repo();
        for (var i = 0; i < 11; i++) r.Add($"b{i:D2}", ItemType.Bug, $"Bug {i}");
        var attention = Section(r.Render(), "Needs attention");
        Assert.Contains("\n10. ", attention);
        Assert.DoesNotContain("\n11. ", attention);
        Assert.Contains("and 1 more", attention);
    }

    // ----------------------------------------------------------- in one minute

    [Fact]
    public void An_empty_repo_reads_nothing_is_overdue_blocked_or_at_risk()
    {
        var page = new Repo().Render();
        Assert.Contains("Nothing is overdue, blocked or at risk.", Section(page, "In one minute"));
        Assert.DoesNotContain(" 0 ", page);
    }

    [Fact]
    public void A_zero_count_leaves_its_sentence_out()
    {
        var r = new Repo();
        r.Add("t", ItemType.Task, "Late", assignee: "a", due: new DateOnly(2030, 3, 1));
        var minute = Section(r.Render(), "In one minute");
        Assert.Contains("1 item is overdue.", minute);
        Assert.DoesNotContain("blocked", minute);
        Assert.DoesNotContain("owner", minute);
        Assert.DoesNotContain("bug", minute);
        Assert.DoesNotContain("🏁", minute);
    }

    // ------------------------------------------------------------- work table

    [Fact]
    public void A_deep_epic_counts_into_its_nearest_shown_row_and_a_deep_late_bug_gets_a_row()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Proj");
        r.Add("e1", ItemType.Epic, "Top epic", "p");
        r.Add("f1", ItemType.Feature, "Child feature", "e1");
        r.Add("e2", ItemType.Epic, "Deep epic", "f1");
        r.Add("t1", ItemType.Task, "Deep task", "e2", state: ItemState.Done);
        r.Add("t2", ItemType.Task, "Deeper task", "e2");
        r.Add("bug", ItemType.Bug, "Deep late bug", "t2", due: new DateOnly(2030, 3, 1));
        var work = Section(r.Render(), "Projects");
        Assert.DoesNotContain("Deep epic", work);
        // f1's progress covers e2 and everything under it.
        Assert.Contains($"| └ ✨ [Child feature](.knit/items/{Repo.Id("f1")}-f1.md) | 🟩⬜⬜⬜⬜ 1 / 4 |", work);
        Assert.Contains("| └ 🐞 [Deep late bug]", work);
    }

    [Fact]
    public void A_normal_bug_that_is_not_late_appears_only_in_the_counter()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Proj");
        r.Add("hot", ItemType.Bug, "Hot bug", "p", priority: Priority.Critical);
        r.Add("calm", ItemType.Bug, "Calm bug", "p");
        var work = Section(r.Render(), "Projects");
        Assert.Contains("[Hot bug]", work);
        Assert.DoesNotContain("Calm bug", work);
        Assert.Contains("| 🐞 2 open bugs: 1 critical · 1 normal |", work);
    }

    [Fact]
    public void An_open_bug_under_a_closed_feature_shows_the_feature_struck()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Proj");
        r.Add("f", ItemType.Feature, "Shipped", "p", state: ItemState.Done);
        r.Add("b", ItemType.Bug, "Regression", "f", priority: Priority.Critical);
        var work = Section(r.Render(), "Projects");
        Assert.Contains("| ✨ ~~[Shipped](", work);
        Assert.Contains("| └ 🐞 [Regression](", work);
    }

    // -------------------------------------------------------------- deadlines

    [Fact]
    public void A_late_task_shows_only_as_its_ancestors_rollup()
    {
        var deadlines = Section(Fixture().Render(), "Deadlines");
        Assert.DoesNotContain("Cart view", deadlines);
        Assert.Contains("[Cart](.knit/items/CART00000000000000-cart.md) | | ⚠ at risk: needed by 2030-03-01 (Beta) · 1 task overdue |", deadlines);
        // No epic, feature or bug above it: a row of its own.
        Assert.Contains("🔧 [Rotate keys]", deadlines);
    }

    [Fact]
    public void No_item_appears_twice_and_a_passed_milestone_with_nothing_open_has_no_row()
    {
        var r = Fixture();
        r.Folder("old", "Old", false, r.Milestone("old", "closed", "Closed out", new DateOnly(2030, 1, 1)));
        r.Add("x", ItemType.Task, "Finished", state: ItemState.Done, milestone: "old/closed");
        var deadlines = Section(r.Render(), "Deadlines");
        Assert.DoesNotContain("Closed out", deadlines);
        var links = deadlines.Split('\n').Where(l => l.StartsWith("| 20", StringComparison.Ordinal))
            .Select(l => l.Split(" | ")[1]).ToList();
        Assert.Equal(links.Count, links.Distinct().Count());
    }

    // ---------------------------------------------------------------- designs

    [Fact]
    public void Superseded_and_implemented_designs_are_hidden()
    {
        var r = new Repo();
        r.Design("old", "Old way", "Superseded by the new way");
        r.Design("built", "Built", "Agreed");
        r.Design("live", "Live", "Agreed");
        r.Add("t1", ItemType.Task, "Built it", state: ItemState.Done, design: "built");
        r.Add("t2", ItemType.Task, "Building", design: "live");
        var designs = Section(r.Render(), "Designs");
        Assert.DoesNotContain("Old way", designs);
        Assert.DoesNotContain("[Built]", designs);
        Assert.Contains("[Live]", designs);
    }

    // --------------------------------------------------------------- escaping

    [Fact]
    public void Markup_in_a_title_renders_as_plain_text_and_keeps_its_row()
    {
        var r = new Repo();
        r.Add("p", ItemType.Project, "Proj");
        r.Add("e", ItemType.Epic, "a | b [c] *d*", "p");
        var row = Section(r.Render(), "Projects").Split('\n').Single(l => l.StartsWith("| ⚡", StringComparison.Ordinal));
        Assert.Contains(@"[a \| b \[c\] \*d\*](", row);
        // Four cells: five unescaped bars.
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(row, @"(?<!\\)\|").Count);
    }

    [Fact]
    public void A_progress_bar_and_its_count_never_wrap_apart()
    {
        const char nbsp = (char)0xA0;
        var page = Fixture().RenderRaw();
        Assert.Contains($"🟩⬜⬜⬜⬜{nbsp}1{nbsp}/{nbsp}7 |", page);
        Assert.Contains($"2{nbsp}/{nbsp}11{nbsp}done", page);
    }

    [Fact]
    public void Links_are_relative_to_the_report_folder()
    {
        var r = Fixture();
        var page = r.Render(Path.Combine(r.Root, "docs"));
        Assert.Contains("](../.knit/items/CHORE0000000000000-chore.md)", page);
        Assert.DoesNotContain("](.knit/", page);
    }
}
