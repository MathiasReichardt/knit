using Knit.Core.Domain;

namespace Knit.Cli.Output;

/// <summary>
/// Monochrome-safe priority glyphs for <c>list</c> / <c>tree</c>. Color is
/// deliberately not applied — the glyph is the signal, and CLI color always
/// strips under redirect/<c>--json</c>/<c>NO_COLOR</c>, so it could never be
/// load-bearing anyway. (Color-by-priority lives in the SPA only.)
/// </summary>
public static class PriorityFormat
{
    public static string Glyph(Priority p) => p switch
    {
        Priority.Critical => "▲▲",
        Priority.Important => "▲",
        Priority.Normal => "·",
        Priority.Someday => "▽",
        _ => "·",
    };

    /// <summary>Level for a dedicated list column: glyph + word for the
    /// signal levels, word-only for <c>Normal</c> (the implicit default gets
    /// no glyph, so the signal levels stand out — mirrors the SPA + tree).</summary>
    public static string Cell(Priority p) =>
        p == Priority.Normal ? p.ToWire() : $"{Glyph(p)} {p.ToWire()}";

    /// <summary>Trailing glyph for a tree node — empty for <c>Normal</c> so
    /// the common case doesn't clutter every line.</summary>
    public static string TreeSuffix(Priority p) =>
        p == Priority.Normal ? string.Empty : Glyph(p);
}
