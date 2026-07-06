using Relego.Cli.Sources;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Sources;

public sealed class HighlightSourceResolverTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private static HighlightSourceResolver CreateResolver()
        => new([new KindleClippingsSource(), new KoboReaderSource()]);

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "relego-resolver-" + Guid.NewGuid().ToString("N"));
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

    // ── T029 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ClippingsFile_RoutesToKindle()
    {
        var dir = NewTempDir();
        var clippings = Path.Combine(dir, "My Clippings.txt");
        File.WriteAllText(clippings, "ignored");

        var resolution = CreateResolver().Resolve(clippings);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kindle", resolved.Source.Descriptor.Id);
        Assert.Equal(clippings, resolved.ResolvedPath);
    }

    [Fact]
    public void Resolve_RenamedTxtFile_RoutesToKindle()
    {
        var dir = NewTempDir();
        var clippings = Path.Combine(dir, "kindle-export.txt");
        File.WriteAllText(clippings, "ignored");

        var resolution = CreateResolver().Resolve(clippings);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kindle", resolved.Source.Descriptor.Id);
        Assert.Equal(clippings, resolved.ResolvedPath);
    }

    [Fact]
    public void Resolve_DirectoryWithDocumentsClippings_RoutesToKindle()
    {
        var dir = NewTempDir();
        var documents = Path.Combine(dir, "documents");
        Directory.CreateDirectory(documents);
        var clippings = Path.Combine(documents, "My Clippings.txt");
        File.WriteAllText(clippings, "ignored");

        var resolution = CreateResolver().Resolve(dir);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kindle", resolved.Source.Descriptor.Id);
        Assert.Equal(clippings, resolved.ResolvedPath);
    }

    // ── T030 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_KoboReaderFile_RoutesToKobo()
    {
        var dir = NewTempDir();
        var kobo = Path.Combine(dir, "KoboReader.sqlite");
        File.Copy(TestFixtures.KoboFixturePath(), kobo);

        var resolution = CreateResolver().Resolve(kobo);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kobo", resolved.Source.Descriptor.Id);
        Assert.Equal(kobo, resolved.ResolvedPath);
    }

    [Fact]
    public void Resolve_RenamedSqliteFile_RoutesToKoboViaHeaderSniff()
    {
        var dir = NewTempDir();
        var renamed = Path.Combine(dir, "backup.db");
        File.Copy(TestFixtures.KoboFixturePath(), renamed);

        var resolution = CreateResolver().Resolve(renamed);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kobo", resolved.Source.Descriptor.Id);
        Assert.Equal(renamed, resolved.ResolvedPath);
    }

    [Fact]
    public void Resolve_DirectoryWithKoboDatabase_RoutesToKobo()
    {
        var dir = NewTempDir();
        var koboDir = Path.Combine(dir, ".kobo");
        Directory.CreateDirectory(koboDir);
        var kobo = Path.Combine(koboDir, "KoboReader.sqlite");
        File.Copy(TestFixtures.KoboFixturePath(), kobo);

        var resolution = CreateResolver().Resolve(dir);

        Assert.True(resolution.Found);
        var resolved = Assert.Single(resolution.Sources);
        Assert.Equal("kobo", resolved.Source.Descriptor.Id);
        Assert.Equal(kobo, resolved.ResolvedPath);
    }

    // ── T031 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DirectoryWithNeitherSource_ReturnsNotFoundWithBothProbedLocations()
    {
        var dir = NewTempDir();

        var resolution = CreateResolver().Resolve(dir);

        Assert.False(resolution.Found);
        Assert.Empty(resolution.Sources);
        Assert.Contains(resolution.ProbedLocations, p => p.EndsWith("My Clippings.txt", StringComparison.Ordinal));
        Assert.Contains(
            resolution.ProbedLocations,
            p => p.EndsWith(Path.Combine(".kobo", "KoboReader.sqlite"), StringComparison.Ordinal));
    }

    // ── T032 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DirectoryWithBothSources_ReturnsBothWithoutPrecedence()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "My Clippings.txt"), "ignored");
        var koboDir = Path.Combine(dir, ".kobo");
        Directory.CreateDirectory(koboDir);
        File.Copy(TestFixtures.KoboFixturePath(), Path.Combine(koboDir, "KoboReader.sqlite"));

        var resolution = CreateResolver().Resolve(dir);

        Assert.True(resolution.Found);
        Assert.Equal(2, resolution.Sources.Count);
        Assert.Contains(resolution.Sources, s => s.Source.Descriptor.Id == "kindle");
        Assert.Contains(resolution.Sources, s => s.Source.Descriptor.Id == "kobo");
    }
}
