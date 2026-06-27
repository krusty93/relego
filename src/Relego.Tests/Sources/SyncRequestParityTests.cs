using Relego.Cli.Import;
using Relego.Cli.Parsing;
using Relego.Cli.Sources;
using Relego.Core.Contracts;

namespace Relego.Tests.Sources;

/// <summary>
/// Proves the import mapping is source-agnostic: a Kobo <see cref="ParseResult"/>
/// passed through the existing <see cref="ClippingsImportWorkflow.CreateSyncRequest"/>
/// produces a <see cref="SyncRequest"/> whose Books/Highlights shape is identical to
/// the Kindle path — no Kobo-specific delivery code is exercised (FR-014, FR-015, SC-006).
/// </summary>
public sealed class SyncRequestParityTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

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

    [Fact]
    public async Task CreateSyncRequest_KoboResult_HasIdenticalShapeToKindleResult()
    {
        // Kindle: one book, one highlight + one note, via the existing parser.
        var kindleInput =
            "A Book (Author One)\n" +
            "- Your Highlight on Location 100 | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            "\n" +
            "first passage\n" +
            "==========\n" +
            "A Book (Author One)\n" +
            "- Your Note on Location 110 | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            "\n" +
            "my comment\n" +
            "==========\n";
        using var reader = new StringReader(kindleInput);
        var kindleResult = await ClippingsParser.ParseAsync(reader);

        // Kobo: the same logical content (highlight then note, by DateCreated).
        var db = KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author One")],
            bookmarks:
            [
                new("book-1", Text: "first passage", DateCreated: "2022-01-01T10:00:00.000"),
                new("book-1", Text: "passage", Annotation: "my comment", Type: "note", DateCreated: "2022-01-01T11:00:00.000"),
            ]);
        _tempFiles.Add(db);
        var koboResult = await new KoboReaderSource().ReadAsync(db);

        var kindleRequest = ClippingsImportWorkflow.CreateSyncRequest(kindleResult);
        var koboRequest = ClippingsImportWorkflow.CreateSyncRequest(koboResult);

        Assert.Equal(Shape(kindleRequest), Shape(koboRequest));
    }

    [Fact]
    public async Task CreateSyncRequest_KoboResult_MapsEveryHighlightFaithfully()
    {
        var db = KoboTestDatabase.Create(
            books: [new("book-1", "A Book", "Author One")],
            bookmarks: [new("book-1", Text: "the passage", DateCreated: "2022-01-01T10:00:00.000")]);
        _tempFiles.Add(db);
        var koboResult = await new KoboReaderSource().ReadAsync(db);

        var request = ClippingsImportWorkflow.CreateSyncRequest(koboResult);

        var book = Assert.Single(request.Books);
        Assert.Equal("A Book", book.Title);
        Assert.Equal("Author One", book.Author);
        var highlight = Assert.Single(book.Highlights);
        Assert.Equal("the passage", highlight.Text);
        Assert.Equal(koboResult.Books.Single().Highlights.Single().AddedOn, highlight.AddedOn);
    }

    // A source-agnostic projection of the request: titles, authors, and ordered highlight texts.
    private static List<(string Title, string? Author, string Texts)> Shape(SyncRequest request)
        => request.Books
            .Select(b => (b.Title, b.Author, string.Join("\u241F", b.Highlights.Select(h => h.Text))))
            .ToList();
}
