using Relego.Core.Parsing;

namespace Relego.Tests.Parsing;

/// <summary>
/// Covers the note-attachment rules of research.md R2: a note joins the passage that starts at the
/// same position, never the passage whose text it resembles.
/// </summary>
public sealed class HighlightAggregatorTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 1, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aggregate_NoteAtLocationStart_AttachesToTheRangePassage()
    {
        // Kindle writes the note as "Location 1234" and the passage as "Location 1234-1236".
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 1234-1236", "The passage.", Day1),
            Note("Location 1234", "My remark.", Day2),
        ], totalEntriesProcessed: 2);

        var highlight = Assert.Single(result.Books[0].Highlights);
        Assert.Equal("The passage.", highlight.Text);
        Assert.Equal("My remark.", highlight.Note);
        Assert.Equal("Location 1234-1236", highlight.Location);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public void Aggregate_NoteMatchingOnlyOnText_DoesNotAttach()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 100-110", "A passage mentioning a remark.", Day1),
            Note("Location 500", "A passage mentioning a remark.", Day2),
        ], totalEntriesProcessed: 2);

        var book = Assert.Single(result.Books);
        Assert.Equal(2, book.Highlights.Count);
        var annotated = Assert.Single(book.Highlights, h => h.Text.Length > 0);
        Assert.Null(annotated.Note);

        var noteOnly = Assert.Single(book.Highlights, h => h.Text.Length == 0);
        Assert.Equal("A passage mentioning a remark.", noteOnly.Note);
    }

    [Fact]
    public void Aggregate_NoteWithoutMatchingPassage_BecomesAnEmptyPassageWithItsNote()
    {
        var result = HighlightAggregator.Aggregate([Note("Location 42", "Standalone.", Day1)], 1);

        var highlight = Assert.Single(result.Books[0].Highlights);
        Assert.Equal(string.Empty, highlight.Text);
        Assert.Equal("Standalone.", highlight.Note);
    }

    [Fact]
    public void Aggregate_SeveralNotesForOnePassage_LatestWinsAndOthersCountAsDuplicates()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 200-210", "A passage.", Day1),
            Note("Location 200", "First attempt.", Day1),
            Note("Location 200", "Final wording.", Day2),
        ], totalEntriesProcessed: 3);

        var highlight = Assert.Single(result.Books[0].Highlights);
        Assert.Equal("Final wording.", highlight.Note);
        Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Equal(3, result.TotalEntriesProcessed);
    }

    [Fact]
    public void Aggregate_IdenticalNoteTwice_CountsAsDuplicate()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 300-310", "A passage.", Day1),
            Note("Location 300", "Same words.", Day1),
            Note("Location 300", "Same words.", Day2),
        ], totalEntriesProcessed: 3);

        var highlight = Assert.Single(result.Books[0].Highlights);
        Assert.Equal("Same words.", highlight.Note);
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    [Fact]
    public void Aggregate_MultiLineNote_KeepsLineBreaks()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 400-410", "A passage.", Day1),
            Note("Location 400", "first line\nsecond line", Day2),
        ], totalEntriesProcessed: 2);

        Assert.Equal("first line\nsecond line", Assert.Single(result.Books[0].Highlights).Note);
    }

    [Fact]
    public void Aggregate_NoteWithOnlyAWhitespaceBody_AttachesNothing()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 500-510", "A passage.", Day1),
            Note("Location 500", "   ", Day2),
        ], totalEntriesProcessed: 2);

        var highlight = Assert.Single(result.Books[0].Highlights);
        Assert.Equal("A passage.", highlight.Text);
        Assert.Null(highlight.Note);
    }

    [Fact]
    public void Aggregate_NotesAreScopedToTheirBook()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 600-610", "Passage in A.", Day1, title: "Book A"),
            Highlight("Location 600-610", "Passage in B.", Day1, title: "Book B"),
            Note("Location 600", "Remark for A.", Day2, title: "Book A"),
        ], totalEntriesProcessed: 3);

        var bookA = Assert.Single(result.Books, b => b.Title == "Book A");
        var bookB = Assert.Single(result.Books, b => b.Title == "Book B");
        Assert.Equal("Remark for A.", Assert.Single(bookA.Highlights).Note);
        Assert.Null(Assert.Single(bookB.Highlights).Note);
    }

    [Fact]
    public void Aggregate_NotesWithDifferentPositionKinds_DoNotAttach()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 700-710", "A passage.", Day1),
            Note("Page 700", "A page-bound remark.", Day2),
        ], totalEntriesProcessed: 2);

        Assert.Equal(2, result.Books[0].Highlights.Count);
    }

    [Fact]
    public void Aggregate_Bookmarks_AreNeitherImportedNorCountedAsDuplicates()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 800-810", "A passage.", Day1),
            new RawClipping("Book", "Author", "Location 820", Day1, Text: string.Empty),
        ], totalEntriesProcessed: 2);

        Assert.Equal("A passage.", Assert.Single(result.Books[0].Highlights).Text);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public void Aggregate_DuplicatePassage_IsRemoved()
    {
        var result = HighlightAggregator.Aggregate(
        [
            Highlight("Location 900-910", "A passage.", Day1),
            Highlight("Location 900-910", "A passage.", Day2),
        ], totalEntriesProcessed: 2);

        Assert.Equal("A passage.", Assert.Single(result.Books[0].Highlights).Text);
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    private static RawClipping Highlight(
        string location,
        string text,
        DateTimeOffset addedOn,
        string title = "A Book")
        => new(title, "Author", location, addedOn, text);

    private static RawClipping Note(
        string location,
        string note,
        DateTimeOffset addedOn,
        string title = "A Book")
        => new(title, "Author", location, addedOn, Text: string.Empty, Note: note);
}
