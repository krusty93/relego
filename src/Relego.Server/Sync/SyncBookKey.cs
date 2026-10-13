namespace Relego.Server.Sync;

/// <summary>
/// Produces the shared book key used to correlate a book the extension sees in the Amazon notebook
/// with the books already imported into Relego. The same normalization is mirrored by the extension
/// (<c>normalizeBookKey</c> in <c>src/relego.extension/src/profile.ts</c>): trim, collapse internal
/// whitespace runs to one space and lowercase. The key is an optimization only — a mismatch costs a
/// re-read of the book, never correctness, because dedup stays insert-only.
/// </summary>
public static class SyncBookKey
{
    /// <summary>Normalizes a book title into the shared key.</summary>
    /// <param name="title">The raw book title, or <c>null</c>.</param>
    /// <returns>The normalized key, or an empty string when there is no usable title.</returns>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var buffer = new System.Text.StringBuilder(title.Length);
        var pendingSpace = false;

        foreach (var character in title)
        {
            // Matches the extension's JavaScript /\s+/g collapse, so both sides derive the same key.
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = buffer.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                buffer.Append(' ');
                pendingSpace = false;
            }

            buffer.Append(character);
        }

        return buffer.ToString().ToLowerInvariant();
    }
}
