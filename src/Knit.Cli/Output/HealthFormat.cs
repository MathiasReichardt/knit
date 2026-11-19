using Knit.Core.Domain;

namespace Knit.Cli.Output;

/// <summary>
/// Human rendering of the project health signal. Deliberately *words*, not
/// glyphs: health lives on project items only, so it appears on a handful of
/// rows, and the three levels are far easier to tell apart spelled out than as
/// three near-identical shapes once color is stripped. The glyph budget in
/// <c>list</c>/<c>tree</c> rows is already spent on type, state, blocked and
/// priority.
///
/// <para>No signal (<c>null</c>) renders as nothing — it is not the same as
/// <c>on-track</c>, which is an assessment someone made.</para>
/// </summary>
public static class HealthFormat
{
    /// <summary>Value for a dedicated column; em dash when unset.</summary>
    public static string Cell(Health? h) => h is { } v ? v.ToWire() : "—";

    /// <summary>Trailing marker for a list/tree row — empty when unset.</summary>
    public static string Suffix(Health? h) => h is { } v ? $" [dim]({v.ToWire()})[/]" : string.Empty;
}
