using Knit.Core.Discovery;
using Knit.Data;
using Microsoft.Extensions.Logging;

namespace Knit.WebApi;

public sealed class WebApiOptions
{
    /// <summary>
    /// The root half of the options, from discovery: the one place every host
    /// takes it from, so a host cannot show other state than the CLI writes to,
    /// and a field derived from the root cannot be wired into one host and
    /// forgotten in another.
    /// </summary>
    public static WebApiOptions ForRoot(RepoDiscovery.RootResolution r) => new()
    {
        RootPath = r.Root ?? throw new ArgumentException("the root did not resolve", nameof(r)),
        RedirectedFrom = r.Redirected ? r.Checkout : null,
        GitRoot = r.GitRoot,
        Unshared = r.Redirected ? null : GitWorktree.UnsharedAt(r.Root),
    };

    public string RootPath { get; set; } = string.Empty;

    /// <summary>The checkout whose <c>knit_root</c> pointed at <see cref="RootPath"/>, or null when not redirected.</summary>
    public string? RedirectedFrom { get; set; }

    /// <summary>The undecided worktree the host runs in, while another checkout has a tracker it could share; else null.</summary>
    public GitWorktree.UnsharedWorktree? Unshared { get; set; }

    /// <summary>Discovery's enclosing git root of the working directory, or null outside one.</summary>
    public string? GitRoot { get; set; }
    public BindAddress Bind { get; set; } = BindAddress.Loopback;
    public int Port { get; set; }
    public bool OpenBrowser { get; set; }
    public bool EnableStaticFiles { get; set; } = true;
    public bool EnableWatch { get; set; } = true;
    public bool EnableSearch { get; set; } = true;
    public LogLevel MinLogLevel { get; set; } = LogLevel.Warning;

    /// <summary>The host's item cache over <see cref="RootPath"/>; set by <see cref="WebApiBuilder.Build"/>.</summary>
    public ItemCache? ItemCache { get; set; }
}
