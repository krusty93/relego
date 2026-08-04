using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Relego.Core.Sources;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Sources;

public sealed class KoboReaderSourceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string Track(string path)
    {
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    // ── T010 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_Fixture_ReturnsBooksWithCorrectTitleAuthorText()
    {
        var fixture = TestFixtures.KoboFixturePath();

        var result = await new KoboReaderSource().ReadAsync(fixture);

        var cleanCode = result.Books.Single(b => b.Title == "Clean Code");
        Assert.Equal("Robert C. Martin", cleanCode.Author);
        Assert.Equal(3, cleanCode.Highlights.Count);
        Assert.Contains(cleanCode.Highlights, h => h.Text == "Functions should do one thing. They should do it well. They should do it only.");

        var pragmatic = result.Books.Single(b => b.Title == "The Pragmatic Programmer");
        Assert.Equal("David Thomas, Andrew Hunt", pragmatic.Author);
        Assert.Contains(pragmatic.Highlights, h => h.Text.StartsWith("Care About Your Craft", StringComparison.Ordinal));
    }

    // ── T011 ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task ReadAsync_HiddenTruthy_IsExcluded(string hidden)
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "Visible Book", "Author")],
            bookmarks:
            [
                new("book-1", "kept highlight"),
                new("book-1", "soft-deleted highlight", Hidden: hidden),
            ]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var texts = result.Books.SelectMany(b => b.Highlights).Select(h => h.Text).ToList();
        Assert.Contains("kept highlight", texts);
        Assert.DoesNotContain("soft-deleted highlight", texts);
    }

    [Fact]
    public async Task ReadAsync_DogearAndTextLessRows_AreExcluded()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks:
            [
                new("book-1", "real highlight"),
                new("book-1", Text: null, Annotation: null, Type: "dogear"),
                new("book-1", Text: null, Annotation: null, Type: "highlight"),
            ]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var highlight = Assert.Single(result.Books.SelectMany(b => b.Highlights));
        Assert.Equal("real highlight", highlight.Text);
    }

    // ── T012 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_OrphanedBookmark_IsDroppedAndWarningLogged()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks:
            [
                new("book-1", "valid highlight"),
                new("missing-book", "orphaned highlight"),
            ]));

        var logger = new CapturingLogger();
        var result = await new KoboReaderSource().ReadAsync(db, logger);

        var texts = result.Books.SelectMany(b => b.Highlights).Select(h => h.Text).ToList();
        Assert.Contains("valid highlight", texts);
        Assert.DoesNotContain("orphaned highlight", texts);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("orphan", StringComparison.OrdinalIgnoreCase));
    }

    // ── T013 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_DoesNotModifyDeviceFile_NorLeaveSidecars()
    {
        var fixture = TestFixtures.KoboFixturePath();
        var beforeHash = Sha256(fixture);
        var beforeWrite = File.GetLastWriteTimeUtc(fixture);

        await new KoboReaderSource().ReadAsync(fixture);

        Assert.Equal(beforeHash, Sha256(fixture));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(fixture));
        Assert.False(File.Exists(fixture + "-wal"), "no -wal sidecar should be left next to the device file");
        Assert.False(File.Exists(fixture + "-shm"), "no -shm sidecar should be left next to the device file");
        Assert.False(File.Exists(fixture + "-journal"), "no -journal sidecar should be left next to the device file");
    }

    // ── T014 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_NonSqliteFile_ThrowsActionableFailure_AndCleansTemp()
    {
        var notSqlite = Track(Path.Combine(Path.GetTempPath(), "relego-kobotest-" + Guid.NewGuid().ToString("N") + ".bin"));
        await File.WriteAllTextAsync(notSqlite, "this is clearly not a SQLite database file at all");

        var before = CountTempKoboCopies();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => new KoboReaderSource().ReadAsync(notSqlite));
        Assert.Contains("not a valid Kobo database", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<Microsoft.Data.Sqlite.SqliteException>(ex);

        Assert.Equal(before, CountTempKoboCopies());
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsActionableFailure()
    {
        var missing = Path.Combine(Path.GetTempPath(), "relego-kobotest-" + Guid.NewGuid().ToString("N") + ".sqlite");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => new KoboReaderSource().ReadAsync(missing));
        Assert.IsNotType<Microsoft.Data.Sqlite.SqliteException>(ex);
    }

    // ── T015 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_NoImportableRows_ReturnsEmptyResult()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "Empty Book", "Author")],
            bookmarks: []));

        var result = await new KoboReaderSource().ReadAsync(db);

        Assert.Empty(result.Books);
    }

    // ── T016 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_NoteRow_EmitsMyNotePrefixedAnnotation()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks: [new("book-1", Text: "anchored passage", Annotation: "my thought", Type: "note")]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var highlight = Assert.Single(result.Books.SelectMany(b => b.Highlights));
        Assert.Equal("[my note] my thought", highlight.Text);
    }

    [Fact]
    public async Task ReadAsync_NoteWithEmptyAnnotation_FallsBackToText()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks: [new("book-1", Text: "anchored passage", Annotation: null, Type: "note")]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var highlight = Assert.Single(result.Books.SelectMany(b => b.Highlights));
        Assert.Equal("[my note] anchored passage", highlight.Text);
    }

    [Fact]
    public async Task ReadAsync_NoteWithBothEmpty_IsSkipped()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks: [new("book-1", Text: null, Annotation: null, Type: "note")]));

        var result = await new KoboReaderSource().ReadAsync(db);

        Assert.Empty(result.Books);
    }

    // ── T017 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_HighlightAndNoteSameBook_AppearUnderSameGroup()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks:
            [
                new("book-1", Text: "a highlight"),
                new("book-1", Text: "passage", Annotation: "a note", Type: "note"),
            ]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var book = Assert.Single(result.Books);
        Assert.Equal(2, book.Highlights.Count);
        Assert.Contains(book.Highlights, h => h.Text == "a highlight");
        Assert.Contains(book.Highlights, h => h.Text == "[my note] a note");
    }

    // ── T018 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_NotePrefix_IsByteIdenticalToKindleParser()
    {
        const string noteText = "they are indistinguishable across sources";

        // Kindle note via the existing parser.
        var kindleInput =
            "A Book (Author)\n" +
            "- Your Note on Location 100 | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            "\n" +
            noteText + "\n" +
            "==========\n";
        using var reader = new StringReader(kindleInput);
        var kindleResult = await Relego.Core.Parsing.ClippingsParser.ParseAsync(reader);
        var kindleNote = kindleResult.Books.SelectMany(b => b.Highlights).Single().Text;

        // Kobo note for equivalent content.
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks: [new("book-1", Text: null, Annotation: noteText, Type: "note")]));
        var koboResult = await new KoboReaderSource().ReadAsync(db);
        var koboNote = koboResult.Books.SelectMany(b => b.Highlights).Single().Text;

        Assert.Equal(kindleNote, koboNote);
    }

    // ── T019 ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("日本語のハイライト")]                       // CJK
    [InlineData("café déjà-vu naïve")]                       // diacritics
    [InlineData("مرحبا بالعالم")]                            // RTL (Arabic)
    public async Task ReadAsync_Utf8Content_RoundTripsVerbatim(string text)
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", text, text)],
            bookmarks: [new("book-1", Text: text)]));

        var result = await new KoboReaderSource().ReadAsync(db);

        var book = Assert.Single(result.Books);
        Assert.Equal(text, book.Title);
        Assert.Equal(text, book.Author);
        Assert.Equal(text, Assert.Single(book.Highlights).Text);
    }

    [Fact]
    public async Task ReadAsync_NullAttribution_YieldsNullAuthor()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "Anonymous Work", null)],
            bookmarks: [new("book-1", Text: "a highlight")]));

        var result = await new KoboReaderSource().ReadAsync(db);

        Assert.Null(Assert.Single(result.Books).Author);
    }

    [Fact]
    public async Task ReadAsync_OrphanedRowAmongValidRows_SkipsAndLogsButImportsValid()
    {
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author")],
            bookmarks:
            [
                new("book-1", "first valid"),
                new("ghost", "orphan"),
                new("book-1", "second valid"),
            ]));

        var logger = new CapturingLogger();
        var result = await new KoboReaderSource().ReadAsync(db, logger);

        var texts = result.Books.SelectMany(b => b.Highlights).Select(h => h.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains("first valid", texts);
        Assert.Contains("second valid", texts);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    // ── T020 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_TenThousandRows_CompletesWithinFiveSeconds()
    {
        var bookmarks = Enumerable.Range(0, 10_000)
            .Select(i => new KoboTestDatabase.BookmarkRow("book-1", $"highlight number {i}"))
            .ToList();
        var db = Track(KoboTestDatabase.Create(
            books: [new("book-1", "Big Book", "Author")],
            bookmarks: bookmarks));

        var stopwatch = Stopwatch.StartNew();
        var result = await new KoboReaderSource().ReadAsync(db);
        stopwatch.Stop();

        Assert.Equal(10_000, result.Books.Sum(b => b.Highlights.Count));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Read took {stopwatch.Elapsed.TotalSeconds:F2}s, expected < 5s");
    }

    private static int CountTempKoboCopies()
        => Directory.EnumerateFiles(Path.GetTempPath(), "relego-kobo-*.sqlite").Count();

    private static byte[] Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
