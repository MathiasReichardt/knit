using Knit.Core.Designs;
using Knit.Core.DocFolders;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Kb;
using Knit.Core.Refs;
using Knit.Core.Templates;

namespace Knit.Core.Tests;

/// <summary>
/// The default templates and the extractors that read them are written in two
/// places and nothing else pairs them, so they drifted: the epic template
/// scaffolded "Success Criteria" while <c>knit context</c> only ever reads
/// "Acceptance criteria". These pin the two sides together.
/// </summary>
public class TemplateExtractorTests
{
    /// <summary>
    /// The types whose template seeds the criteria section. The rest are
    /// deliberate omissions, not oversights: a <c>bug</c> states expected vs
    /// actual instead, a <c>project</c> is a container, a <c>todo</c> is too
    /// small to earn one.
    /// </summary>
    private static readonly ItemType[] SeedCriteria = [ItemType.Epic, ItemType.Feature, ItemType.Task];

    [Fact]
    public void A_criteria_section_is_always_the_one_the_extractor_reads()
    {
        foreach (var type in TemplateEngine.AllTypes)
        {
            var headings = Headings(TemplateEngine.EmbeddedDefault(type))
                .Where(h => h.Contains("criteria", StringComparison.OrdinalIgnoreCase));

            foreach (var heading in headings)
                Assert.Equal(BodySections.AcceptanceHeading, heading);
        }
    }

    /// <summary>
    /// The other direction: the fact above only fires on a heading that is
    /// already there, so deleting the section outright would leave it green.
    /// This pins which types must seed it — and which must not.
    /// </summary>
    [Fact]
    public void Exactly_the_types_that_should_seed_criteria_do()
    {
        foreach (var type in TemplateEngine.AllTypes)
        {
            var seeded = Headings(TemplateEngine.EmbeddedDefault(type))
                .Any(h => h.Equals(BodySections.AcceptanceHeading, StringComparison.OrdinalIgnoreCase));

            Assert.True(SeedCriteria.Contains(type) == seeded,
                $"{type}: template {(seeded ? "seeds" : "omits")} \"{BodySections.AcceptanceHeading}\" but shouldn't.");
        }
    }

    [Fact]
    public void A_freshly_scaffolded_body_reports_no_criteria_and_no_pointers()
    {
        foreach (var type in TemplateEngine.AllTypes)
        {
            var body = TemplateEngine.EmbeddedDefault(type);
            Assert.Empty(BodySections.ExtractList(body, BodySections.AcceptanceHeading));
            Assert.Empty(BodySections.ExtractList(body, BodySections.PointersHeading));
        }
    }

    /// <summary>
    /// The design template has the same shape of pairing as the item ones —
    /// it seeds <c>## Status</c> and <c>DesignMeta.StatusOf</c> is the only
    /// thing that reads it, so a rename would silently blank the status column
    /// in <c>knit design list</c>. Unlike the item criteria sections this one
    /// is seeded *filled*: a fresh design must list as a draft, not as blank.
    /// </summary>
    [Fact]
    public void The_design_template_seeds_a_status_the_extractor_reads()
    {
        var (_, body) = DocBody.Split(DesignTemplate.Load());

        Assert.Contains(DesignMeta.StatusHeading, Headings(body), StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(DesignMeta.StatusOf(body)));
    }

    /// <summary>
    /// <c>SetSection</c> writes a blank line under the heading. A template
    /// without one makes the first edit look like it inserted a line; CRLF
    /// from a Windows checkout makes it rewrite every line.
    /// </summary>
    [Fact]
    public void Every_template_is_LF_with_a_blank_line_under_each_h2()
    {
        var templates = TemplateEngine.AllTypes.Select(t => (t.ToString(), TemplateEngine.EmbeddedDefault(t)))
            .Concat(new[] { Bucket.Adr, Bucket.Doc, Bucket.Feature, Bucket.Log, Bucket.Idea, Bucket.Source }
                .Select(b => (b.ToString(), KbTemplates.Load(b))))
            .Append(("design", DesignTemplate.Load()));

        foreach (var (name, text) in templates)
        {
            Assert.DoesNotContain('\r', text);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length - 1; i++)
                if (lines[i].StartsWith("## ", StringComparison.Ordinal))
                    Assert.True(lines[i + 1].Length == 0, $"{name}: no blank line under '{lines[i]}'");
        }
    }

    private static IEnumerable<string> Headings(string body) =>
        body.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l[3..].Trim());
}
