using System.Text;

namespace Knit.Core.Ids;

/// <summary>What <see cref="Slug.Describe"/> had to change, beyond the
/// lowercase-and-dash normalization every caller expects.</summary>
[Flags]
public enum SlugChange
{
    None = 0,
    /// <summary>Diacritics folded to ASCII (ü → u).</summary>
    Folded = 1,
    /// <summary>Punctuation replaced by the separator (a.b → a-b).</summary>
    Stripped = 2,
    /// <summary>Cut at the 40-char cap.</summary>
    Truncated = 4,
    /// <summary>Nothing usable was left; the slug is "untitled".</summary>
    Fallback = 8,
}

/// <summary>A slug plus the reason it differs from its input.</summary>
public readonly record struct SlugResult(string Value, SlugChange Change)
{
    public bool Changed => Change != SlugChange.None;

    /// <summary>Human phrase for the change, or null when there is none.</summary>
    public string? Reason
    {
        get
        {
            if (!Changed) return null;
            var parts = new List<string>(4);
            if (Change.HasFlag(SlugChange.Folded)) parts.Add("folded accents");
            if (Change.HasFlag(SlugChange.Stripped)) parts.Add("stripped punctuation");
            if (Change.HasFlag(SlugChange.Truncated)) parts.Add($"truncated to {Slug.MaxLen} chars");
            if (Change.HasFlag(SlugChange.Fallback)) parts.Add("nothing usable left");
            return string.Join(", ", parts);
        }
    }
}

public static class Slug
{
    internal const int MaxLen = 40;

    // Hand-rolled ASCII fold for common Latin diacritics. Independent of
    // ICU, so works under InvariantGlobalization.
    private static readonly Dictionary<char, string> Folds = new()
    {
        ['ä'] = "a", ['Ä'] = "a",
        ['ö'] = "o", ['Ö'] = "o",
        ['ü'] = "u", ['Ü'] = "u",
        ['ß'] = "ss",
        ['á'] = "a", ['à'] = "a", ['â'] = "a", ['ã'] = "a", ['å'] = "a",
        ['Á'] = "a", ['À'] = "a", ['Â'] = "a", ['Ã'] = "a", ['Å'] = "a",
        ['é'] = "e", ['è'] = "e", ['ê'] = "e", ['ë'] = "e",
        ['É'] = "e", ['È'] = "e", ['Ê'] = "e", ['Ë'] = "e",
        ['í'] = "i", ['ì'] = "i", ['î'] = "i", ['ï'] = "i",
        ['Í'] = "i", ['Ì'] = "i", ['Î'] = "i", ['Ï'] = "i",
        ['ó'] = "o", ['ò'] = "o", ['ô'] = "o", ['õ'] = "o", ['ø'] = "o",
        ['Ó'] = "o", ['Ò'] = "o", ['Ô'] = "o", ['Õ'] = "o", ['Ø'] = "o",
        ['ú'] = "u", ['ù'] = "u", ['û'] = "u",
        ['Ú'] = "u", ['Ù'] = "u", ['Û'] = "u",
        ['ñ'] = "n", ['Ñ'] = "n",
        ['ç'] = "c", ['Ç'] = "c",
        ['æ'] = "ae", ['Æ'] = "ae",
        ['œ'] = "oe", ['Œ'] = "oe",
        ['ý'] = "y", ['ÿ'] = "y",
    };

    public static string From(string title) => Describe(title).Value;

    /// <summary>
    /// Slugify, and say what had to be changed beyond lowercasing and
    /// turning spaces into dashes. Callers that mint an <em>addressable</em>
    /// id (kb entries, slates) report the difference so the caller isn't left
    /// guessing why the id it asked for isn't the id it got.
    /// </summary>
    public static SlugResult Describe(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return new SlugResult("untitled", SlugChange.Fallback);

        var sb = new StringBuilder(title.Length);
        var lastWasDash = false;
        var change = SlugChange.None;

        foreach (var ch in title)
        {
            if (Folds.TryGetValue(ch, out var folded))
            {
                change |= SlugChange.Folded;
                foreach (var fc in folded)
                {
                    AppendChar(sb, fc, ref lastWasDash, ref change);
                }
                continue;
            }
            AppendChar(sb, ch, ref lastWasDash, ref change);
        }

        var result = sb.ToString().Trim('-');
        if (result.Length == 0) return new SlugResult("untitled", change | SlugChange.Fallback);
        if (result.Length > MaxLen)
        {
            result = result[..MaxLen].TrimEnd('-');
            change |= SlugChange.Truncated;
        }
        return result.Length == 0
            ? new SlugResult("untitled", change | SlugChange.Fallback)
            : new SlugResult(result, change);
    }

    private static void AppendChar(StringBuilder sb, char ch, ref bool lastWasDash, ref SlugChange change)
    {
        var c = ch;
        if (c is >= 'A' and <= 'Z') c = (char)(c + 32);

        if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            sb.Append(c);
            lastWasDash = false;
            return;
        }

        // Whitespace and a literal dash become the separator everybody
        // expects; anything else is punctuation the caller loses.
        if (!char.IsWhiteSpace(c) && c != '-') change |= SlugChange.Stripped;

        if (!lastWasDash && sb.Length > 0)
        {
            sb.Append('-');
            lastWasDash = true;
        }
    }
}
