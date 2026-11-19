using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.Data.Search;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// Locks in the rules of the shared in-memory scanner: plain substring
/// matching within a word, diacritic folding, multi-token AND, comment-region
/// matching, and quality-then-title ranking.
/// </summary>
public class LiveScanSearcherTests
{
    [Fact]
    public void Matches_substring_within_a_word()
    {
        var (root, searcher) = Repo(
            ("Category planning", ""),
            ("Scatter plot", ""));
        try
        {
            // Substring, not word-start: "cat" hits both "Category" and "Scatter".
            var hits = searcher.Query("cat", SearchKind.Item, 50);
            Assert.Contains(hits, h => h.Title == "Category planning");
            Assert.Contains(hits, h => h.Title == "Scatter plot");
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Ranks_word_start_prefix_above_mid_word_substring()
    {
        var (root, searcher) = Repo(
            ("OAuth flow", ""),
            ("Authentication guide", ""));
        try
        {
            // Both contain "auth"; the word-start prefix ranks first.
            var hits = searcher.Query("auth", SearchKind.Item, 50);
            Assert.Equal("Authentication guide", hits[0].Title);
            Assert.Contains(hits, h => h.Title == "OAuth flow");
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Folds_diacritics()
    {
        var (root, searcher) = Repo(("Lunch", "Discuss the café menu"));
        try
        {
            // Accent-folded both ways: unaccented query hits accented corpus…
            Assert.Single(searcher.Query("cafe", SearchKind.Item, 50));
            // …and an accented query still hits.
            Assert.Single(searcher.Query("café", SearchKind.Item, 50));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Is_case_insensitive()
    {
        var (root, searcher) = Repo(("Deploy guide", "rollout NOTES here"));
        try
        {
            Assert.Single(searcher.Query("DEPLOY", SearchKind.Item, 50)); // query upper, title mixed
            Assert.Single(searcher.Query("notes", SearchKind.Item, 50));  // query lower, body upper
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Multi_token_query_is_and()
    {
        var (root, searcher) = Repo(
            ("First", "alpha beta gamma"),
            ("Second", "alpha only"));
        try
        {
            var hits = searcher.Query("alpha beta", SearchKind.Item, 50);
            Assert.Single(hits);
            Assert.Equal("First", hits[0].Title);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Matches_in_comment_report_comment_field()
    {
        var (root, searcher) = Repo(
            new Item
            {
                Id = "01968000-0000-7000-8000-00000000c001",
                Title = "Ticket",
                Type = ItemType.Task,
                State = ItemState.Todo,
                Created = DateTimeOffset.UtcNow,
                Body = "nothing interesting here",
                Comments =
                [
                    new Comment("c1", "alice", DateTimeOffset.UtcNow, "let us try a burndown chart"),
                ],
            });
        try
        {
            var hits = searcher.Query("burndown", SearchKind.Item, 50);
            var hit = Assert.Single(hits);
            Assert.Equal(MatchField.Comment, hit.Field);
            Assert.Contains("burndown", hit.Snippet);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Snippet_is_plain_text_around_the_match()
    {
        var (root, searcher) = Repo(("Doc", "the quick brown fox jumps"));
        try
        {
            var hit = Assert.Single(searcher.Query("brown", SearchKind.Item, 50));
            // Plain windowed excerpt — no <mark>, highlighting is the renderer's job.
            Assert.Contains("brown", hit.Snippet);
            Assert.DoesNotContain("<mark>", hit.Snippet);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Title_hits_rank_before_body_hits()
    {
        var (root, searcher) = Repo(
            ("Random body match", "the deploy steps follow"),
            ("Deploy guide", "unrelated content"));
        try
        {
            var hits = searcher.Query("deploy", SearchKind.Item, 50);
            Assert.Equal("Deploy guide", hits[0].Title);
        }
        finally { TryDelete(root); }
    }

    // ---------- helpers ----------

    private static (string Root, LiveScanSearcher Searcher) Repo(params (string Title, string Body)[] items)
    {
        var built = new Item[items.Length];
        for (var i = 0; i < items.Length; i++)
            built[i] = new Item
            {
                Id = $"01968000-0000-7000-8000-0000000000{i:x2}",
                Title = items[i].Title,
                Type = ItemType.Task,
                State = ItemState.Todo,
                Created = DateTimeOffset.UtcNow,
                Body = items[i].Body,
            };
        return Repo(built);
    }

    private static (string Root, LiveScanSearcher Searcher) Repo(params Item[] items)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".knit", "items"));
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        foreach (var item in items) store.Write(item);
        return (root, new LiveScanSearcher(root));
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
