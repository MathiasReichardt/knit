using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Knit.Core;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.IO;
using Knit.Data;
using Knit.Data.Search;
using Knit.WebApi.Contracts;
using Knit.WebApi.Endpoints;
using Knit.WebApi.Services;

namespace Knit.WebApi;

public static class WebApiBuilder
{
    public static WebApplication Build(string[] args, WebApiOptions options)
    {
        ValidateOptions(options);
        options.ItemCache ??= new ItemCache(options.RootPath);
        var builder = WebApplication.CreateSlimBuilder(args);
        if (options.Port > 0)
            builder.WebHost.UseSetting("urls", options.Bind.ListenUrl(options.Port));

        builder.Logging.SetMinimumLevel(options.MinLogLevel);
        builder.Logging.AddFilter("Microsoft", options.MinLogLevel);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", options.MinLogLevel);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        builder.Services.AddSingleton(options);
        if (options.EnableWatch)
        {
            builder.Services.AddSingleton<FileWatcher>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<FileWatcher>());
            builder.Services.AddSingleton(sp => new ActivityBuffer(
                sp.GetRequiredService<WebApiOptions>(),
                sp.GetRequiredService<ILogger<ActivityBuffer>>(),
                sp.GetRequiredService<FileWatcher>()));
            builder.Services.AddHostedService(sp => sp.GetRequiredService<ActivityBuffer>());
        }
        if (options.EnableSearch)
            builder.Services.AddSingleton<ISearcher>(sp =>
                new LiveScanSearcher(options.RootPath, cache: options.ItemCache));

        var app = builder.Build();
        Configure(app, options);
        return app;
    }

    private static void ValidateOptions(WebApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
            throw new InvalidOperationException("WebApiOptions.RootPath is required.");
        if (!Directory.Exists(options.RootPath))
            throw new DirectoryNotFoundException($"Root path does not exist: {options.RootPath}");
        var knitDir = RepoDiscovery.KnitDir(options.RootPath);
        if (!Directory.Exists(knitDir))
            throw new DirectoryNotFoundException(
                $"No .knit/ found under root '{options.RootPath}'. Run `knit init` there or pass --root to a repo with .knit/.");
    }

    /// <summary>
    /// Second line behind the markdown / SVG sanitizers: injected markup that
    /// slips past them still cannot run script. Inline styles stay allowed —
    /// Mermaid emits a style block per diagram, React and CodeMirror set style
    /// attributes. External https images are allowed in docs.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";

    public static void Configure(WebApplication app, WebApiOptions options)
    {
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            var refusal = RequestOriginGuard.Reject(
                options.Bind, ctx.Request.Scheme, ctx.Request.Method, ctx.Request.Host,
                ctx.Request.Headers.Origin, ctx.Request.ContentType);
            if (refusal is null)
            {
                await next(ctx);
                return;
            }
            await ApiProblem.Problem(refusal.Status, refusal.Code, refusal.Reason).ExecuteAsync(ctx);
        });

        // Another writer changed the item, holds the repo lock or holds the file.
        app.Use(async (ctx, next) =>
        {
            try { await next(ctx); }
            catch (WriteRefusedException ex) when (!ctx.Response.HasStarted)
            {
                await ApiProblem.Problem(StatusCodes.Status409Conflict, "write_refused", ex.Message).ExecuteAsync(ctx);
            }
            // 423, not 409: the SPA reads a 409 on some routes as a domain answer.
            catch (FileLockedException ex) when (!ctx.Response.HasStarted)
            {
                await ApiProblem.Problem(StatusCodes.Status423Locked, "file_locked", ex.Message).ExecuteAsync(ctx);
            }
        });

        app.MapGet("/api/version", () =>
        {
            var config = ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath));
            return new VersionDto(
                VersionInfo.Get(typeof(WebApiBuilder).Assembly),
                config?.FormatVersion ?? FormatVersion.Current,
                FormatVersion.Current);
        });
        app.MapGet("/api/root", () => new RootDto(
            Path.GetFullPath(options.RootPath),
            options.RedirectedFrom,
            options.Unshared is { } u ? new UnsharedWorktreeDto(u.Target, u.Bare, u.Command) : null));
        app.MapRepo(options);
        app.MapShare(options);

        app.MapItems(options);
        app.MapTimeline(options);
        app.MapStatus(options);
        app.MapItemMutations(options);
        app.MapItemLinkComment(options);
        app.MapKb(options);
        app.MapDesigns(options);
        app.MapSlates(options);
        app.MapProjects(options);
        app.MapFileView(options);
        app.MapMounts(options);
        app.MapConfig(options);
        app.MapConventions(options);
        app.MapDoctor(options);
        var watcher = options.EnableWatch ? app.Services.GetService<FileWatcher>() : null;
        var activity = options.EnableWatch ? app.Services.GetService<ActivityBuffer>() : null;
        app.MapEvents(watcher, activity);
        app.MapActivity(activity);
        if (options.EnableSearch) app.MapSearch();

        if (options.EnableStaticFiles)
        {
            var asm = typeof(WebApiBuilder).Assembly;
            var provider = new ManifestEmbeddedFileProvider(asm, "wwwroot");
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
            app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = provider });
        }

        if (options.OpenBrowser)
        {
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() =>
            {
                var url = ResolveUrl(app, options);
                if (url != null) TryOpenBrowser(url, app.Logger);
            });
        }
    }

    private static string? ResolveUrl(WebApplication app, WebApiOptions options)
    {
        if (options.Port > 0) return options.Bind.BrowsableUrl(options.Port);
        var addresses = app.Urls;
        foreach (var addr in addresses)
            return addr.Replace("0.0.0.0", "127.0.0.1").Replace("[::]", "127.0.0.1");
        return null;
    }

    private static void TryOpenBrowser(string url, ILogger logger)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open browser for {Url}; continuing to serve.", url);
        }
    }
}
