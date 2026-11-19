namespace Knit.Core.Refs;

public enum Bucket
{
    Item,
    Adr,
    Doc,
    Feature,
    Log,
    Idea,
    Source,
    // Designs live under .knit/designs/, not .knit/kb/ — Design is a ref
    // address space (parse / resolve / verify), deliberately absent from
    // `All` so KB enumeration never treats it as a KB bucket.
    Design,
    // Project doc folders live under .knit/projects/ — same design-style
    // ref address space, absent from `All`. `project:<slug>` resolves to
    // the folder's readme.md.
    Project,
}

public static class BucketStrings
{
    public static string ToWire(this Bucket b) => b switch
    {
        Bucket.Item => "item",
        Bucket.Adr => "adr",
        Bucket.Doc => "doc",
        Bucket.Feature => "feature",
        Bucket.Log => "log",
        Bucket.Idea => "idea",
        Bucket.Source => "source",
        Bucket.Design => "design",
        Bucket.Project => "project",
        _ => throw new ArgumentOutOfRangeException(nameof(b), b, "unknown bucket"),
    };

    public static bool TryParse(string? s, out Bucket result)
    {
        switch ((s ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "item": result = Bucket.Item; return true;
            case "adr": result = Bucket.Adr; return true;
            case "doc": result = Bucket.Doc; return true;
            case "feature": result = Bucket.Feature; return true;
            case "log": result = Bucket.Log; return true;
            case "idea": result = Bucket.Idea; return true;
            case "source": result = Bucket.Source; return true;
            case "design": result = Bucket.Design; return true;
            case "project": result = Bucket.Project; return true;
            default: result = Bucket.Item; return false;
        }
    }

    public static IReadOnlyList<Bucket> All { get; } =
    [
        Bucket.Item, Bucket.Adr, Bucket.Doc, Bucket.Feature,
        Bucket.Log, Bucket.Idea, Bucket.Source,
    ];
}
