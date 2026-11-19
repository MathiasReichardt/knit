using System.Text;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Ids;
using Knit.Core.Refs;

namespace Knit.Cli.Output;

/// <summary>
/// JSON emitter for <c>knit kb ...</c> commands. Hand-written via
/// <see cref="Utf8JsonWriter"/> because frontmatter shape is dynamic
/// per bucket.
/// </summary>
public static class KbJson
{
    public static void WriteArtifact(
        Ref r,
        string path,
        IReadOnlyDictionary<string, object?> frontmatter,
        string? body = null,
        SlugResult? mint = null,
        string? mintInput = null,
        MountResolver.SourceTarget? target = null)
    {
        using var stream = new MemoryStream();
        var opts = new JsonWriterOptions { Indented = true };
        using (var w = new Utf8JsonWriter(stream, opts))
        {
            w.WriteStartObject();
            w.WriteString("ref", r.ToString());
            w.WriteString("path", path);
            w.WritePropertyName("frontmatter");
            WriteValue(w, frontmatter);
            if (body is not null) w.WriteString("body", body);
            // A mount-relative `path` is unusable on its own — a reader must
            // never have to join it with config by hand.
            if (target is not null)
            {
                if (target.Reason is null) w.WriteString("resolved_path", target.FullPath);
                else w.WriteString("mount_reason", target.Reason);
            }
            // The id was not what the caller asked for. Fields, not
            // prose, so an agent can pick the real id up without parsing.
            if (mint is { Changed: true } m)
            {
                w.WriteBoolean("id_trimmed", true);
                w.WriteString("id_input", mintInput ?? "");
                w.WriteString("id_reason", m.Reason!);
            }
            w.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public static void WriteList(IEnumerable<(Ref Ref, string Path, IReadOnlyDictionary<string, object?> Frontmatter)> items)
    {
        using var stream = new MemoryStream();
        var opts = new JsonWriterOptions { Indented = true };
        using (var w = new Utf8JsonWriter(stream, opts))
        {
            w.WriteStartObject();
            w.WriteStartArray("items");
            foreach (var (r, path, fm) in items)
            {
                w.WriteStartObject();
                w.WriteString("ref", r.ToString());
                w.WriteString("path", path);
                w.WritePropertyName("frontmatter");
                WriteValue(w, fm);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.WriteNullValue();
                break;
            case string s:
                w.WriteStringValue(s);
                break;
            case bool b:
                w.WriteBooleanValue(b);
                break;
            case int i:
                w.WriteNumberValue(i);
                break;
            case long l:
                w.WriteNumberValue(l);
                break;
            case double d:
                w.WriteNumberValue(d);
                break;
            case IReadOnlyDictionary<string, object?> map:
                w.WriteStartObject();
                foreach (var (k, v) in map)
                {
                    w.WritePropertyName(k);
                    WriteValue(w, v);
                }
                w.WriteEndObject();
                break;
            case IDictionary<string, object?> mutMap:
                w.WriteStartObject();
                foreach (var k in mutMap.Keys)
                {
                    w.WritePropertyName(k);
                    WriteValue(w, mutMap[k]);
                }
                w.WriteEndObject();
                break;
            case System.Collections.IEnumerable list:
                w.WriteStartArray();
                foreach (var item in list)
                    WriteValue(w, item);
                w.WriteEndArray();
                break;
            default:
                w.WriteStringValue(value.ToString());
                break;
        }
    }
}
