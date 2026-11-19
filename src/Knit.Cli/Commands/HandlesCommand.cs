using System.CommandLine;
using Knit.Core.Domain;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// The handles already written into this repo, so an agent assigns to an
/// existing spelling instead of inventing a third one. Derived from
/// the items, not a registry: authority over who is a real person is M2's hub.
/// </summary>
public static class HandlesCommand
{
    public static Command Build()
    {
        var cmd = new Command(
            "handles",
            "List the handles in use: every distinct assignee and comment author, "
            + "with how many items each is assigned and how many comments each wrote. "
            + "Check it before assigning to someone else. Your own handle is marked "
            + "and always listed — it comes from config, not from this list.");

        cmd.SetRepoAction((ctx, parse) =>
        {
            var rows = Census(ctx.Store.AllItems(), ctx.Config.Handle);

            if (parse.GetValue(GlobalOptions.Json))
            {
                Out.Json(rows, AgentJsonContext.Default.ListJsonHandle);
                return 0;
            }

            if (rows.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]no handles[/]");
                return 0;
            }
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumns("handle", "assigned", "comments");
            foreach (var r in rows)
                table.AddRow(
                    new Markup(Markup.Escape(r.Handle) + (r.Me ? " [dim](you)[/]" : "")),
                    new Markup(r.Assigned.ToString()),
                    new Markup(r.Comments.ToString()));
            AnsiConsole.Write(table);
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// Exact (ordinal) match, like <c>--assignee</c>: two spellings of one
    /// person are two rows, which is the point — the near-duplicate is what the
    /// reader is here to spot. Your own handle is listed even at zero, so a
    /// configured handle nobody has written yet reads as yours rather than as
    /// absent, and sits beside any near-spelling that suggests config drift.
    /// </summary>
    internal static List<JsonHandle> Census(IEnumerable<Item> items, string? me)
    {
        var counts = new Dictionary<string, (int Assigned, int Comments)>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            Bump(item.Assignee, assigned: true);
            foreach (var comment in item.Comments) Bump(comment.Author, assigned: false);
        }
        var self = me?.Trim();
        if (!string.IsNullOrEmpty(self)) counts.TryAdd(self, (0, 0));

        return
        [
            .. counts
                .Select(kv => new JsonHandle(kv.Key, kv.Value.Assigned, kv.Value.Comments, kv.Key == self))
                .OrderByDescending(h => h.Assigned + h.Comments)
                .ThenBy(h => h.Handle, StringComparer.Ordinal),
        ];

        void Bump(string? raw, bool assigned)
        {
            var h = raw?.Trim();
            if (string.IsNullOrEmpty(h)) return;
            var c = counts.GetValueOrDefault(h);
            counts[h] = assigned ? (c.Assigned + 1, c.Comments) : (c.Assigned, c.Comments + 1);
        }
    }
}
