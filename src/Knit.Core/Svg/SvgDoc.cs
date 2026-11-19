using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Knit.Core.Svg;

/// <param name="Group">The id of the nearest enclosing <c>data-kind="group"</c>.</param>
/// <param name="At">The element's own <c>translate(x,y)</c>, as written: where a node sits.</param>
public sealed record SvgElement(
    string Kind, string? Id, string? Title, string? From, string? To, string? Group, string? At = null);

public sealed record SvgOutline(string? Desc, IReadOnlyList<SvgElement> Elements);

/// <summary>
/// A diagram SVG file placed next to the doc that embeds it. Its meaning lives
/// in attributes (<c>data-kind</c>, <c>data-from</c> / <c>data-to</c>, a
/// <c>&lt;title&gt;</c> per element, a <c>&lt;desc&gt;</c> at the top), so an
/// agent reads the <see cref="Outline"/> instead of the geometry.
/// </summary>
public static partial class SvgDoc
{
    public static readonly IReadOnlyList<string> Kinds = ["node", "edge", "group", "label"];

    // Inlined into the SPA page: a script or handler runs in its origin, a
    // <style> restyles the whole page, a foreignObject carries HTML.
    private static readonly string[] Forbidden = ["script", "foreignObject", "style"];

    public static XDocument? TryParse(string text, out string? error)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            error = null;
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>What keeps the file from rendering safely or reading well; empty when it holds.</summary>
    public static IReadOnlyList<string> Check(string text)
    {
        var doc = TryParse(text, out var error);
        if (doc?.Root is not { } root) return [$"not well-formed XML: {error}"];
        if (root.Name.LocalName != "svg") return [$"root element is <{root.Name.LocalName}>, not <svg>"];

        var reasons = new List<string>();
        if (root.Attribute("viewBox") is null) reasons.Add("no viewBox on <svg> — it won't scale to its frame");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var el in root.DescendantsAndSelf())
        {
            var name = el.Name.LocalName;
            if (Forbidden.Contains(name)) reasons.Add($"<{name}> is not allowed");
            foreach (var a in el.Attributes())
            {
                var attr = a.Name.LocalName;
                if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase) && a.Name.Namespace == XNamespace.None)
                    reasons.Add($"event attribute {attr} on {Describe(el)} is not allowed");
                if (attr == "href" && !a.Value.TrimStart().StartsWith('#'))
                    reasons.Add($"external href '{a.Value}' on {Describe(el)} is not allowed — only #fragment links");
            }
            if (Id(el) is { } id && !ids.Add(id)) reasons.Add($"duplicate id '{id}'");
        }

        foreach (var el in root.Descendants().Where(e => Kind(e) is not null))
        {
            var kind = Kind(el)!;
            if (!Kinds.Contains(kind))
            {
                reasons.Add($"unknown data-kind '{kind}' on {Describe(el)} (node, edge, group, label)");
                continue;
            }
            if (kind is "node" or "group" && Title(el) is null)
                reasons.Add($"{kind} {Describe(el)} has no <title> — its name, and the pointer label");
            if (kind != "edge") continue;
            foreach (var end in new[] { "data-from", "data-to" })
            {
                var target = (string?)el.Attribute(end);
                if (target is null) reasons.Add($"edge {Describe(el)} has no {end}");
                else if (!ids.Contains(target)) reasons.Add($"edge {Describe(el)} {end} '{target}' names no element");
            }
        }
        return reasons;
    }

    /// <summary>
    /// The <c>&lt;desc&gt;</c> and every <c>data-kind</c> element in document order, with
    /// its <c>translate</c> — enough to place a node, without reading paths or shapes.
    /// </summary>
    public static SvgOutline Outline(XDocument doc)
    {
        var root = doc.Root!;
        var desc = root.Elements().FirstOrDefault(e => e.Name.LocalName == "desc")?.Value.Trim();
        var elements = root.Descendants()
            .Where(e => Kind(e) is not null)
            .Select(e => new SvgElement(
                Kind(e)!,
                Id(e),
                Title(e),
                (string?)e.Attribute("data-from"),
                (string?)e.Attribute("data-to"),
                e.Ancestors().FirstOrDefault(a => Kind(a) == "group") is { } g ? Id(g) : null,
                Translate(e)))
            .ToList();
        return new SvgOutline(string.IsNullOrEmpty(desc) ? null : desc, elements);
    }

    [GeneratedRegex(@"translate\(\s*(-?[\d.]+)(?:[\s,]+(-?[\d.]+))?\s*\)")]
    private static partial Regex TranslateRe();

    private static string? Translate(XElement e) =>
        TranslateRe().Match((string?)e.Attribute("transform") ?? "") is { Success: true } m
            ? $"{m.Groups[1].Value},{(m.Groups[2].Success ? m.Groups[2].Value : "0")}"
            : null;

    private static string? Kind(XElement e) => (string?)e.Attribute("data-kind");

    private static string? Id(XElement e) => (string?)e.Attribute("id");

    private static string? Title(XElement e) =>
        e.Elements().FirstOrDefault(c => c.Name.LocalName == "title")?.Value.Trim() is { Length: > 0 } t ? t : null;

    private static string Describe(XElement e) => Id(e) is { } id ? $"'{id}'" : $"<{e.Name.LocalName}>";
}
