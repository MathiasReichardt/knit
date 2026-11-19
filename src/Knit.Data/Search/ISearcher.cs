using Knit.Core.Refs;

namespace Knit.Data.Search;

public enum SearchKind { All, Item, Kb, Design, Project }

/// <summary>Which region of an entity a hit matched in.</summary>
public enum MatchField { Title, Body, Comment, Tags, Source, Link }

/// <summary>
/// A single search result. <see cref="Kind"/>/<see cref="Id"/> address the
/// entity to navigate to; <see cref="Field"/> says which region matched and
/// <see cref="BodyOffset"/> the char offset of the match within the body
/// (null unless <see cref="Field"/> is <see cref="MatchField.Body"/>) — both
/// are the hooks the web "jump to occurrence" feature consumes.
/// </summary>
public sealed record SearchHit(
    SearchKind Kind, string Id, string Title, string Snippet,
    MatchField Field, int? BodyOffset);

/// <summary>
/// The one search seam, shared by <c>knit search</c> and <c>/api/search</c>.
/// Entity-keyed (items / KB / designs), not file:line-keyed — results
/// navigate to an entity, not a source line. The implementation owns
/// tokenization, matching, ranking, and snippet generation. The snippet is
/// <b>plain text</b> (a windowed excerpt) — highlighting is the renderer's job.
///
/// Deliberately minimal: there is no reindex / upsert / delete / IsReady on
/// this interface. Those were artifacts of the old stale-cache backend; a
/// live scanner reads the store on every query, so there is nothing to keep
/// fresh and nothing to warm up. A future stateful impl (e.g. an in-memory
/// ranked index at M2 scale) can still live behind this same seam.
/// </summary>
public interface ISearcher
{
    IReadOnlyList<SearchHit> Query(string query, SearchKind kind, int limit);

    /// <summary><c>ref:bucket:slug</c> / <c>bucket:slug</c> direct-lookup
    /// shortcut — bypasses scanning so a freshly-created entry surfaces.</summary>
    SearchHit? LookupRef(Ref reference);
}
