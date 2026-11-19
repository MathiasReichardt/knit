using System.Globalization;
using System.Reflection;
using System.Text;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.IO;

namespace Knit.Core.Templates;

/// <summary>
/// Template loading + {{placeholder}} substitution. Default templates
/// for every item type ship as embedded resources in this assembly;
/// `knit init` extracts them to <c>.knit/templates/</c> so users can customize.
/// </summary>
public static class TemplateEngine
{
    public const string TemplatesDirName = "templates";

    public static IReadOnlyList<ItemType> AllTypes => Hierarchy.AllTypes;

    /// <summary>
    /// Read the embedded default template for the given type.
    /// </summary>
    public static string EmbeddedDefault(ItemType type)
    {
        var name = $"Knit.Core.Templates.Defaults.{type.ToWire()}.md";
        var asm = typeof(TemplateEngine).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded template: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // A Windows checkout (autocrlf) embeds CRLF; items are written LF.
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>
    /// Resolve template content for an item type. Looks in
    /// <c>.knit/templates/&lt;type&gt;.md</c> first, falls back to the
    /// embedded default. <paramref name="name"/> overrides the default
    /// stem for variants like <c>bug-customer-reported</c>.
    /// </summary>
    public static string Load(string knitRoot, ItemType type, string? name = null)
    {
        var stem = string.IsNullOrEmpty(name) ? type.ToWire() : name;
        if (!IsValidTemplateName(stem))
            throw new ArgumentException(
                $"invalid template name '{stem}'; allowed: letters, digits, '-', '_'", nameof(name));
        var path = Path.Combine(
            RepoDiscovery.KnitDir(knitRoot), TemplatesDirName, $"{stem}.md");
        if (File.Exists(path))
            return AtomicFile.ReadAllText(path).Replace("\r\n", "\n");
        return EmbeddedDefault(type);
    }

    private static bool IsValidTemplateName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var c in name)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Write the embedded defaults to <c>.knit/templates/</c>.
    /// Used by <c>knit init</c>. Existing files are left alone.
    /// </summary>
    public static IReadOnlyList<string> ExtractDefaults(string knitRoot)
    {
        var dir = Path.Combine(RepoDiscovery.KnitDir(knitRoot), TemplatesDirName);
        Directory.CreateDirectory(dir);
        var written = new List<string>();
        foreach (var type in AllTypes)
        {
            var path = Path.Combine(dir, $"{type.ToWire()}.md");
            if (File.Exists(path)) continue;
            AtomicFile.WriteAllText(path, EmbeddedDefault(type));
            written.Add(path);
        }
        return written;
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            if (i + 1 < template.Length && template[i] == '{' && template[i + 1] == '{')
            {
                var end = template.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    var key = template.Substring(i + 2, end - i - 2).Trim();
                    sb.Append(values.TryGetValue(key, out var v) ? v : string.Empty);
                    i = end + 2;
                    continue;
                }
            }
            sb.Append(template[i]);
            i++;
        }
        return sb.ToString();
    }

    public static IReadOnlyDictionary<string, string> StandardValues(
        string id, string title, string author, ItemType type)
    {
        return new Dictionary<string, string>
        {
            ["id"] = id,
            ["title"] = title,
            ["author"] = author,
            ["now"] = DateTimeOffset.UtcNow.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["type"] = type.ToWire(),
        };
    }
}
