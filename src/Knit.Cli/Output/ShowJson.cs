using System.Text;
using System.Text.Json;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Cli.Output;

/// <summary>
/// JSON writer for <c>knit show --json</c>. Hand-written via
/// <see cref="Utf8JsonWriter"/> because the section keys (h2 headings)
/// are dynamic — there's no static schema source-gen can target.
/// </summary>
public static class ShowJson
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "id", "type", "state", "title", "assignee", "parent", "rank",
        "effort", "due", "priority", "health", "project", "created", "modified",
        "links", "comments", "blocked", "tags", "design", "slate",
    };

    public static void Write(
        Item item, string rootPath, string? effectiveSlate = null, string? heldBy = null, string? effectiveMilestone = null)
    {
        using var stream = new MemoryStream();
        var opts = new JsonWriterOptions { Indented = true };
        using (var w = new Utf8JsonWriter(stream, opts))
            WriteTo(w, item, rootPath, effectiveSlate, heldBy, effectiveMilestone);
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>
    /// The same object, written into a caller-supplied writer so a dump can
    /// nest it in an array or a tree node. `--dump` must not invent a
    /// second JSON shape for an item.
    /// </summary>
    public static void WriteTo(
        Utf8JsonWriter w, Item item, string rootPath, string? effectiveSlate = null, string? heldBy = null,
        string? effectiveMilestone = null)
    {
        w.WriteStartObject();
        w.WriteString("id", item.Id);
        w.WriteString("type", item.Type.ToWire());
        w.WriteString("state", item.State.ToWire());
        w.WriteString("title", item.Title);
        if (!string.IsNullOrEmpty(item.Assignee)) w.WriteString("assignee", item.Assignee);
        if (!string.IsNullOrEmpty(item.Parent)) w.WriteString("parent", item.Parent);
        if (!string.IsNullOrEmpty(item.Rank)) w.WriteString("rank", item.Rank);
        if (!string.IsNullOrEmpty(item.Effort)) w.WriteString("effort", item.Effort);
        if (item.Due is { } due) w.WriteString("due", DueDates.ToWire(due));
        // Emitted always (even the Normal default) so scripts get the
        // resolved value, matching the list/tree --json shape.
        w.WriteString("priority", item.Priority.ToWire());
        if (item.Health is { } health) w.WriteString("health", health.ToWire());
        if (!string.IsNullOrEmpty(item.Project)) w.WriteString("project", item.Project);
        if (!string.IsNullOrEmpty(item.Design)) w.WriteString("design", item.Design);
        if (!string.IsNullOrEmpty(item.Slate)) w.WriteString("slate", item.Slate);
        if (!string.IsNullOrEmpty(effectiveSlate)) w.WriteString("effective_slate", effectiveSlate);
        if (!string.IsNullOrEmpty(effectiveMilestone)) w.WriteString("effective_milestone", effectiveMilestone);
        if (item.BlockedSummary is { } blocked) w.WriteString("blocked", blocked);
        if (heldBy is not null) w.WriteString("held_by", heldBy);
        w.WriteString("created", item.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        w.WriteString("modified",
            AgentMapper.ResolveModified(rootPath, item).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        w.WriteStartArray("tags");
        foreach (var tag in item.Tags) w.WriteStringValue(tag);
        w.WriteEndArray();

        w.WriteStartArray("links");
        foreach (var r in item.LinkRows)
        {
            w.WriteStartObject();
            w.WriteString("rel", LinkRels.ToWire(r.Rel));
            w.WriteString("target_text", r.TargetText);
            w.WriteString("target_path", r.TargetPath);
            if (!string.IsNullOrEmpty(r.Notes)) w.WriteString("notes", r.Notes);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        foreach (var (rawKey, value) in BodySections.Split(item.Body))
        {
            var key = Reserved.Contains(rawKey) ? "section_" + rawKey : rawKey;
            w.WriteString(key, value);
        }

        w.WriteStartArray("comments");
        foreach (var c in item.Comments)
        {
            w.WriteStartObject();
            w.WriteString("id", c.Id);
            w.WriteString("author", c.Author);
            w.WriteString("created", c.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            w.WriteString("body", c.Body);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
    }
}
