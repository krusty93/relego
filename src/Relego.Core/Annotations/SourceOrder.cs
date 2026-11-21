namespace Relego.Core.Annotations;

/// <summary>
/// A source position split into the counter it uses and its leading number, which is the only
/// comparable part of values such as <c>Location 1234-1236</c> or <c>Page 12</c>.
/// </summary>
public readonly record struct SourcePosition
{
    private SourcePosition(string? kind, int? start)
    {
        Kind = kind;
        Start = start;
    }

    /// <summary><c>location</c>, <c>page</c>, or <c>null</c> when the source names no counter.</summary>
    public string? Kind { get; }

    /// <summary>The leading number of the position, or <c>null</c> when it does not start with one.</summary>
    public int? Start { get; }

    /// <summary>Splits a raw source position into its kind and its leading number.</summary>
    /// <param name="raw">Raw position as reported by the source, or <c>null</c>.</param>
    /// <returns>The parsed position; both parts are <c>null</c> when nothing can be read.</returns>
    public static SourcePosition Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return default;

        var text = raw.Trim();
        var kind = ReadKind(text);
        var digits = kind is null ? text : text[(kind.Length + 1)..];

        return new SourcePosition(kind, ReadLeadingNumber(digits));
    }

    private static string? ReadKind(string text)
    {
        if (text.StartsWith("location ", StringComparison.OrdinalIgnoreCase))
            return "location";

        return text.StartsWith("page ", StringComparison.OrdinalIgnoreCase) ? "page" : null;
    }

    private static int? ReadLeadingNumber(string text)
    {
        var length = 0;
        while (length < text.Length && char.IsAsciiDigit(text[length]))
            length++;

        return length > 0 && int.TryParse(text.AsSpan(0, length), out var start) ? start : null;
    }
}

/// <summary>The identity of an annotation's place in its book, used to order annotations for reading.</summary>
/// <param name="PositionKind">Position counter kind, or <c>null</c>.</param>
/// <param name="PositionStart">Leading number of the position, or <c>null</c>.</param>
/// <param name="AddedOn">Source timestamp, or <c>null</c>.</param>
/// <param name="Id">Stored annotation id, the final tie-break.</param>
public readonly record struct AnnotationOrderKey(
    string? PositionKind,
    int? PositionStart,
    DateTimeOffset? AddedOn,
    int Id);

/// <summary>
/// The single ordering function shared by the chapter and stitch builders, so a book always reads
/// in one order regardless of the order its annotations arrived in.
/// </summary>
public static class SourceOrder
{
    /// <summary>
    /// Orders annotations by position when the book reports one consistent counter for all of them,
    /// and by source timestamp otherwise.
    /// </summary>
    /// <typeparam name="T">Annotation type.</typeparam>
    /// <param name="annotations">Annotations to order.</param>
    /// <param name="keySelector">Projects an annotation onto its ordering key.</param>
    /// <returns>The annotations in reading order.</returns>
    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> annotations, Func<T, AnnotationOrderKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(keySelector);

        var items = annotations.Select(annotation => (Annotation: annotation, Key: keySelector(annotation))).ToList();

        var orderByPosition = items.Count > 0
            && items.All(item => item.Key.PositionStart is not null)
            && items.Select(item => item.Key.PositionKind).Distinct(StringComparer.Ordinal).Count() == 1;

        var ordered = orderByPosition
            ? items
                .OrderBy(item => item.Key.PositionStart)
                .ThenBy(item => item.Key.Id)
            : items
                // A missing source date sorts last: an annotation without a date cannot claim to
                // have been read first.
                .OrderBy(item => item.Key.AddedOn is null)
                .ThenBy(item => item.Key.AddedOn)
                .ThenBy(item => item.Key.Id);

        return [.. ordered.Select(item => item.Annotation)];
    }
}
