using Knit.Core.Discovery;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Data;

namespace Knit.WebApi.Services;

/// <summary>
/// Per-request facade over the on-disk repo, constructed fresh on each
/// request. Items come from the host's <see cref="ItemCache"/>, validated
/// against the files on the first read, so the answer reflects the current
/// filesystem state.
/// </summary>
public sealed class RepoServices
{
    public string Root { get; }
    public ItemStore Store { get; }
    public IFrontmatterSerializer Yaml { get; }

    public RepoServices(WebApiOptions options) : this(options.RootPath, options.ItemCache) { }

    /// <summary>Without a cache every read parses the files: what verify needs.</summary>
    public RepoServices(string root, ItemCache? cache = null)
    {
        Root = root;
        Yaml = new SimpleFrontmatterSerializer();
        Store = new ItemStore(root, Yaml, cache);
    }

    public IdShortener Shortener() => IdShortener.FromItemsDir(Root);

    public LinkGraph Graph() => Store.Graph();

    public string KnitDir => RepoDiscovery.KnitDir(Root);

    /// <summary>The repo lock for a mutation that rewrites many files.</summary>
    public RepoLock Exclusive(string operation) =>
        RepoLock.Acquire(Root, operation, note => Console.Error.WriteLine($"note: {note}"));
}
