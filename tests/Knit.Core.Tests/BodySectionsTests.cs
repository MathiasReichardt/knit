using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class BodySectionsTests
{
    [Fact]
    public void Splits_h2_into_snake_case_keys_and_strips_leading_h1()
    {
        var body = """
            # title

            ## Description

            the prose

            ## Acceptance Criteria

            - [ ] thing

            ## Notes

            extra
            """;
        var sections = BodySections.Split(body);
        var keys = sections.Select(s => s.Key).ToList();
        // Leading "# title" duplicates the frontmatter title and is dropped.
        Assert.Equal(new[] { "description", "acceptance_criteria", "notes" }, keys);
        Assert.Equal("the prose", sections[0].Value);
        Assert.Equal("- [ ] thing", sections[1].Value);
        Assert.Equal("extra", sections[2].Value);
    }

    [Fact]
    public void Prose_before_first_h2_without_h1_stays_under_body_key()
    {
        var body = """
            some prelude prose

            ## Description

            content
            """;
        var sections = BodySections.Split(body);
        Assert.Equal(new[] { "body", "description" }, sections.Select(s => s.Key).ToList());
        Assert.Equal("some prelude prose", sections[0].Value);
    }

    [Fact]
    public void Excludes_comments_section()
    {
        var body = """
            ## Description
            x
            ## Comments
            ignored
            """;
        var sections = BodySections.Split(body);
        Assert.Single(sections);
        Assert.Equal("description", sections[0].Key);
    }

    [Fact]
    public void Empty_body_returns_empty()
    {
        Assert.Empty(BodySections.Split(string.Empty));
    }

    [Fact]
    public void Body_without_h2_goes_under_body_key()
    {
        var sections = BodySections.Split("just prose, no headings\nsecond line");
        Assert.Single(sections);
        Assert.Equal("body", sections[0].Key);
        Assert.Equal("just prose, no headings\nsecond line", sections[0].Value);
    }

    [Fact]
    public void ExtractList_drops_the_scaffolded_empty_checkbox()
    {
        var body = """
            ## Acceptance criteria

            - [ ]
            -
            """;
        Assert.Empty(BodySections.ExtractList(body, "Acceptance criteria"));
    }

    [Fact]
    public void ExtractList_turns_the_checkbox_into_checked_state()
    {
        var body = """
            ## Acceptance criteria

            - [ ] unchecked one
            - [x] checked one
            - [X] checked upper
            - plain one
            """;
        Assert.Equal(
            new BodySections.ListEntry[]
            {
                new("unchecked one", false),
                new("checked one", true),
                new("checked upper", true),
                new("plain one", null),
            },
            BodySections.ExtractList(body, "Acceptance criteria"));
    }

    [Fact]
    public void ExtractList_joins_continuation_lines_into_the_bullet()
    {
        var body = """
            ## Acceptance criteria

            - [x] first criterion, long enough to wrap
              onto a continuation line
            - [ ] second
            lazy continuation

              indented second paragraph

            prose after a blank line is not part of a bullet
            - third
            """;
        Assert.Equal(
            new BodySections.ListEntry[]
            {
                new("first criterion, long enough to wrap onto a continuation line", true),
                new("second lazy continuation indented second paragraph", false),
                new("third", null),
            },
            BodySections.ExtractList(body, "Acceptance criteria"));
    }

    [Fact]
    public void ExtractList_stops_a_bullet_at_the_next_heading()
    {
        var body = """
            ## Acceptance criteria

            - only one
            ## Notes
            not a criterion
            """;
        Assert.Equal(
            new BodySections.ListEntry[] { new("only one", null) },
            BodySections.ExtractList(body, "Acceptance criteria"));
    }

    [Fact]
    public void ExtractList_keeps_a_bullet_that_opens_with_a_markdown_link()
    {
        var body = """
            ## Code pointers

            - [src/foo.cs](../foo.cs) — why
            """;
        Assert.Equal(
            new BodySections.ListEntry[] { new("[src/foo.cs](../foo.cs) — why", null) },
            BodySections.ExtractList(body, "Code pointers"));
    }

    [Fact]
    public void Internal_h3_stays_inline_as_raw_markdown()
    {
        var body = """
            ## Description

            intro prose

            ### Sub heading

            sub content
            """;
        var sections = BodySections.Split(body);
        Assert.Single(sections);
        Assert.Equal("description", sections[0].Key);
        Assert.Contains("### Sub heading", sections[0].Value);
        Assert.Contains("sub content", sections[0].Value);
    }

    // The counterpart to SetSection with an empty value: the heading
    // goes too, so a template section that doesn't apply stops looking like
    // unfinished work.
    private const string ThreeSections = """
        # title

        ## Description

        the prose

        ## Repro

        - step one

        ## Notes

        tail
        """;

    [Fact]
    public void TryDeleteSection_removes_heading_and_content()
    {
        Assert.True(BodySections.TryDeleteSection(ThreeSections, "Repro", out var body));
        Assert.DoesNotContain("## Repro", body);
        Assert.DoesNotContain("step one", body);
        Assert.Contains("## Description", body);
        Assert.Contains("## Notes", body);
    }

    [Fact]
    public void TryDeleteSection_matches_case_insensitively()
    {
        Assert.True(BodySections.TryDeleteSection(ThreeSections, "repro", out var body));
        Assert.DoesNotContain("## Repro", body);
    }

    [Fact]
    public void TryDeleteSection_removes_the_last_section_too()
    {
        Assert.True(BodySections.TryDeleteSection(ThreeSections, "Notes", out var body));
        Assert.DoesNotContain("## Notes", body);
        Assert.DoesNotContain("tail", body);
        Assert.Contains("## Repro", body);
    }

    [Fact]
    public void TryDeleteSection_reports_an_absent_heading_instead_of_no_opping()
    {
        Assert.False(BodySections.TryDeleteSection(ThreeSections, "Nope", out var body));
        Assert.Equal(ThreeSections, body);
    }

    [Theory]
    [InlineData("Links")]
    [InlineData("Comments")]
    public void TryDeleteSection_refuses_structural_sections(string heading)
    {
        Assert.Throws<ArgumentException>(() =>
            BodySections.TryDeleteSection(ThreeSections, heading, out _));
    }

    [Fact]
    public void TryDeleteSection_leaves_a_clean_body_for_repeated_deletes()
    {
        Assert.True(BodySections.TryDeleteSection(ThreeSections, "Repro", out var once));
        Assert.True(BodySections.TryDeleteSection(once, "Notes", out var twice));
        Assert.DoesNotContain("\n\n\n", twice);
        Assert.Equal(["Description"], BodySections.Headings(twice));
    }

    [Theory]
    [InlineData("- step two")]
    [InlineData("")]
    public void SetSection_repeated_on_a_middle_section_keeps_one_blank_line(string value)
    {
        var body = ThreeSections;
        for (var i = 0; i < 3; i++) body = BodySections.SetSection(body, "Repro", value);
        Assert.DoesNotContain("\n\n\n", body);
        Assert.Equal(["Description", "Repro", "Notes"], BodySections.Headings(body));
    }

    [Fact]
    public void SetSection_replaces_content_in_place()
    {
        var body = BodySections.SetSection(ThreeSections, "Repro", "- step two");
        Assert.Contains("## Repro\n\n- step two\n\n## Notes", body);
        Assert.DoesNotContain("step one", body);
    }

    [Fact]
    public void Headings_returns_authored_spelling_in_order()
    {
        Assert.Equal(["Description", "Repro", "Notes"], BodySections.Headings(ThreeSections));
    }
}
