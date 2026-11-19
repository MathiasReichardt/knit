using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Result;
using Knit.Core.Templates;

namespace Knit.Core.Designs;

/// <summary>
/// Read / write helpers for <c>.knit/designs/&lt;slug&gt;-design.md</c>.
/// Designs are markdown only — the plan side of a cycle is tracked as
/// Knit items (see the item <c>design:</c> field).
/// </summary>
public sealed class DesignStore
{
    private readonly string _repoRoot;
    private readonly IFrontmatterSerializer _yaml;

    public DesignStore(string repoRoot, IFrontmatterSerializer? yaml = null)
    {
        _repoRoot = repoRoot;
        _yaml = yaml ?? new SimpleFrontmatterSerializer();
    }

    public OperationResult<string> New(string slug, string title, bool force)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return OperationResult<string>.Error(KnitError.BadArgument("slug must not be empty"));

        var path = DesignsLayout.DesignDocPath(_repoRoot, slug);
        if (File.Exists(path) && !force)
            return OperationResult<string>.Error(KnitError.Conflict(
                $"design already exists at {path}; pass --force to overwrite"));

        var body = TemplateEngine.Render(DesignTemplate.Load(), new Dictionary<string, string>
        {
            ["title"] = title,
            ["slug"] = slug,
            ["now"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture),
        });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, body);
        return OperationResult<string>.Ok(path);
    }

    public string Write(string slug, IReadOnlyDictionary<string, object?> frontmatter, string body)
        => WritePath(DesignsLayout.DesignDocPath(_repoRoot, slug), frontmatter, body);

    // Write to an explicit path so archived docs (completed/<slug>/...) can be
    // amended in place — they don't live at the active DesignDocPath.
    public string WritePath(string path, IReadOnlyDictionary<string, object?> frontmatter, string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new System.Text.StringBuilder();
        if (frontmatter.Count > 0)
        {
            sb.Append("---\n");
            sb.Append(_yaml.Write(frontmatter).TrimEnd());
            sb.Append("\n---\n\n");
        }
        sb.Append(body.TrimEnd());
        sb.Append('\n');
        AtomicFile.WriteAllText(path, sb.ToString());
        return path;
    }

    public (IReadOnlyDictionary<string, object?> Frontmatter, string Body)? Read(string slug) =>
        ReadPath(DesignsLayout.DesignDocPath(_repoRoot, slug));

    public (IReadOnlyDictionary<string, object?> Frontmatter, string Body)? ReadPath(string path)
    {
        if (!File.Exists(path)) return null;
        var content = AtomicFile.ReadAllText(path).Replace("\r\n", "\n");
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return (new Dictionary<string, object?>(), content);
        var endIdx = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return (new Dictionary<string, object?>(), content);
        var fmText = content[3..endIdx].TrimStart('\n');
        var body = content[(endIdx + 4)..].TrimStart('\n');
        return (_yaml.Parse(fmText), body);
    }
}
