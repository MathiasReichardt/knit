using System.CommandLine;
using System.Text;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Refs;
using Knit.Core.Sharing;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit url &lt;ref&gt;</c> — the share link: the doc's blob URL
/// on the default branch of the repo's forge, or <c>&lt;repo_name&gt;:&lt;path&gt;</c>
/// without an origin. <c>knit link</c> is taken by relations.
/// </summary>
public static class UrlCommand
{
    public static Command Build()
    {
        var cmd = new Command("url",
            "Print the share link to an item, KB entry, design or .knit/… doc — its file on the forge's default branch");
        var refArg = new Argument<string>("ref") { Description = "An item id or short id, bucket:slug, design:<slug>, project:<slug> or a .knit/… path" };
        cmd.Arguments.Add(refArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var raw = parse.GetValue(refArg)!.Trim();
            if (FileOf(ctx, raw, out var error) is not { } path) return Out.Error(error!, ErrorCode.NotFound, json);

            var origin = ShareLinks.Origin(ctx.Root, ctx.Resolution.GitRoot, ctx.Config);
            var link = ShareLinks.Link(origin, ctx.Root, path);
            if (link.Url is null) return Out.Error(origin.Error!, ErrorCode.Conflict, json);

            if (json)
            {
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteString("url", link.Url);
                    if (origin.Forge is { } f) w.WriteString("forge", Forges.Name(f.Kind));
                    else w.WriteNull("forge");
                    w.WriteString("ref", Forges.Ref);
                    w.WriteString("path", link.Path);
                    w.WriteEndObject();
                }
                Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }
            // Bare on stdout, so `$(knit url …)` captures the link alone.
            Console.WriteLine(link.Url);
            return 0;
        });
        return cmd;
    }

    private static string? FileOf(CliContext ctx, string raw, out string? error)
    {
        error = null;
        if (DocRef.TryParse(raw, out var d) && (d.Ref is null || d.Ref.Bucket != Bucket.Item))
        {
            if (d.Fragment is not null)
            {
                error = $"a share link names the doc — drop '#{d.Fragment}'";
                return null;
            }
            var (path, exists) = d.Resolve(ctx.Root);
            if (exists) return path;
            error = $"{d.Doc} not found";
            return null;
        }
        var (item, err) = ItemUtils.Resolve(ctx, raw);
        if (item is not null && ctx.Store.PathOf(item.Id) is { } file) return file;
        error = err ?? $"item not found: {raw}";
        return null;
    }
}
