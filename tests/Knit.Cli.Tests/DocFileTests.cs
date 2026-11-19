using System.Text.Json;
using Knit.Cli.Output;

namespace Knit.Cli.Tests;

/// <summary>Files next to a doc (an SVG, an image, a PDF): verify findings, moves, the SVG outline.</summary>
[Collection(nameof(CliCollection))]
public class DocFileTests
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
          <desc>Orders flow from the shop to billing.</desc>
          <g data-kind="group" id="backend">
            <title>Backend</title>
            <g data-kind="node" id="shop" transform="translate(8,8)"><title>Shop</title><rect width="40" height="24"/></g>
            <g data-kind="node" id="billing" transform="translate(96,8)"><title>Billing</title><rect width="40" height="24"/></g>
          </g>
          <path data-kind="edge" id="e1" data-from="shop" data-to="billing" d="M48,20 L96,20"><title>order placed</title></path>
        </svg>
        """;

    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        return root;
    }

    private static string Doc(TempKnitRoot root, string slug)
    {
        Assert.Equal(0, root.Run("kb", "add", "doc", slug).ExitCode);
        return Path.Combine(root.Path, ".knit", "kb", "docs", $"{slug}.md");
    }

    private static string StrayJson(TempKnitRoot root) =>
        JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement.GetProperty("stray_files").ToString();

    [Fact]
    public void Linked_files_next_to_a_doc_are_clean()
    {
        using var root = NewRepo();
        var doc = Doc(root, "guide");
        var dir = Path.GetDirectoryName(doc)!;
        File.WriteAllText(Path.Combine(dir, "order-flow.svg"), Svg);
        File.WriteAllBytes(Path.Combine(dir, "shot.png"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "spec.pdf"), [1]);
        File.AppendAllText(doc, "\n![Order flow](order-flow.svg)\n\n![Shot](shot.png) and the [spec](spec.pdf).\n");

        var r = root.Run("verify");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("no issues", r.Stdout);
    }

    [Fact]
    public void Unlinked_svg_png_and_pdf_are_stray()
    {
        using var root = NewRepo();
        var dir = Path.GetDirectoryName(Doc(root, "guide"))!;
        foreach (var name in new[] { "flow.svg", "shot.png", "spec.pdf" })
            File.WriteAllText(Path.Combine(dir, name), name.EndsWith(".svg") ? Svg : "x");

        Assert.Equal((int)ErrorCode.Conflict, root.Run("verify").ExitCode);
        var stray = StrayJson(root);
        foreach (var name in new[] { "flow.svg", "shot.png", "spec.pdf" })
            Assert.Contains(name, stray);
        Assert.Contains("no body links to it", stray);
    }

    [Fact]
    public void Svg_linked_from_another_folder_is_stray()
    {
        using var root = NewRepo();
        var doc = Doc(root, "guide");
        var other = Path.Combine(root.Path, ".knit", "kb", "flow.svg");
        File.WriteAllText(other, Svg);
        File.AppendAllText(doc, "\n![Flow](../flow.svg)\n");

        Assert.Contains("next to the doc that embeds it", StrayJson(root));
    }

    [Fact]
    public void A_file_next_to_an_item_needs_its_id_prefix_and_a_link()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("add", "task", "login fails").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var itemFile = ItemFixtures.FirstItemFile(root);
        var dir = Path.GetDirectoryName(itemFile)!;
        File.WriteAllBytes(Path.Combine(dir, $"{id}-login-error.png"), [1]);
        File.AppendAllText(itemFile, $"\n![Error]({id}-login-error.png)\n");
        Assert.Equal(0, root.Run("verify").ExitCode);

        File.WriteAllBytes(Path.Combine(dir, "login-error.png"), [1]);
        Assert.Contains("starts with its item's id", StrayJson(root));
        File.Delete(Path.Combine(dir, "login-error.png"));

        Assert.Equal(0, root.Run("delete", id, "--yes").ExitCode);
        Assert.True(File.Exists(Path.Combine(dir, $"{id}-login-error.png")));
        Assert.Contains($"its item {id} is gone", StrayJson(root));
    }

    [Fact]
    public void A_loose_file_outside_a_bucket_is_stray()
    {
        using var root = NewRepo();
        Doc(root, "guide");
        File.WriteAllBytes(Path.Combine(root.Path, ".knit", "kb", "photo.jpg"), [1]);

        Assert.Contains("photo.jpg", StrayJson(root));
    }

    [Fact]
    public void A_move_to_another_folder_carries_the_linked_files()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("design", "new", "checkout").ExitCode);
        var designs = Path.Combine(root.Path, ".knit", "designs");
        File.WriteAllText(Path.Combine(designs, "order-flow.svg"), Svg);
        File.AppendAllText(Path.Combine(designs, "checkout-design.md"), "\n![Order flow](order-flow.svg)\n");

        Assert.Equal(0, root.Run("design", "archive", "checkout").ExitCode);

        var archived = Path.Combine(designs, "completed", "checkout");
        Assert.True(File.Exists(Path.Combine(archived, "order-flow.svg")));
        Assert.False(File.Exists(Path.Combine(designs, "order-flow.svg")));
        Assert.Contains("![Order flow](order-flow.svg)", File.ReadAllText(Path.Combine(archived, "checkout-design.md")));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void A_kb_split_carries_the_linked_files_into_the_folder()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.Run("kb", "add", "feature", "checkout").ExitCode);
        var features = Path.Combine(root.Path, ".knit", "kb", "features");
        File.WriteAllText(Path.Combine(features, "order-flow.svg"), Svg);
        File.AppendAllText(Path.Combine(features, "checkout.md"), "\n![Order flow](order-flow.svg)\n");

        Assert.Equal(0, root.Run("kb", "split", "feature:checkout").ExitCode);

        Assert.True(File.Exists(Path.Combine(features, "checkout", "order-flow.svg")));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Svg_findings_are_warnings_and_strict_fails()
    {
        using var root = NewRepo();
        var doc = Doc(root, "guide");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(doc)!, "bad.svg"),
            """<svg xmlns="http://www.w3.org/2000/svg"><script>x()</script></svg>""");
        File.AppendAllText(doc, "\n![Bad](bad.svg)\n");

        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        var svg = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("svg_files").ToString();
        Assert.Contains("no viewBox", svg);
        Assert.Contains("<script> is not allowed", svg);
        Assert.Equal((int)ErrorCode.Conflict, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Ref_show_prints_the_svg_outline()
    {
        using var root = NewRepo();
        var doc = Doc(root, "guide");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(doc)!, "order-flow.svg"), Svg);
        File.AppendAllText(doc, "\n![Order flow](order-flow.svg)\n");

        var r = root.Run("ref", "show", "doc:guide#order-flow.svg");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Orders flow from the shop to billing.", r.Stdout);
        Assert.Contains("shop \"Shop\" at 8,8 in backend", r.Stdout);
        Assert.Contains("e1 shop → billing \"order placed\"", r.Stdout);
        Assert.DoesNotContain("M48,20", r.Stdout);

        var json = JsonDocument.Parse(root.Run("ref", "show", "doc:guide#order-flow.svg", "--json").Stdout).RootElement;
        var billing = json.GetProperty("elements").EnumerateArray().First(e => e.GetProperty("id").GetString() == "billing");
        Assert.Equal("backend", billing.GetProperty("group").GetString());
        Assert.Equal("96,8", billing.GetProperty("at").GetString());
        Assert.Equal("doc:guide#order-flow.svg/node/billing", billing.GetProperty("pointer").GetString());

        Assert.Equal((int)ErrorCode.NotFound, root.Run("ref", "show", "doc:guide#missing.svg").ExitCode);
    }
}
