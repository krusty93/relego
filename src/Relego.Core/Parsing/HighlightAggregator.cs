using Relego.Core.Annotations;

namespace Relego.Core.Parsing;

/// <summary>
/// Shared normalization for highlight sources. Extracted verbatim from
/// <see cref="ClippingsParser"/> so every source (Kindle, Kobo, …) emits an
/// identical <see cref="ParseResult"/> shape. Pure, no I/O.
/// </summary>
internal static class HighlightAggregator
{
    /// <summary>
    /// Attaches reader notes to the passages they annotate, deduplicates, groups by
    /// <c>(Title, Author)</c> preserving first-seen order, excludes empty books, and reports counts.
    /// </summary>
    /// <remarks>
    /// A note is attached to a passage by source position — the pair of location kind and leading
    /// number — never by note text, because Kindle writes a note as <c>Location 1234</c> and its
    /// passage as <c>Location 1234-1236</c>. A source that reports no position (Kobo) is matched on
    /// the passage the clipping is anchored to instead. When a group holds several notes the most
    /// recent one wins and the superseded ones count as duplicates; a note that matches no passage
    /// becomes its own annotation, keeping its anchored text when the source reports one.
    /// </remarks>
    /// <param name="clippings">Raw clippings produced by a source.</param>
    /// <param name="totalEntriesProcessed">
    /// The number of rows/entries considered (including skipped ones), supplied by
    /// each source since skipped rows are not present in <paramref name="clippings"/>.
    /// </param>
    /// <returns>The books, the entries considered and the duplicates removed.</returns>
    public static ParseResult Aggregate(IReadOnlyList<RawClipping> clippings, int totalEntriesProcessed)
    {
        ArgumentNullException.ThrowIfNull(clippings);

        var duplicatesRemoved = 0;
        var bookOrder = new List<(string Title, string? Author)>();
        var bookGroups = new Dictionary<(string Title, string? Author), List<ClipGroup>>();
        var groupLookup = new Dictionary<GroupKey, ClipGroup>();

        foreach (var clip in clippings)
        {
            var bookKey = (clip.Title, clip.Author);
            if (!bookGroups.TryGetValue(bookKey, out var groups))
            {
                groups = [];
                bookGroups[bookKey] = groups;
                bookOrder.Add(bookKey);
            }

            var position = SourcePosition.Parse(clip.Location);

            // A position counter identifies the passage; a source that reports none (Kobo) is
            // identified by the passage the clipping is anchored to, so a note row still finds the
            // passage it belongs to.
            var groupKey = position.Start is not null
                ? new GroupKey(clip.Title, clip.Author, position.Kind, position.Start, null)
                : new GroupKey(clip.Title, clip.Author, null, null, clip.Text);

            if (!groupLookup.TryGetValue(groupKey, out var group))
            {
                group = new ClipGroup();
                groupLookup[groupKey] = group;
                groups.Add(group);
            }

            if (!group.Add(clip))
                duplicatesRemoved++;
        }

        var books = new List<ParsedBook>(bookOrder.Count);
        foreach (var bookKey in bookOrder)
        {
            var highlights = new List<ParsedHighlight>();

            foreach (var group in bookGroups[bookKey])
                group.Emit(highlights, ref duplicatesRemoved);

            if (highlights.Count > 0)
                books.Add(new ParsedBook(bookKey.Title, bookKey.Author, highlights));
        }

        return new ParseResult(books, totalEntriesProcessed, duplicatesRemoved);
    }

    /// <summary>What makes two clippings describe the same annotation in a book.</summary>
    /// <param name="Title">Book title.</param>
    /// <param name="Author">Book author, or <c>null</c>.</param>
    /// <param name="PositionKind">Position counter kind, or <c>null</c>.</param>
    /// <param name="PositionStart">Leading number of the position, or <c>null</c>.</param>
    /// <param name="AnchoredText">
    /// For a source that reports no position, the passage a clipping is anchored to; <c>null</c>
    /// when the position identifies the group instead.
    /// </param>
    private readonly record struct GroupKey(
        string Title,
        string? Author,
        string? PositionKind,
        int? PositionStart,
        string? AnchoredText);

    /// <summary>The clippings that share a book and a source position.</summary>
    private sealed class ClipGroup
    {
        private readonly List<RawClipping> _passages = [];
        private readonly List<RawClipping> _notes = [];
        private readonly HashSet<string> _passageTexts = new(StringComparer.Ordinal);
        private readonly HashSet<string> _noteTexts = new(StringComparer.Ordinal);

        /// <summary>Adds a clipping, or reports that an identical one is already in the group.</summary>
        /// <param name="clip">Clipping to add.</param>
        /// <returns><c>false</c> when the group already holds an identical passage or note.</returns>
        public bool Add(RawClipping clip)
        {
            var note = string.IsNullOrWhiteSpace(clip.Note) ? null : clip.Note;

            if (note is not null)
            {
                // A note is never a passage of its own: it belongs to the passage the group holds,
                // and keeps its anchored text only for the case where no passage row survives.
                if (!_noteTexts.Add(note))
                    return false;

                _notes.Add(clip);
                return true;
            }

            if (string.IsNullOrEmpty(clip.Text))
                return true; // a bookmark: nothing to import, nothing duplicated

            if (!_passageTexts.Add(clip.Text))
                return false;

            _passages.Add(clip);
            return true;
        }

        /// <summary>Turns the group into annotations, attaching the latest note to its first passage.</summary>
        /// <param name="highlights">Annotations emitted so far, appended to in reading order.</param>
        /// <param name="duplicatesRemoved">Running count of entries discarded as duplicates.</param>
        public void Emit(List<ParsedHighlight> highlights, ref int duplicatesRemoved)
        {
            if (_passages.Count == 0)
            {
                // No passage survived to attach to, so the note becomes the annotation. A Kobo note
                // carries its anchored passage as text; a Kindle note carries none.
                foreach (var note in _notes)
                    highlights.Add(new ParsedHighlight(note.Text, note.Location, note.AddedOn, note.Note, note.Location));

                return;
            }

            var attached = _notes.Count == 0
                ? null
                : _notes.OrderBy(note => note.AddedOn is null).ThenBy(note => note.AddedOn).Last();

            // The winner is the annotation; every other note in the group was superseded by it.
            duplicatesRemoved += _notes.Count == 0 ? 0 : _notes.Count - 1;

            for (var index = 0; index < _passages.Count; index++)
            {
                var passage = _passages[index];
                var note = index == 0 ? attached?.Note : null;

                highlights.Add(new ParsedHighlight(passage.Text, passage.Location, passage.AddedOn, note, passage.Location));
            }
        }
    }
}
