namespace Knit.Core.Refs;

/// <summary>
/// Single source of truth for bucket->folder name mapping.
/// Buckets are singular nouns; folders are English-natural plurals.
/// </summary>
public static class BucketFolders
{
    public const string Items    = "items";
    public const string Adrs     = "adrs";
    public const string Docs     = "docs";
    public const string Features = "features";
    public const string Logs     = "logs";
    public const string Ideas    = "ideas";
    public const string Sources  = "sources";

    public static string Folder(this Bucket b) => b switch
    {
        Bucket.Item    => Items,
        Bucket.Adr     => Adrs,
        Bucket.Doc     => Docs,
        Bucket.Feature => Features,
        Bucket.Log     => Logs,
        Bucket.Idea    => Ideas,
        Bucket.Source  => Sources,
        _ => throw new ArgumentOutOfRangeException(nameof(b), b, "unknown bucket"),
    };

    public static bool TryFromFolder(string folder, out Bucket bucket)
    {
        switch (folder)
        {
            case Items:    bucket = Bucket.Item;    return true;
            case Adrs:     bucket = Bucket.Adr;     return true;
            case Docs:     bucket = Bucket.Doc;     return true;
            case Features: bucket = Bucket.Feature; return true;
            case Logs:     bucket = Bucket.Log;     return true;
            case Ideas:    bucket = Bucket.Idea;    return true;
            case Sources:  bucket = Bucket.Source;  return true;
            default:       bucket = Bucket.Item;    return false;
        }
    }

    /// <summary>
    /// True when single-file entries may grow into a folder containing
    /// <c>readme.md</c>. Resolver falls back to the folder form for these.
    /// A per-bucket opt-in, not a KB-wide rule: ADRs are numbered and
    /// chronological, logs are time-ordered and ideas are a scratch list —
    /// none of them accumulate the unbounded nested material that features,
    /// docs and sources do.
    /// </summary>
    public static bool AllowsGrowToFolder(this Bucket b) =>
        b is Bucket.Feature or Bucket.Doc or Bucket.Source;

    /// <summary>The grow-to-folder buckets, for error messages.</summary>
    public const string GrowToFolderBuckets = "feature, doc, source";
}
