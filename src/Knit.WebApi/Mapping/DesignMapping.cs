using Knit.Core.Designs;

namespace Knit.WebApi.Mapping;

public static class DesignMapping
{
    public static string TitleOf(string slug, IReadOnlyDictionary<string, object?> fm, string body)
        => DesignMeta.TitleOf(slug, fm, body);

    public static string? FirstHeading(string body) => DesignMeta.FirstHeading(body);

    public static string ModifiedIso(string path) => FileTime.ModifiedIso(path);
}
