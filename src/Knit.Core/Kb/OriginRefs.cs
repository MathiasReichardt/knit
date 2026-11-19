using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Validates + canonicalizes ADR <c>origin:</c> refs — the <c>design:&lt;slug&gt;</c>
/// / <c>item:&lt;id&gt;</c> tokens the decision emerged from. Shared by the CLI
/// (<c>kb add adr --origin</c>) and the web PATCH path so both reject the same
/// bad input identically. Returns the deduped, canonical ref strings, or an
/// error message on the first invalid / unresolvable token.
/// </summary>
public static class OriginRefs
{
    public static (string[] Origins, string? Error) Normalize(string root, IEnumerable<string> raw)
    {
        var result = new List<string>();
        foreach (var token in raw)
        {
            var t = token.Trim();
            if (t.Length == 0) continue;
            var parsed = Ref.Parse(t);
            if (parsed.IsError)
                return ([], $"invalid origin '{t}': {parsed.GetErrorOrDefault()!.Message}");
            var r = parsed.GetValueOrThrow();
            if (r.Bucket is not (Bucket.Design or Bucket.Item))
                return ([], $"origin '{t}' must be a design:<slug> or item:<id> ref");
            var resolved = RefResolver.Resolve(r, root).GetValueOrThrow();
            if (!resolved.Exists)
                return ([], $"origin '{t}' does not resolve (no file at {resolved.Path})");
            var canonical = resolved.Ref.ToString();
            if (!result.Contains(canonical, StringComparer.Ordinal)) result.Add(canonical);
        }
        return (result.ToArray(), null);
    }
}
