namespace Knit.Cli.Tests;

/// <summary>
/// The release artifact is a bare executable, so the notices MIT/ISC/BSD
/// require have to be reachable from the binary itself. These pin that the
/// resources are actually embedded — a build that drops them still compiles.
/// </summary>
[Collection(nameof(CliCollection))]
public class LicensesTests
{
    [Fact]
    public void Licenses_prints_own_mit_license_and_third_party_notices()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("licenses");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("MIT License", r.Stdout);
        Assert.Contains("Permission is hereby granted, free of charge", r.Stdout);
        Assert.Contains("Third-party notices", r.Stdout);
        Assert.Contains("Spectre.Console", r.Stdout);
        Assert.Empty(r.Stderr);
    }

    [Fact]
    public void Licenses_third_party_omits_knits_own_license_header()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("licenses", "--third-party");

        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("Knit is licensed under", r.Stdout);
        Assert.Contains("Third-party notices", r.Stdout);
    }

    [Fact]
    public void Licenses_out_writes_both_files()
    {
        using var root = new TempKnitRoot();
        var dest = Path.Combine(root.Path, "legal");

        var r = root.Run("licenses", "--out", dest);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Permission is hereby granted", File.ReadAllText(Path.Combine(dest, "LICENSE")));
        Assert.Contains("Third-party notices", File.ReadAllText(Path.Combine(dest, "THIRD-PARTY-NOTICES.md")));
    }

    [Fact]
    public void Licenses_out_with_third_party_is_rejected()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("licenses", "--out", Path.Combine(root.Path, "legal"), "--third-party");

        Assert.Equal(2, r.ExitCode);
        Assert.Contains("--out writes both files", r.Stderr);
        Assert.Empty(r.Stdout);
    }
}
