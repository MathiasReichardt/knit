using Knit.Core.Svg;

namespace Knit.Core.Tests;

public class SvgDocTests
{
    private static IReadOnlyList<string> Check(string inner, string rootAttrs = """viewBox="0 0 10 10" """) =>
        SvgDoc.Check($"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" {rootAttrs}>{inner}</svg>""");

    [Fact]
    public void A_clean_file_has_no_findings() =>
        Assert.Empty(Check("""
            <desc>d</desc>
            <g data-kind="node" id="a"><title>A</title></g>
            <g data-kind="node" id="b"><title>B</title></g>
            <path data-kind="edge" id="ab" data-from="a" data-to="b"/>
            <a href="#a"><text data-kind="label">l</text></a>
            """));

    [Theory]
    [InlineData("<script>x()</script>", "<script> is not allowed")]
    [InlineData("<style>*{}</style>", "<style> is not allowed")]
    [InlineData("<foreignObject/>", "<foreignObject> is not allowed")]
    [InlineData("""<rect id="r" onclick="x()"/>""", "event attribute onclick on 'r'")]
    [InlineData("""<image href="https://x.test/a.png"/>""", "external href 'https://x.test/a.png'")]
    [InlineData("""<use xlink:href="other.svg#a"/>""", "external href 'other.svg#a'")]
    [InlineData("""<rect id="a"/><rect id="a"/>""", "duplicate id 'a'")]
    [InlineData("""<g data-kind="node" id="a"/>""", "node 'a' has no <title>")]
    [InlineData("""<g data-kind="group" id="g"/>""", "group 'g' has no <title>")]
    [InlineData("""<path data-kind="edge" id="e" data-from="x" data-to="x"/>""", "edge 'e' data-from 'x' names no element")]
    [InlineData("""<path data-kind="edge" id="e"/>""", "edge 'e' has no data-to")]
    [InlineData("""<g data-kind="box" id="b"/>""", "unknown data-kind 'box'")]
    public void Each_rule_is_reported(string inner, string expected) =>
        Assert.Contains(Check(inner), r => r.Contains(expected));

    [Fact]
    public void No_view_box_is_reported() =>
        Assert.Contains(Check("", rootAttrs: ""), r => r.Contains("no viewBox"));

    [Fact]
    public void Malformed_xml_is_reported_alone() =>
        Assert.Single(SvgDoc.Check("<svg><g></svg>"), r => r.StartsWith("not well-formed XML"));

    [Fact]
    public void A_doctype_is_ignored_not_resolved() =>
        Assert.Empty(SvgDoc.Check("""
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"/>
            """));

    [Fact]
    public void Outline_lists_desc_and_kinds_with_their_group()
    {
        var doc = SvgDoc.TryParse("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1">
              <desc> What it says. </desc>
              <g data-kind="group" id="g"><title>Zone</title>
                <g data-kind="node" id="a" transform="translate(48, 72)"><title>A</title></g>
              </g>
              <path data-kind="edge" id="e" data-from="a" data-to="a"/>
            </svg>
            """, out _)!;
        var outline = SvgDoc.Outline(doc);
        Assert.Equal("What it says.", outline.Desc);
        Assert.Equal(
            [
                new SvgElement("group", "g", "Zone", null, null, null),
                new SvgElement("node", "a", "A", null, null, "g", "48,72"),
                new SvgElement("edge", "e", null, "a", "a", null),
            ],
            outline.Elements);
    }
}
