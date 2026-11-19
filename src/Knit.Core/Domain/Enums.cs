namespace Knit.Core.Domain;

// Ordinal order is altitude (top → leaf) — `--sort type` relies on it.
public enum ItemType
{
    Project,
    Epic,
    Feature,
    Bug,
    Task,
    Todo,
}

// Ordinal order is the lifecycle, dismissed last — `--sort state:desc` relies on it.
public enum ItemState
{
    Draft,
    Todo,
    InProgress,
    Review,
    Done,
    Dismissed,
}

public sealed class KnitParseException : Exception
{
    public KnitParseException(string message) : base(message) { }
}

public static class EnumStrings
{
    public static string ToWire(this ItemType t) => t switch
    {
        ItemType.Project => "project",
        ItemType.Epic => "epic",
        ItemType.Feature => "feature",
        ItemType.Bug => "bug",
        ItemType.Task => "task",
        ItemType.Todo => "todo",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "unknown item type"),
    };

    public static bool TryParseItemType(string? s, out ItemType result)
    {
        switch ((s ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "project": result = ItemType.Project; return true;
            case "epic": result = ItemType.Epic; return true;
            case "feature": result = ItemType.Feature; return true;
            case "bug": result = ItemType.Bug; return true;
            case "task": result = ItemType.Task; return true;
            case "todo": result = ItemType.Todo; return true;
            default: result = ItemType.Task; return false;
        }
    }

    public static string ToWire(this ItemState s) => s switch
    {
        ItemState.Draft => "draft",
        ItemState.Todo => "todo",
        ItemState.InProgress => "in-progress",
        ItemState.Review => "review",
        ItemState.Done => "done",
        ItemState.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown item state"),
    };

    /// <summary>
    /// Tolerant parse: unknown values map to <see cref="ItemState.Todo"/>
    /// and return false. Use this from CLI/API input where the layer above
    /// renders the user-facing error. Disk-read uses <see cref="ParseItemStateStrict"/>
    /// so a legacy <c>state: blocked</c> surfaces as a hard error.
    /// </summary>
    public static bool TryParseItemState(string? s, out ItemState result)
    {
        switch ((s ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "draft": result = ItemState.Draft; return true;
            case "todo": result = ItemState.Todo; return true;
            case "in-progress":
            case "in_progress":
            case "inprogress": result = ItemState.InProgress; return true;
            case "review": result = ItemState.Review; return true;
            case "done": result = ItemState.Done; return true;
            case "dismissed": result = ItemState.Dismissed; return true;
            // 'blocked' is intentionally rejected; callers wanting a hard
            // error use ParseItemStateStrict.
            case "blocked": result = ItemState.Todo; return false;
            default: result = ItemState.Todo; return false;
        }
    }

    /// <summary>
    /// Throws <see cref="KnitParseException"/> on legacy <c>blocked</c>
    /// or unknown values. Used by the on-disk reader so stale frontmatter
    /// surfaces loudly instead of degrading to <see cref="ItemState.Todo"/>.
    /// </summary>
    public static ItemState ParseItemStateStrict(string? s)
    {
        var normalized = (s ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => ItemState.Draft,
            "" or "todo" => ItemState.Todo,
            "in-progress" or "in_progress" or "inprogress" => ItemState.InProgress,
            "review" => ItemState.Review,
            "done" => ItemState.Done,
            "dismissed" => ItemState.Dismissed,
            "blocked" => throw new KnitParseException(
                "'blocked' is no longer a valid state; use the orthogonal `blocked` flag"),
            _ => throw new KnitParseException($"unknown item state: {s}"),
        };
    }

    // Spectre markup is preserved when color is on, auto-stripped on
    // --no-color / NO_COLOR / redirected stdout. Glyphs themselves stay
    // monochrome-safe single-width unicode so layout doesn't shift.
    public static string Icon(this ItemType t) => t switch
    {
        ItemType.Project => "[cyan]◆[/]",
        ItemType.Epic => "[magenta]▣[/]",
        ItemType.Feature => "[blue]◈[/]",
        ItemType.Bug => "[red]▲[/]",
        ItemType.Task => "[grey]▸[/]",
        ItemType.Todo => "[grey]•[/]",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "unknown item type"),
    };

    public static string Glyph(this ItemState s) => s switch
    {
        ItemState.Draft => "[grey]✎[/]",
        ItemState.Todo => "○",
        ItemState.InProgress => "[yellow]◐[/]",
        ItemState.Review => "[magenta]⚯[/]",
        ItemState.Done => "[green]✓[/]",
        ItemState.Dismissed => "[grey]⊘[/]",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "unknown item state"),
    };
}
