namespace Knit.Core.Frontmatter;

/// <summary>
/// Reads and writes the YAML frontmatter that lives between `---` markers
/// at the top of an item file. Implementations must be lenient: missing or
/// malformed fields degrade to defaults, unknown keys are preserved.
/// </summary>
public interface IFrontmatterSerializer
{
    /// <summary>
    /// Parse a YAML frontmatter block (without the surrounding --- markers)
    /// into a flat field map. Keys are strings; values are the raw scalar
    /// or nested structure as YAML produced it.
    /// </summary>
    Dictionary<string, object?> Parse(string yaml);

    /// <summary>
    /// Serialize a field map back to YAML in canonical key order.
    /// Caller is responsible for adding the surrounding --- markers.
    /// </summary>
    string Write(IReadOnlyDictionary<string, object?> fields);
}
