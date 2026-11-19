using System.Globalization;
using System.Text;

namespace Knit.Core.Frontmatter;

/// <summary>
/// Hand-rolled YAML reader/writer scoped to the shape Knit frontmatter
/// uses: a flat map of scalar values plus a single 'links' list of small
/// objects. AOT-clean (no reflection). Handles enough YAML to be useful
/// without trying to be a full implementation.
/// </summary>
public sealed class SimpleFrontmatterSerializer : IFrontmatterSerializer
{
    public Dictionary<string, object?> Parse(string yaml)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(yaml)) return result;

        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                i++;
                continue;
            }

            // Top-level key: must have no leading whitespace
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                i++;
                continue;
            }

            var colon = FindKeyColon(line);
            if (colon < 0) { i++; continue; }

            var key = line[..colon].Trim();
            var rest = line[(colon + 1)..].TrimStart();

            if (string.IsNullOrEmpty(rest))
            {
                // Block scalar / list following on indented lines
                var (value, advance) = ParseIndentedBlock(lines, i + 1);
                result[key] = value;
                i += 1 + advance;
            }
            else
            {
                result[key] = ParseScalarOrFlow(rest);
                i++;
            }
        }
        return result;
    }

    public string Write(IReadOnlyDictionary<string, object?> fields)
    {
        if (fields.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var (key, value) in fields)
        {
            WriteEntry(sb, key, value);
        }
        return sb.ToString();
    }

    private static void WriteEntry(StringBuilder sb, string key, object? value)
    {
        if (value is List<object?> list)
        {
            sb.Append(key).Append(":\n");
            foreach (var entry in list)
            {
                sb.Append("  - ");
                if (entry is IReadOnlyDictionary<string, object?> map)
                {
                    sb.Append('{');
                    var first = true;
                    foreach (var (k, v) in map)
                    {
                        if (!first) sb.Append(", ");
                        sb.Append(k).Append(": ").Append(FormatScalar(v));
                        first = false;
                    }
                    sb.Append('}');
                }
                else
                {
                    sb.Append(FormatScalar(entry));
                }
                sb.Append('\n');
            }
            return;
        }
        if (value is IReadOnlyDictionary<string, object?> nestedMap)
        {
            sb.Append(key).Append(": {");
            var first = true;
            foreach (var (k, v) in nestedMap)
            {
                if (!first) sb.Append(", ");
                sb.Append(k).Append(": ").Append(FormatScalar(v));
                first = false;
            }
            sb.Append("}\n");
            return;
        }
        if (value is null) { sb.Append(key).Append(":\n"); return; }
        sb.Append(key).Append(": ").Append(FormatScalar(value)).Append('\n');
    }

    private static string FormatScalar(object? v)
    {
        if (v is null) return "";
        if (v is bool b) return b ? "true" : "false";
        if (v is int or long or double or float)
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";

        var s = v.ToString() ?? "";
        if (NeedsQuoting(s)) return QuoteString(s);
        return s;
    }

    private static bool NeedsQuoting(string s)
    {
        if (s.Length == 0) return true;
        if (s == "true" || s == "false" || s == "null" || s == "~") return true;
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) return true;
        if (s[0] is '-' or '[' or '{' or '"' or '\'' or '*' or '&' or '!' or '|' or '>' or '%' or '@' or '`' or '#' or ',') return true;
        if (s.Contains('\n') || s.Contains('\t')) return true;
        // YAML allows ':' in plain scalars unless followed by space/EOL — required
        // for unquoted refs like 'item:<uuid>'. Quote when colon-then-space is present.
        for (var i = 0; i < s.Length - 1; i++)
            if (s[i] == ':' && (s[i + 1] == ' ' || s[i + 1] == '\t')) return true;
        if (s.Length > 0 && s[^1] == ':') return true;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return true;
        return false;
    }

    private static string QuoteString(string s)
    {
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
        return "\"" + escaped + "\"";
    }

    /// <summary>Find ':' that separates key from value, ignoring colons inside quotes.</summary>
    private static int FindKeyColon(string line)
    {
        var inQuote = false;
        var quote = '"';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuote)
            {
                if (c == quote) inQuote = false;
                continue;
            }
            if (c is '"' or '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }
            if (c == ':')
            {
                if (i + 1 == line.Length || line[i + 1] == ' ' || line[i + 1] == '\t')
                    return i;
            }
        }
        return -1;
    }

    private static object? ParseScalarOrFlow(string text)
    {
        text = text.Trim();
        // Strip trailing comment
        var commentIdx = FindUnquotedHash(text);
        if (commentIdx >= 0) text = text[..commentIdx].TrimEnd();
        if (string.IsNullOrEmpty(text)) return null;

        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
            return UnescapeQuoted(text[1..^1]);
        if (text.StartsWith('\'') && text.EndsWith('\'') && text.Length >= 2)
            return text[1..^1].Replace("''", "'");

        if (text.StartsWith('[') && text.EndsWith(']'))
            return ParseFlowList(text[1..^1]);
        if (text.StartsWith('{') && text.EndsWith('}'))
            return ParseFlowMap(text[1..^1]);

        if (text == "true") return true;
        if (text == "false") return false;
        if (text == "null" || text == "~") return null;

        return text;
    }

    private static int FindUnquotedHash(string text)
    {
        var inQuote = false;
        var quote = '"';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuote)
            {
                if (c == quote) inQuote = false;
                continue;
            }
            if (c is '"' or '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }
            if (c == '#' && (i == 0 || text[i - 1] == ' ' || text[i - 1] == '\t')) return i;
        }
        return -1;
    }

    private static string UnescapeQuoted(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    var c => c,
                });
                i++;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static (object? value, int advance) ParseIndentedBlock(string[] lines, int start)
    {
        var i = start;
        var listItems = new List<object?>();
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        var mode = BlockMode.Unknown;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- "))
                {
                    if (mode == BlockMode.Unknown) mode = BlockMode.List;
                    if (mode != BlockMode.List) break;
                    listItems.Add(ParseScalarOrFlow(trimmed[2..]));
                    i++;
                    continue;
                }
                var colon = FindKeyColon(trimmed);
                if (colon > 0)
                {
                    if (mode == BlockMode.Unknown) mode = BlockMode.Map;
                    if (mode != BlockMode.Map) break;
                    var k = trimmed[..colon].Trim();
                    var rest = trimmed[(colon + 1)..].TrimStart();
                    dict[k] = string.IsNullOrEmpty(rest) ? null : ParseScalarOrFlow(rest);
                    i++;
                    continue;
                }
                i++;
            }
            else
            {
                break; // dedent
            }
        }

        return mode switch
        {
            BlockMode.List => (listItems, i - start),
            BlockMode.Map => (dict, i - start),
            _ => (null, 0),
        };
    }

    private enum BlockMode { Unknown, List, Map }

    private static List<object?> ParseFlowList(string body)
    {
        var items = new List<object?>();
        foreach (var part in SplitFlow(body, ','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            items.Add(ParseScalarOrFlow(trimmed));
        }
        return items;
    }

    private static Dictionary<string, object?> ParseFlowMap(string body)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var part in SplitFlow(body, ','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            var colon = FindKeyColon(trimmed);
            if (colon <= 0) continue;
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            map[key] = ParseScalarOrFlow(value);
        }
        return map;
    }

    private static IEnumerable<string> SplitFlow(string body, char sep)
    {
        var depth = 0;
        var inQuote = false;
        var quote = '"';
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (inQuote)
            {
                if (c == quote) inQuote = false;
                continue;
            }
            if (c is '"' or '\'') { inQuote = true; quote = c; continue; }
            if (c is '[' or '{') depth++;
            else if (c is ']' or '}') depth--;
            else if (c == sep && depth == 0)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
        if (start < body.Length) yield return body[start..];
    }
}
