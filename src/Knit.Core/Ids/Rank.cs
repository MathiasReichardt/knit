namespace Knit.Core.Ids;

/// <summary>
/// Fractional rank using base-26 (a-z). Between(lo, hi) produces a string
/// strictly between lo and hi in lexicographic order. Empty lo means
/// "before everything"; empty hi means "after everything".
///
/// <para><b>Invariant: no generated rank ends in <c>'a'</c>.</b> That is
/// exactly the condition under which "insert before this row" always has an
/// answer, since nothing sorts before <c>"a"</c>. Mirrored in the SPA by
/// <c>src/Knit.Web/src/lib/rank.ts</c> — keep the two in step.</para>
/// </summary>
public static class Rank
{
    private const char Min = 'a';
    private const char Max = 'z';

    /// <summary>Digits a rebalance spreads over: 'b'..'y'.</summary>
    private const int Radix = Max - Min - 1;

    public static string Initial() => "n";

    public static string Between(string? before, string? after)
    {
        var lo = before ?? "";
        var hi = after ?? "";
        if (lo.Length > 0 && hi.Length > 0 && string.CompareOrdinal(lo, hi) >= 0)
            throw new ArgumentException($"before '{lo}' must be < after '{hi}'");
        if (IsGapClosed(lo, hi))
            throw new InvalidOperationException(GapClosedMessage(lo, hi));

        return Compute(lo, hi);
    }

    /// <summary>
    /// True when <b>no</b> rank sorts strictly between the two bounds, so a
    /// move into that slot has no answer at all. That happens in exactly one
    /// shape: <paramref name="after"/> is <paramref name="before"/> followed
    /// by one or more <c>'a'</c>s — <c>"" → "a"</c>, <c>"a" → "aa"</c>,
    /// <c>"n" → "na"</c>. Anything else has room, however deep.
    ///
    /// <para>Only ranks written by older Knit versions can form such a pair, and
    /// <see cref="Spread"/> never does: it emits no <c>'a'</c> at all.</para>
    /// </summary>
    public static bool IsGapClosed(string? before, string? after)
    {
        var lo = before ?? "";
        var hi = after ?? "";
        // An open upper bound can always be extended.
        if (hi.Length == 0) return false;
        if (hi.Length <= lo.Length) return false;
        if (!hi.AsSpan(0, lo.Length).SequenceEqual(lo)) return false;
        return !hi.AsSpan(lo.Length).ContainsAnyExcept(Min);
    }

    // No remedy named here on purpose: Core does not know which surface is
    // asking. Callers respace the group and retry.
    private static string GapClosedMessage(string lo, string hi) =>
        lo.Length == 0
            ? $"nothing sorts before rank '{hi}'"
            : $"no rank fits between '{lo}' and '{hi}'";

    private static string Compute(string lo, string hi)
    {
        if (hi.Length == 0)
            return lo.Length == 0 ? Initial() : ExtendUpward(lo);
        if (lo.Length == 0)
            return ShrinkDownward(hi);

        if (lo[0] == hi[0])
        {
            var rest = Compute(lo.Length > 1 ? lo[1..] : "", hi.Length > 1 ? hi[1..] : "");
            return lo[0] + rest;
        }

        if (lo[0] + 1 < hi[0])
        {
            return ((char)((lo[0] + hi[0]) / 2)).ToString();
        }

        // adjacent: extend lo
        return lo[0] + ExtendUpward(lo.Length > 1 ? lo[1..] : "");
    }

    /// <summary>
    /// Produce a string strictly greater than <paramref name="lo"/>. Picks
    /// the midpoint between <c>lo[0]</c> and <c>'z'</c> at the first
    /// position with room; only descends into a longer suffix when the
    /// leading char has already saturated at <c>'z'</c>. This keeps the
    /// rank space shallow for the common "append to the end" case —
    /// adding items in sequence yields <c>n, t, w, y, z, zn, zt, …</c>
    /// instead of <c>n, nn, nnn, nnnn, …</c>.
    /// </summary>
    private static string ExtendUpward(string lo)
    {
        if (lo.Length == 0) return Initial();
        var c0 = lo[0];
        if (c0 < Max)
        {
            var mid = (char)((c0 + Max + 1) / 2);
            if (mid <= c0) mid = (char)(c0 + 1);
            return mid.ToString();
        }
        return Max + ExtendUpward(lo.Length > 1 ? lo[1..] : "");
    }

    /// <summary>
    /// Produce a string strictly less than <paramref name="hi"/>. <c>'a'</c>
    /// is a <b>prefix-only</b> digit: a rank ending in it has nothing below
    /// it, because every rank starting with <c>'a'</c> is <c>"a" + rest</c>,
    /// which sorts <i>after</i> <c>"a"</c>. So this descends into a longer
    /// suffix rather than saturating at <c>'a'</c> — repeated front-inserts
    /// yield <c>n, g, c, b, an, ag, ac, ab, aan, …</c>, unbounded at roughly
    /// one extra character per four inserts. Saturating instead exhausted the
    /// space after three.
    /// </summary>
    private static string ShrinkDownward(string hi)
    {
        var c0 = hi[0];
        if (c0 > Min + 1)
        {
            var c = (char)((Min + c0) / 2);
            if (c <= Min) c = (char)(Min + 1);
            return c.ToString();
        }
        if (c0 == Min)
        {
            // Unreachable: Between rejects this pair up front via
            // IsGapClosed. Kept so the recursion cannot silently return a
            // value that is not below hi.
            if (hi.Length == 1)
                throw new InvalidOperationException(GapClosedMessage("", hi));
            return Min + ShrinkDownward(hi[1..]);
        }
        // hi[0] == 'b': no character fits between 'a' and 'b', so move under
        // the 'a' prefix and start fresh there.
        return Min + Initial();
    }

    /// <summary>
    /// <paramref name="count"/> ranks, strictly increasing, evenly spaced and
    /// all the same width — what a rebalance assigns to one sibling group.
    /// Uses only the 24 digits <c>'b'..'y'</c>: <c>'a'</c> stays free
    /// as a prefix so inserting above the first row has room, <c>'z'</c> stays
    /// free so appending after the last stays single-character. Centred, so
    /// <c>Spread(1)</c> is <see cref="Initial"/>. Width grows only when
    /// 24^width can no longer hold <paramref name="count"/>.
    /// </summary>
    public static string[] Spread(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must not be negative");
        if (count == 0) return [];

        var width = 1;
        var slots = (long)Radix;
        while (slots < count) { width++; slots *= Radix; }

        var ranks = new string[count];
        for (var i = 0; i < count; i++)
            ranks[i] = Encode((2L * i + 1) * slots / (2L * count), width);
        return ranks;
    }

    private static string Encode(long index, int width)
    {
        var chars = new char[width];
        for (var pos = width - 1; pos >= 0; pos--)
        {
            chars[pos] = (char)(Min + 1 + (int)(index % Radix));
            index /= Radix;
        }
        return new string(chars);
    }

    public static bool IsValid(string? r) =>
        !string.IsNullOrEmpty(r) && r.All(c => c is >= Min and <= Max);
}
