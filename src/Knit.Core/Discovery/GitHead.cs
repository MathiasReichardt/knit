using System.Text.RegularExpressions;
using Knit.Core.Paths;

namespace Knit.Core.Discovery;

/// <summary>
/// What a checkout's <c>HEAD</c> points at.
/// </summary>
/// <param name="Branch">The checked-out branch, or null when HEAD is detached.</param>
/// <param name="ShortSha">The abbreviated commit when HEAD is detached, else null.</param>
/// <param name="Rebasing">A rebase (or <c>git am</c>) is in progress — why HEAD is detached.</param>
public sealed record GitHeadState(string? Branch, string? ShortSha, bool Rebasing);

/// <summary>
/// The branch the knit repo's checkout is on, read from <c>HEAD</c> — no git
/// shell-out, like <see cref="GitWorktree"/>.
/// </summary>
public static partial class GitHead
{
    public const int ShortShaLength = 7;

    private const string BranchRef = "ref: refs/heads/";

    // The reftable backend leaves this placeholder in HEAD; the real ref lives
    // in binary tables we do not parse.
    private const string ReftablePlaceholder = ".invalid";

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex Sha();

    /// <summary>
    /// The checkout whose HEAD describes <paramref name="knitRoot"/>:
    /// discovery's <paramref name="gitRoot"/> when it holds the knit root, else
    /// — a worktree redirected to its main checkout — that main checkout. Null
    /// otherwise; never a fresh walk up from the knit root, which could land in
    /// an unrelated enclosing repo.
    /// </summary>
    public static string? CheckoutFor(string knitRoot, string? gitRoot)
    {
        if (gitRoot is null) return null;
        var root = Path.GetFullPath(knitRoot);
        if (PathContainment.IsContained(Path.GetFullPath(gitRoot), root)) return gitRoot;
        if (GitWorktree.Bare(gitRoot) is { } bare)
            return bare.Worktrees.FirstOrDefault(w => PathContainment.IsContained(Path.GetFullPath(w), root));
        return GitWorktree.MainCheckout(gitRoot) is { } main && PathContainment.IsContained(Path.GetFullPath(main), root)
            ? main
            : null;
    }

    /// <summary>
    /// HEAD of the checkout at <paramref name="checkout"/>, or null when it is
    /// unreadable or names something other than a branch or a commit.
    /// </summary>
    public static GitHeadState? Read(string checkout)
    {
        if (GitWorktree.GitDir(checkout) is not { } gitDir) return null;

        string head;
        try { head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        if (head.StartsWith(BranchRef, StringComparison.Ordinal))
        {
            var branch = head[BranchRef.Length..];
            return branch.Length == 0 || branch == ReftablePlaceholder ? null : new GitHeadState(branch, null, false);
        }
        if (!Sha().IsMatch(head)) return null;

        var rebasing = Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
        return new GitHeadState(null, head[..ShortShaLength], rebasing);
    }

    /// <summary>
    /// The name a repo goes by when <c>repo_name</c> is unset: its folder — the
    /// main checkout's when the knit root is a linked worktree's own root, so
    /// every worktree of one repo shows the same name; for a bare repo's
    /// worktree, the folder holding a <c>.git</c> / <c>.bare</c> repo, else
    /// the repo's own (<c>app.git</c> → <c>app</c>).
    /// </summary>
    public static string DefaultRepoName(string knitRoot, string? gitRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(knitRoot));
        if (gitRoot is not null && RepoDiscovery.SamePath(gitRoot, root))
        {
            if (GitWorktree.Bare(gitRoot) is { } bare)
            {
                var common = Path.GetFileName(bare.CommonDir);
                return common.StartsWith('.')
                    ? Path.GetFileName(Path.GetDirectoryName(bare.CommonDir)) is { Length: > 0 } c ? c : common
                    : common.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && common.Length > 4 ? common[..^4] : common;
            }
            if (GitWorktree.MainCheckout(gitRoot) is { } main) root = Path.TrimEndingDirectorySeparator(main);
        }
        var name = Path.GetFileName(root);
        return name.Length > 0 ? name : root;
    }
}
