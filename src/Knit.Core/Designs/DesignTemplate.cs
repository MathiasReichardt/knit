using System.Reflection;
using System.Text;

namespace Knit.Core.Designs;

/// <summary>
/// Loads the embedded design body template. One file: the design side
/// of a cycle. (The plan side lives as Knit items, not markdown.)
/// </summary>
public static class DesignTemplate
{
    public static string Load()
    {
        const string name = "Knit.Core.Templates.Designs.design.md";
        var asm = typeof(DesignTemplate).Assembly;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded template: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
