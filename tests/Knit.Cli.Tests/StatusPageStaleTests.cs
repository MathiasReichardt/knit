using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

/// <summary>
/// The staleness hint on `knit status` and `knit verify`. It only
/// ever speaks about a page knit wrote, and reads the page's age from its
/// Generated line.
/// </summary>
[Collection(nameof(CliCollection))]
public partial class StatusPageStaleTests
{
    private static TempKnitRoot Init()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "Something").ExitCode);
        return root;
    }

    private static string PagePath(TempKnitRoot root) => Path.Combine(root.Path, "project-status.md");

    [GeneratedRegex(@"^Generated \d{4}-\d{2}-\d{2}", RegexOptions.Multiline)]
    private static partial Regex GeneratedLine();

    /// <summary>A page knit wrote, back-dated by <paramref name="days"/>; its mtime stays now.</summary>
    private static void Page(TempKnitRoot root, int days)
    {
        Assert.Equal(0, root.Run("status", "report").ExitCode);
        var date = DateOnly.FromDateTime(DateTime.Now).AddDays(-days).ToString("yyyy-MM-dd");
        var text = GeneratedLine().Replace(File.ReadAllText(PagePath(root)), $"Generated {date}", 1);
        File.WriteAllText(PagePath(root), text);
        File.SetLastWriteTime(PagePath(root), DateTime.Now);
    }

    private static JsonElement StatusJson(TempKnitRoot root)
    {
        var r = root.Run("status", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement;
    }

    private static (int Exit, string Stdout) Verify(TempKnitRoot root, params string[] extra)
    {
        var r = root.Run(["verify", .. extra]);
        return (r.ExitCode, r.Stdout);
    }

    [Fact]
    public void No_page_no_hint_anywhere()
    {
        using var root = Init();
        Assert.DoesNotContain("status page", root.Run("status").Stdout);
        Assert.False(StatusJson(root).TryGetProperty("status_page", out _));
        Assert.DoesNotContain("status page", Verify(root).Stdout);
        Assert.False(JsonDocument.Parse(Verify(root, "--json").Stdout).RootElement.TryGetProperty("status_page", out _));
    }

    [Fact]
    public void A_page_generated_today_is_not_stale()
    {
        using var root = Init();
        Page(root, 0);
        Assert.DoesNotContain("status page", root.Run("status").Stdout);
        var page = StatusJson(root).GetProperty("status_page");
        Assert.Equal(PagePath(root), page.GetProperty("path").GetString());
        Assert.False(page.GetProperty("stale").GetBoolean());
        Assert.DoesNotContain("status page is stale", Verify(root).Stdout);
    }

    [Fact]
    public void A_page_two_days_old_is_flagged_by_status_and_verify_without_failing_verify()
    {
        using var root = Init();
        Page(root, 2);

        Assert.Contains("status page project-status.md is 2 days old — knit status report", root.Run("status").Stdout);
        var page = StatusJson(root).GetProperty("status_page");
        Assert.True(page.GetProperty("stale").GetBoolean());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).AddDays(-2).ToString("yyyy-MM-dd"), page.GetProperty("generated").GetString());

        var (exit, stdout) = Verify(root, "--strict");
        Assert.Equal(0, exit);
        Assert.Contains("status page is stale", stdout);
        var json = JsonDocument.Parse(Verify(root, "--json").Stdout).RootElement;
        Assert.True(json.GetProperty("status_page").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Stale_days_quiets_the_hint()
    {
        using var root = Init();
        Page(root, 2);
        Assert.Equal(0, root.Run("config", "set", "status_report.stale_days", "3").ExitCode);
        Assert.DoesNotContain("status page", root.Run("status").Stdout);
        Assert.False(StatusJson(root).GetProperty("status_page").GetProperty("stale").GetBoolean());
        Assert.DoesNotContain("status page is stale", Verify(root).Stdout);
    }

    [Fact]
    public void A_page_without_the_marker_gives_no_hint()
    {
        using var root = Init();
        File.WriteAllText(PagePath(root), "# Status\n\nGenerated 2000-01-01\n");
        Assert.DoesNotContain("status page", root.Run("status").Stdout);
        Assert.False(StatusJson(root).TryGetProperty("status_page", out _));
        Assert.DoesNotContain("status page", Verify(root).Stdout);
    }

    [Fact]
    public void The_age_follows_the_generated_line_not_the_mtime()
    {
        using var root = Init();
        Page(root, 0);
        File.SetLastWriteTime(PagePath(root), DateTime.Now.AddDays(-30));
        Assert.False(StatusJson(root).GetProperty("status_page").GetProperty("stale").GetBoolean());

        Page(root, 5);
        Assert.True(StatusJson(root).GetProperty("status_page").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void The_hint_follows_the_configured_path()
    {
        using var root = Init();
        Assert.Equal(0, root.Run("config", "set", "status_report.path", "docs/status.md").ExitCode);
        Assert.Equal(0, root.Run("status", "report").ExitCode);
        Assert.Equal(
            Path.Combine(root.Path, "docs", "status.md"),
            StatusJson(root).GetProperty("status_page").GetProperty("path").GetString());
    }
}
