using Relego.Cli.Sources;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Sources;

public sealed class KindleClippingsSourceTests : IDisposable
{
    private const string ClippingsFileName = "My Clippings.txt";

    private readonly List<string> _tempDirs = [];

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "relego-kindlesource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    // ── Descriptor ──────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IdentifiesKindle()
    {
        var descriptor = new KindleClippingsSource().Descriptor;

        Assert.Equal("kindle", descriptor.Id);
        Assert.Equal("Kindle", descriptor.DisplayName);
    }

    // ── ReadAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_Fixture_ReturnsBooksWithCorrectTitleAuthorText()
    {
        var fixture = TestFixtures.KindleFixturePath();

        var result = await new KindleClippingsSource().ReadAsync(fixture);

        Assert.Equal(5, result.Books.Count);

        var cleanCode = result.Books.Single(b => b.Title == "Clean Code");
        Assert.Equal("Robert C. Martin", cleanCode.Author);
        Assert.Equal(3, cleanCode.Highlights.Count);
        Assert.Contains(
            cleanCode.Highlights,
            h => h.Text == "Functions should do one thing. They should do it well. They should do it only.");

        var foundation = result.Books.Single(b => b.Title == "Foundation");
        Assert.Equal("Isaac Asimov", foundation.Author);
        Assert.Contains(foundation.Highlights, h => h.Text == "Violence is the last refuge of the incompetent.");
    }

    [Fact]
    public async Task ReadAsync_Fixture_SurfacesParsedTimestamps()
    {
        var fixture = TestFixtures.KindleFixturePath();

        var result = await new KindleClippingsSource().ReadAsync(fixture);

        Assert.All(
            result.Books.SelectMany(b => b.Highlights),
            highlight => Assert.NotNull(highlight.AddedOn));
    }

    [Fact]
    public async Task ReadAsync_TempClippingsFile_ParsesBookAndHighlight()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, ClippingsFileName);
        await File.WriteAllTextAsync(path, """
            Dune (Frank Herbert)
            - Your Highlight on Location 10-12 | Added on Monday, January 10, 2022 9:15:00 AM

            Fear is the mind-killer.
            ==========
            """);

        var result = await new KindleClippingsSource().ReadAsync(path);

        var book = Assert.Single(result.Books);
        Assert.Equal("Dune", book.Title);
        Assert.Equal("Frank Herbert", book.Author);
        Assert.Equal("Fear is the mind-killer.", Assert.Single(book.Highlights).Text);
    }

    // ── Locate ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Locate_ExistingClippingsFile_ReturnsThatPath()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, ClippingsFileName);
        File.WriteAllText(path, "ignored");

        var probe = new KindleClippingsSource().Locate(path);

        Assert.Equal(path, probe.FoundPath);
        Assert.Equal(new[] { path }, probe.ProbedLocations);
    }

    [Fact]
    public void Locate_ExistingRenamedTxtFile_ReturnsThatPath()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "kindle-export.txt");
        File.WriteAllText(path, "ignored");

        var probe = new KindleClippingsSource().Locate(path);

        Assert.Equal(path, probe.FoundPath);
        Assert.Equal(new[] { path }, probe.ProbedLocations);
    }

    [Fact]
    public void Locate_MissingClippingsFileByName_ReturnsNullButProbesIt()
    {
        var path = Path.Combine(NewTempDir(), ClippingsFileName);

        var probe = new KindleClippingsSource().Locate(path);

        Assert.Null(probe.FoundPath);
        Assert.Equal(new[] { path }, probe.ProbedLocations);
    }

    [Fact]
    public void Locate_CaseInsensitiveFileName_IsMatched()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "my clippings.TXT");
        File.WriteAllText(path, "ignored");

        var probe = new KindleClippingsSource().Locate(path);

        Assert.Equal(path, probe.FoundPath);
    }

    [Fact]
    public void Locate_DirectoryWithDocumentsClippings_ResolvesDocumentsFirst()
    {
        var dir = NewTempDir();
        var documents = Path.Combine(dir, "documents");
        Directory.CreateDirectory(documents);
        var clippings = Path.Combine(documents, ClippingsFileName);
        File.WriteAllText(clippings, "ignored");

        var probe = new KindleClippingsSource().Locate(dir);

        Assert.Equal(clippings, probe.FoundPath);
        Assert.Equal(clippings, probe.ProbedLocations[0]);
    }

    [Fact]
    public void Locate_DirectoryWithTopLevelClippings_ResolvesAfterProbingDocuments()
    {
        var dir = NewTempDir();
        var clippings = Path.Combine(dir, ClippingsFileName);
        File.WriteAllText(clippings, "ignored");

        var probe = new KindleClippingsSource().Locate(dir);

        Assert.Equal(clippings, probe.FoundPath);
        Assert.Contains(
            probe.ProbedLocations,
            p => p.EndsWith(Path.Combine("documents", ClippingsFileName), StringComparison.Ordinal));
        Assert.Contains(clippings, probe.ProbedLocations);
    }

    [Fact]
    public void Locate_DirectoryWithoutClippings_ReturnsNullWithBothProbedLocations()
    {
        var dir = NewTempDir();

        var probe = new KindleClippingsSource().Locate(dir);

        Assert.Null(probe.FoundPath);
        Assert.Contains(
            probe.ProbedLocations,
            p => p.EndsWith(Path.Combine("documents", ClippingsFileName), StringComparison.Ordinal));
        Assert.Contains(probe.ProbedLocations, p => p.EndsWith(ClippingsFileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Locate_UnrelatedNonexistentPath_ReturnsNull()
    {
        var path = Path.Combine(NewTempDir(), "highlights.csv");

        var probe = new KindleClippingsSource().Locate(path);

        Assert.Null(probe.FoundPath);
    }
}
