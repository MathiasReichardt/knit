using System.Security.Cryptography;

namespace Knit.Core.Ids;

/// <summary>
/// NanoID generator for Knit-minted entities (items + comments). Ids are
/// <see cref="Length"/> chars over an uppercase-digit alphabet, giving ~93
/// bits — collision-safe for concurrent/offline minting without a coordinator.
///
/// The alphabet is uppercase + digits only so ids are <b>case-insensitive by
/// construction</b>: Windows/macOS filesystems are case-insensitive, and a
/// single-case alphabet removes any cross-OS folder-name case-collision risk.
/// Not time-sortable (no code relies on id time-order — items sort by
/// <c>rank</c>/<c>created</c>, comments by <c>created</c>).
/// </summary>
public static class NanoId
{
    public const int Length = 18;
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string New()
    {
        return string.Create(Length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
    }

    /// <summary>
    /// True when <paramref name="s"/> is a well-formed NanoID: exactly
    /// <see cref="Length"/> chars, all <c>[0-9A-Z]</c> (case-folded — a
    /// lowercase-typed id still validates). This is the one place the
    /// "is this an item id vs. a KB-ref leaf" discrimination lives.
    /// </summary>
    public static bool IsValid(string? s)
    {
        if (s is null || s.Length != Length) return false;
        foreach (var ch in s)
        {
            var c = ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch;
            if (c is not (>= '0' and <= '9' or >= 'A' and <= 'Z')) return false;
        }
        return true;
    }
}
