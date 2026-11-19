using System.Text;
using System.Text.Json;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Cli.Output;

/// <summary>
/// The markdown body of <c>list --dump</c> / <c>tree --dump</c>:
/// an item's full contents, so an agent can read a slice in one call instead
/// of paying one <c>knit show</c> per item.
///
/// Not a reuse of <c>show</c>'s renderer: that one builds Spectre panels and
/// tables, which are terminal furniture. A dump is markdown that survives
/// being pasted, piped and re-read.
/// </summary>
public static class ItemDump
{
    /// <summary>
    /// Record separator. An opener only — the next one ends the previous item
    /// (decided with the user): a closing sentinel buys detection of a body
    /// that forges the opener, which no real body does, at the cost of a line
    /// per item in output whose whole problem is size.
    ///
    /// It carries the ids because that is what makes a dump better than
    /// `cat`-ing the folder: the agent can address anything it reads.
    /// </summary>
    public const string Rule = "=====";

    public static string Header(CliContext ctx, Item item, bool context = false)
    {
        var suffix = context ? " · context" : "";
        return $"{Rule} knit {item.Id} ({Out.Short(ctx, item.Id)}) · {item.Type.ToWire()} · {item.State.ToWire()}{suffix} {Rule}";
    }

    /// <summary>
    /// One item, header line included. <paramref name="context"/> marks a row
    /// the filter did not select and the tree kept only for structure — it is
    /// dumped in full anyway, because a feature's spec is what makes its tasks
    /// readable, and `--flat` is the lever for dropping it.
    /// </summary>
    public static void Write(StringBuilder sb, CliContext ctx, Item item, bool context = false)
    {
        sb.Append(Header(ctx, item, context)).Append('\n');
        sb.Append("title: ").Append(item.Title).Append('\n');
        Field(sb, "assignee", item.Assignee);
        if (item.Due is { } due) Field(sb, "due", DueDates.ToWire(due));
        Field(sb, "priority", item.Priority.ToWire());
        if (item.Health is { } health) Field(sb, "health", health.ToWire());
        Field(sb, "blocked", item.BlockedReason);
        if (!string.IsNullOrEmpty(item.Parent))
            Field(sb, "parent", $"{item.Parent} ({Out.Short(ctx, item.Parent)})");
        Field(sb, "slate", ctx.EffectiveSlate(item.Id));
        Field(sb, "milestone", ctx.EffectiveMilestone(item.Id));
        Field(sb, "project", item.Project);
        Field(sb, "design", item.Design);
        Field(sb, "effort", item.Effort);
        if (item.Tags.Count > 0) Field(sb, "tags", string.Join(", ", item.Tags));

        // The body goes in verbatim — it is already markdown, and rewriting it
        // is how a dump stops being the thing the agent would have read. The
        // one exception is the leading h1, which restates `title:` (same rule
        // as `show --json`).
        var body = StripTitleHeading(item.Body).TrimEnd();
        if (body.Length > 0) sb.Append('\n').Append(body).Append('\n');

        // `knit:`-prefixed so these can't be mistaken for the item's own h2
        // sections, which are arbitrary.
        if (item.LinkRows.Count > 0)
        {
            sb.Append("\n## knit:links\n");
            foreach (var r in item.LinkRows)
            {
                sb.Append("- ").Append(LinkRels.ToWire(r.Rel)).Append(" → ").Append(r.TargetText);
                if (!string.IsNullOrEmpty(r.TargetPath)) sb.Append(" (").Append(r.TargetPath).Append(')');
                if (!string.IsNullOrEmpty(r.Notes)) sb.Append(" — ").Append(r.Notes);
                sb.Append('\n');
            }
        }

        if (item.Comments.Count > 0)
        {
            sb.Append("\n## knit:comments\n");
            foreach (var c in item.Comments)
            {
                sb.Append("\n### ").Append(c.Created.UtcDateTime.ToString("yyyy-MM-dd HH:mm"))
                  .Append(" UTC · ").Append(c.Author).Append('\n');
                sb.Append(c.Body.TrimEnd()).Append('\n');
            }
        }

        sb.Append('\n');
    }

    /// <summary>
    /// <c>list --json --dump</c>: the same <c>items</c> envelope
    /// <c>list --json</c> uses, with each element in the <c>show --json</c>
    /// shape rather than the bodyless <c>JsonItem</c>. Returns the byte count
    /// so the caller can report what the dump cost.
    /// </summary>
    public static int WriteJsonArray(CliContext ctx, IReadOnlyList<Item> items)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("items");
            foreach (var i in items) ShowJson.WriteTo(w, i, ctx.Root, ctx.EffectiveSlate(i.Id), effectiveMilestone: ctx.EffectiveMilestone(i.Id));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var bytes = stream.ToArray();
        Console.WriteLine(Encoding.UTF8.GetString(bytes));
        return bytes.Length;
    }

    private static string StripTitleHeading(string body)
    {
        var lines = body.Split('\n');
        var i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length || !lines[i].StartsWith("# ", StringComparison.Ordinal)) return body;
        i++;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        return string.Join('\n', lines[i..]);
    }

    private static void Field(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        sb.Append(key).Append(": ").Append(value).Append('\n');
    }
}
