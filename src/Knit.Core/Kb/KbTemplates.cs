using System.Reflection;
using System.Text;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Loads KB body templates from embedded resources. One template per bucket
/// (no per-instance customization in M1b — same pattern as <c>SKILL.md</c>).
/// </summary>
public static class KbTemplates
{
    public static string Load(Bucket bucket)
    {
        var name = $"Knit.Core.Templates.Kb.{bucket.ToWire()}.md";
        var asm = typeof(KbTemplates).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded template: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
