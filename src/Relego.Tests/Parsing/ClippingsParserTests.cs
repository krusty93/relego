using Microsoft.Extensions.Logging;
using Relego.Cli.Parsing;

namespace Relego.Tests.Parsing;

public class ClippingsParserTests
{
    #region T009 — Standard multi-book parsing

    [Fact]
    public async Task ParseAsync_StandardInput_ReturnsCorrectCountsAndHighlights()
    {
        // Arrange — 3 highlights across 2 books
        var input = """
            The Great Gatsby (F. Scott Fitzgerald)
            - Your Highlight on Location 100-105 | Added on Thursday, January 1, 2026 12:00:00 AM

            In my younger and more vulnerable years my father gave me some advice.
            ==========
            1984 (Orwell, George)
            - Your Highlight on Location 200-210 | Added on Friday, January 2, 2026 1:30:00 PM

            War is peace. Freedom is slavery. Ignorance is strength.
            ==========
            The Great Gatsby (F. Scott Fitzgerald)
            - Your Highlight on Location 150-160 | Added on Saturday, January 3, 2026 9:00:00 AM

            So we beat on, boats against the current, borne back ceaselessly into the past.
            ==========
            """;

        using var reader = new StringReader(input);

        // Act
        var result = await ClippingsParser.ParseAsync(reader);

        // Assert
        Assert.Equal(3, result.TotalEntriesProcessed);
        Assert.Equal(2, result.Books.Count);

        var gatsby = result.Books.Single(b => b.Title == "The Great Gatsby");
        Assert.Equal("F. Scott Fitzgerald", gatsby.Author);
        Assert.Equal(2, gatsby.Highlights.Count);
        Assert.Equal("In my younger and more vulnerable years my father gave me some advice.", gatsby.Highlights[0].Text);
        Assert.Equal("So we beat on, boats against the current, borne back ceaselessly into the past.", gatsby.Highlights[1].Text);

        var orwell = result.Books.Single(b => b.Title == "1984");
        Assert.Equal("Orwell, George", orwell.Author);
        Assert.Single(orwell.Highlights);
        Assert.Equal("War is peace. Freedom is slavery. Ignorance is strength.", orwell.Highlights[0].Text);
    }

    #endregion

    #region T010 — Multi-line highlight text

    [Fact]
    public async Task ParseAsync_MultiLineHighlight_PreservesNewlines()
    {
        var input = """
            Some Book (Author)
            - Your Highlight on Location 10-20 | Added on Thursday, January 1, 2026 12:00:00 AM

            First line of highlight.
            Second line continues here.
            Third line ends the highlight.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        var highlight = result.Books[0].Highlights[0];
        Assert.Contains("First line of highlight.", highlight.Text);
        Assert.Contains("Second line continues here.", highlight.Text);
        Assert.Contains("Third line ends the highlight.", highlight.Text);
        Assert.Contains("\n", highlight.Text);
    }

    #endregion

    #region T011 — Title/author extraction edge cases

    [Theory]
    [InlineData("Book Title (Author Name)", "Book Title", "Author Name")]
    [InlineData("1984 (Orwell, George)", "1984", "Orwell, George")]
    public async Task ParseAsync_StandardAuthorFormats_ExtractsCorrectly(
        string titleLine, string expectedTitle, string expectedAuthor)
    {
        var input = $"""
            {titleLine}
            - Your Highlight on Location 10-20 | Added on Thursday, January 1, 2026 12:00:00 AM

            Some text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal(expectedTitle, result.Books[0].Title);
        Assert.Equal(expectedAuthor, result.Books[0].Author);
    }

    [Fact]
    public async Task ParseAsync_TitleWithNestedParens_ExtractsLastParenGroupAsAuthor()
    {
        var input = """
            The Art of War (Annotated) (Sun Tzu)
            - Your Highlight on Location 10-20 | Added on Thursday, January 1, 2026 12:00:00 AM

            Some text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal("The Art of War (Annotated)", result.Books[0].Title);
        Assert.Equal("Sun Tzu", result.Books[0].Author);
    }

    [Fact]
    public async Task ParseAsync_NoParentheses_TitleIsFullLineAuthorIsNull()
    {
        var input = """
            My Personal Notes
            - Your Highlight on Location 10-20 | Added on Thursday, January 1, 2026 12:00:00 AM

            Some text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal("My Personal Notes", result.Books[0].Title);
        Assert.Null(result.Books[0].Author);
    }

    [Fact]
    public async Task ParseAsync_EmptyParens_AuthorIsNull()
    {
        var input = """
            Some Book ()
            - Your Highlight on Location 10-20 | Added on Thursday, January 1, 2026 12:00:00 AM

            Some text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal("Some Book", result.Books[0].Title);
        Assert.Null(result.Books[0].Author);
    }

    #endregion

    #region T012 — Metadata line parsing and entry type handling

    [Fact]
    public async Task ParseAsync_HighlightEntry_ExtractsLocationAndDate()
    {
        var input = """
            Book (Author)
            - Your Highlight on Location 100-105 | Added on Thursday, January 1, 2026 12:00:00 AM

            Highlighted text here.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        var h = result.Books[0].Highlights[0];
        Assert.Equal("Highlighted text here.", h.Text);
        Assert.Equal("Location 100-105", h.Location);
        Assert.NotNull(h.AddedOn);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1))), h.AddedOn);
    }

    [Fact]
    public async Task ParseAsync_NoteEntry_PrependsMyNotePrefix()
    {
        var input = """
            Book (Author)
            - Your Note on Location 50-55 | Added on Thursday, January 1, 2026 12:00:00 AM

            This is my annotation.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        var h = result.Books[0].Highlights[0];
        Assert.Equal("[my note] This is my annotation.", h.Text);
    }

    [Fact]
    public async Task ParseAsync_BookmarkEntry_IsExcludedFromResult()
    {
        var input = """
            Book (Author)
            - Your Bookmark on Location 30 | Added on Thursday, January 1, 2026 12:00:00 AM

            
            ==========
            Book (Author)
            - Your Highlight on Location 100-105 | Added on Thursday, January 1, 2026 12:00:00 AM

            Actual highlight text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal(2, result.TotalEntriesProcessed);
        Assert.Single(result.Books);
        Assert.Single(result.Books[0].Highlights);
        Assert.Equal("Actual highlight text.", result.Books[0].Highlights[0].Text);
    }

    #endregion

    #region T013 — File path overload

    [Fact]
    public async Task ParseAsync_FilePath_ReturnsSameResultAsTextReader()
    {
        var content = """
            The Great Gatsby (F. Scott Fitzgerald)
            - Your Highlight on Location 100-105 | Added on Thursday, January 1, 2026 12:00:00 AM

            In my younger and more vulnerable years.
            ==========
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, content);

            var resultFromFile = await ClippingsParser.ParseAsync(tempFile);

            using var reader = new StringReader(content);
            var resultFromReader = await ClippingsParser.ParseAsync(reader);

            Assert.Equal(resultFromReader.TotalEntriesProcessed, resultFromFile.TotalEntriesProcessed);
            Assert.Equal(resultFromReader.Books.Count, resultFromFile.Books.Count);
            Assert.Equal(resultFromReader.Books[0].Title, resultFromFile.Books[0].Title);
            Assert.Equal(resultFromReader.Books[0].Highlights[0].Text, resultFromFile.Books[0].Highlights[0].Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region T021-T024 — Deduplication (US2)

    [Fact]
    public async Task ParseAsync_DuplicateHighlightSameBook_KeepsOneAndCountsOne()
    {
        var input = """
            1984 (Orwell, George)
            - Your Highlight on Location 200-210 | Added on Friday, January 2, 2026 1:30:00 PM

            War is peace. Freedom is slavery. Ignorance is strength.
            ==========
            1984 (Orwell, George)
            - Your Highlight on Location 200-210 | Added on Friday, January 2, 2026 1:30:00 PM

            War is peace. Freedom is slavery. Ignorance is strength.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Single(result.Books[0].Highlights);
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ParseAsync_SameTextDifferentBooks_BothRetained()
    {
        var input = """
            Book One (Author A)
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            Identical text.
            ==========
            Book Two (Author B)
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            Identical text.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal(2, result.Books.Count);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ParseAsync_SubstringHighlights_BothRetained()
    {
        var input = """
            1984 (Orwell, George)
            - Your Highlight on Location 200-205 | Added on Friday, January 2, 2026 1:00:00 PM

            War is peace.
            ==========
            1984 (Orwell, George)
            - Your Highlight on Location 200-210 | Added on Friday, January 2, 2026 1:30:00 PM

            War is peace. Freedom is slavery. Ignorance is strength.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal(2, result.Books[0].Highlights.Count);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ParseAsync_TenClippingsThreeDuplicates_ReportsCorrectCounts()
    {
        // Build 10 entries: 7 unique + 3 duplicates (entries 8-10 repeat entries 1-3)
        static string Entry(int i) =>
            $"Book {i} (Author {i})\n" +
            $"- Your Highlight on Location {i * 10}-{i * 10 + 5} | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            $"\n" +
            $"Unique highlight number {i}.\n" +
            "==========\n";

        var input = string.Concat(Enumerable.Range(1, 7).Select(Entry))
                  + string.Concat(Enumerable.Range(1, 3).Select(Entry));

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        var totalHighlights = result.Books.Sum(b => b.Highlights.Count);
        Assert.Equal(7, totalHighlights);
        Assert.Equal(3, result.DuplicatesRemoved);
    }

    #endregion

    #region T027-T029 — Grouping by book (US3)

    [Fact]
    public async Task ParseAsync_SixHighlightsThreeBooks_ProducesThreeParsedBooks()
    {
        var input = """
            Dune (Frank Herbert)
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            I must not fear.
            ==========
            Dune (Frank Herbert)
            - Your Highlight on Location 20-25 | Added on Thursday, January 1, 2026 1:00:00 AM

            Fear is the mind-killer.
            ==========
            Foundation (Isaac Asimov)
            - Your Highlight on Location 30-35 | Added on Thursday, January 1, 2026 2:00:00 AM

            Violence is the last refuge of the incompetent.
            ==========
            Foundation (Isaac Asimov)
            - Your Highlight on Location 40-45 | Added on Thursday, January 1, 2026 3:00:00 AM

            It pays to be obvious, especially if you have a reputation for subtlety.
            ==========
            Neuromancer (William Gibson)
            - Your Highlight on Location 50-55 | Added on Thursday, January 1, 2026 4:00:00 AM

            The sky above the port was the color of television.
            ==========
            Neuromancer (William Gibson)
            - Your Highlight on Location 60-65 | Added on Thursday, January 1, 2026 5:00:00 AM

            Cyberspace. A consensual hallucination.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal(3, result.Books.Count);

        var dune = result.Books.Single(b => b.Title == "Dune");
        Assert.Equal("Frank Herbert", dune.Author);
        Assert.Equal(2, dune.Highlights.Count);

        var foundation = result.Books.Single(b => b.Title == "Foundation");
        Assert.Equal("Isaac Asimov", foundation.Author);
        Assert.Equal(2, foundation.Highlights.Count);

        var neuromancer = result.Books.Single(b => b.Title == "Neuromancer");
        Assert.Equal("William Gibson", neuromancer.Author);
        Assert.Equal(2, neuromancer.Highlights.Count);
    }

    [Fact]
    public async Task ParseAsync_TwoBooksSameAuthor_ProducesTwoSeparateBooks()
    {
        var input = """
            Foundation (Isaac Asimov)
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            First highlight.
            ==========
            I Robot (Isaac Asimov)
            - Your Highlight on Location 20-25 | Added on Thursday, January 1, 2026 1:00:00 AM

            Second highlight.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Equal(2, result.Books.Count);
        Assert.Single(result.Books, b => b.Title == "Foundation" && b.Author == "Isaac Asimov");
        Assert.Single(result.Books, b => b.Title == "I Robot" && b.Author == "Isaac Asimov");
    }

    [Fact]
    public async Task ParseAsync_BookWithOnlyBookmarks_DoesNotAppearInResult()
    {
        var input = """
            Bookmarks Only (Some Author)
            - Your Bookmark on Location 10 | Added on Thursday, January 1, 2026 12:00:00 AM

            
            ==========
            Bookmarks Only (Some Author)
            - Your Bookmark on Location 20 | Added on Thursday, January 1, 2026 1:00:00 AM

            
            ==========
            Real Book (Real Author)
            - Your Highlight on Location 30-35 | Added on Thursday, January 1, 2026 2:00:00 AM

            Actual content.
            ==========
            """;

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal("Real Book", result.Books[0].Title);
    }

    #endregion

    #region T032-T036 — Malformed entry handling (US4)

    // Minimal ILogger that captures log messages for assertion.
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    [Fact]
    public async Task ParseAsync_MalformedEntryMissingMetadata_SkipsItAndLogsWarning()
    {
        // Arrange — build 10 valid entries + 1 malformed (title only, no metadata line)
        static string ValidEntry(int i) =>
            $"Book {i} (Author {i})\n" +
            $"- Your Highlight on Location {i * 10}-{i * 10 + 5} | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            $"\n" +
            $"Highlight text {i}.\n" +
            "==========\n";

        var malformed = "Orphaned Title Only\n==========\n";

        var input = string.Concat(Enumerable.Range(1, 5).Select(ValidEntry))
                  + malformed
                  + string.Concat(Enumerable.Range(6, 5).Select(ValidEntry));

        using var reader = new StringReader(input);
        var logger = new CapturingLogger();

        // Act
        var result = await ClippingsParser.ParseAsync(reader, logger);

        // Assert — 10 valid highlights extracted
        var totalHighlights = result.Books.Sum(b => b.Highlights.Count);
        Assert.Equal(10, totalHighlights);

        // At least one warning logged about the malformed entry
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ParseAsync_EmptyInput_ReturnsEmptyResultWithoutException()
    {
        using var reader = new StringReader(string.Empty);

        var result = await ClippingsParser.ParseAsync(reader);

        Assert.NotNull(result);
        Assert.Empty(result.Books);
        Assert.Equal(0, result.TotalEntriesProcessed);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ParseAsync_OnlySeparators_ReturnsEmptyResult()
    {
        var input = "==========\n==========\n==========\n";

        using var reader = new StringReader(input);

        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Empty(result.Books);
        Assert.Equal(0, result.TotalEntriesProcessed);
    }

    [Fact]
    public async Task ParseAsync_MissingAuthorParens_ParsesWithNullAuthorAndIsNotSkipped()
    {
        var input = """
            My Untitled Journal
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            Some highlighted passage.
            ==========
            """;

        using var reader = new StringReader(input);

        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal("My Untitled Journal", result.Books[0].Title);
        Assert.Null(result.Books[0].Author);
        Assert.Single(result.Books[0].Highlights);
    }

    [Fact]
    public async Task ParseAsync_UnrecognizedMetadataType_SkipsEntryAndLogsWarning()
    {
        // "Clip" is not a known type — regex won't match
        var input = """
            Some Book (Some Author)
            - Your Clip on Location 50 | Added on Thursday, January 1, 2026 12:00:00 AM

            Content that should be skipped.
            ==========
            Valid Book (Valid Author)
            - Your Highlight on Location 100-105 | Added on Thursday, January 1, 2026 12:00:00 AM

            Valid highlight text.
            ==========
            """;

        using var reader = new StringReader(input);
        var logger = new CapturingLogger();

        var result = await ClippingsParser.ParseAsync(reader, logger);

        // Only the valid entry is returned
        Assert.Single(result.Books);
        Assert.Equal("Valid Book", result.Books[0].Title);
        Assert.Single(result.Books[0].Highlights);
        Assert.Equal("Valid highlight text.", result.Books[0].Highlights[0].Text);

        // Warning logged for the unrecognized type entry
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    #endregion

    #region T041 — Edge cases: BOM, Unicode, special characters

    [Fact]
    public async Task ParseAsync_FilePath_BomPrefixedFile_ParsesCorrectly()
    {
        // UTF-8 BOM = 0xEF 0xBB 0xBF prepended to file content
        var content = """
            Gatsby (Fitzgerald)
            - Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM

            BOM test highlight.
            ==========
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            // Write with BOM
            await File.WriteAllTextAsync(tempFile, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var result = await ClippingsParser.ParseAsync(tempFile);

            Assert.Single(result.Books);
            Assert.Equal("Gatsby", result.Books[0].Title);
            Assert.Equal("BOM test highlight.", result.Books[0].Highlights[0].Text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("三体 (刘慈欣)", "三体", "刘慈欣")]                          // CJK
    [InlineData("Éloge de la fuite (Henri Laborit)", "Éloge de la fuite", "Henri Laborit")]  // diacritics
    [InlineData("مئة عام من العزلة (ماركيز)", "مئة عام من العزلة", "ماركيز")]             // RTL Arabic
    public async Task ParseAsync_UnicodeBookTitlesAndAuthors_ParsesCorrectly(
        string titleLine, string expectedTitle, string expectedAuthor)
    {
        var input = $"{titleLine}\n" +
                    "- Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
                    "\n" +
                    "Unicode highlight text.\n" +
                    "==========\n";

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal(expectedTitle, result.Books[0].Title);
        Assert.Equal(expectedAuthor, result.Books[0].Author);
    }

    [Fact]
    public async Task ParseAsync_HighlightWithSpecialCharacters_PreservesVerbatim()
    {
        const string specialText = """He said "hello" & she replied <'world'>.""";

        var input = "Book (Author)\n" +
                    "- Your Highlight on Location 10-15 | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
                    "\n" +
                    specialText + "\n" +
                    "==========\n";

        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);

        Assert.Single(result.Books);
        Assert.Equal(specialText, result.Books[0].Highlights[0].Text);
    }

    #endregion

    #region T042 — Performance: 10,000 entries within 5 seconds

    [Fact]
    public async Task ParseAsync_TenThousandEntries_CompletesWithinFiveSeconds()
    {
        static string Entry(int i) =>
            $"Book {i % 100} (Author {i % 20})\n" +
            $"- Your Highlight on Location {i * 2}-{i * 2 + 5} | Added on Thursday, January 1, 2026 12:00:00 AM\n" +
            $"\n" +
            $"Highlight number {i} with some reasonable length text to simulate real data.\n" +
            "==========\n";

        var input = string.Concat(Enumerable.Range(1, 10_000).Select(Entry));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var reader = new StringReader(input);
        var result = await ClippingsParser.ParseAsync(reader);
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 5,
            $"Parsing 10,000 entries took {sw.Elapsed.TotalSeconds:F2}s — exceeded 5s limit.");
        Assert.NotEmpty(result.Books);
    }

    #endregion
}
