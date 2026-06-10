using System.IO.Compression;
using System.Text;
using Relego.Server.Services;

namespace Relego.Tests.Recap;

public sealed class EpubComposerTests
{
    private static readonly DateTimeOffset RecapDate = new(2026, 4, 20, 18, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<SelectionCandidate> SampleHighlights =
    [
        new(1, "The only way to do great work is to love what you do.", "Steve Jobs Biography", "Walter Isaacson", 5, null, RecapDate.AddDays(-30), 10),
        new(2, "In the middle of difficulty lies opportunity.", "Collected Works", "Albert Einstein", 3, RecapDate.AddDays(-7), RecapDate.AddDays(-60), 8),
        new(3, "Text with <special> & \"characters\"", "Book & Title", "Author <Name>", 2, null, RecapDate.AddDays(-10), 5),
    ];

    [Fact]
    public void Compose_ReturnsValidZipArchive()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.True(archive.Entries.Count > 0);
    }

    [Fact]
    public void Compose_MimetypeIsFirstEntry()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var firstEntry = archive.Entries[0];
        Assert.Equal("mimetype", firstEntry.FullName);
    }

    [Fact]
    public void Compose_MimetypeIsUncompressed()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var mimetypeEntry = archive.GetEntry("mimetype")!;
        // Uncompressed: CompressedLength == Length
        Assert.Equal(mimetypeEntry.Length, mimetypeEntry.CompressedLength);
    }

    [Fact]
    public void Compose_MimetypeContentIsCorrect()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var content = ReadEntry(archive, "mimetype");
        Assert.Equal("application/epub+zip", content);
    }

    [Fact]
    public void Compose_ContainsRequiredEpubFiles()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("META-INF/container.xml"));
        Assert.NotNull(archive.GetEntry("OEBPS/content.opf"));
        Assert.NotNull(archive.GetEntry("OEBPS/toc.ncx"));
        Assert.NotNull(archive.GetEntry("OEBPS/highlights.xhtml"));
        Assert.NotNull(archive.GetEntry("OEBPS/cover.png"));
        Assert.NotNull(archive.GetEntry("OEBPS/cover.xhtml"));
    }

    [Fact]
    public void Compose_CoverPng_IsValidRasterImage()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var coverBytes = ReadEntryBytes(archive, "OEBPS/cover.png");
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], coverBytes.Take(8).ToArray());
        Assert.True(coverBytes.Length > 4096);
    }

    [Fact]
    public void Compose_CoverXhtml_ReferencesRasterCoverImage()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var coverXhtml = ReadEntry(archive, "OEBPS/cover.xhtml");
        Assert.Contains("@page", coverXhtml);
        Assert.Contains("overflow: hidden;", coverXhtml);
        Assert.Contains("background: #b56b39;", coverXhtml);
        Assert.Contains("cover.png", coverXhtml);
        Assert.Contains("object-fit: cover;", coverXhtml);
        Assert.Contains("height: 100%;", coverXhtml);
        Assert.Contains("width: 100%;", coverXhtml);
        Assert.DoesNotContain("<svg", coverXhtml);
    }

    [Fact]
    public void Compose_ContentOpf_HasCoverImageManifestItem()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("id=\"cover-image\" href=\"cover.png\" media-type=\"image/png\"", opf);
    }

    [Fact]
    public void Compose_ContentOpf_HasCoverMetaTag()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("<meta name=\"cover\" content=\"cover-image\"/>", opf);
    }

    [Fact]
    public void Compose_ContentOpf_HasCoverGuideReference()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("<reference type=\"cover\" title=\"Cover\" href=\"cover.xhtml\"/>", opf);
    }

    [Fact]
    public void Compose_ContainerXmlPointsToContentOpf()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var containerXml = ReadEntry(archive, "META-INF/container.xml");
        Assert.Contains("OEBPS/content.opf", containerXml);
        Assert.Contains("application/oebps-package+xml", containerXml);
    }

    [Fact]
    public void Compose_ContentOpf_TitleIsNotesRecapWithDateTimeAndTag()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var opf = ReadEntry(archive, "OEBPS/content.opf");
        Assert.Contains("Notes Recap (2026-04-20 18:00)", opf);
        Assert.Contains("<dc:subject>relego.app</dc:subject>", opf);
    }

    [Fact]
    public void Compose_HighlightsXhtml_DailyHeadingIncludesCadenceAndDateTime()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "Daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");
        Assert.Contains("<h1>Relego Daily Recap (2026-04-20 18:00)</h1>", xhtml);
    }

    [Fact]
    public void Compose_HighlightsXhtml_WeeklyHeadingIncludesCadenceAndDateTime()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "Weekly");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");
        Assert.Contains("<h1>Relego Weekly Recap (2026-04-20 18:00)</h1>", xhtml);
    }

    [Fact]
    public void Compose_HighlightsXhtml_NoEmDashBeforeBookTitle()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");
        Assert.DoesNotContain("— <em>", xhtml);
        Assert.Contains("<em>Steve Jobs Biography</em>", xhtml);
    }

    [Fact]
    public void Compose_HighlightsXhtml_RendersFlatList()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");
        Assert.Contains("<ul>", xhtml);
        Assert.Contains("</ul>", xhtml);
        Assert.Contains("<li>", xhtml);
    }

    [Fact]
    public void Compose_HighlightsXhtml_PreservesInputOrder()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");

        var firstIndex = xhtml.IndexOf("The only way to do great work", StringComparison.Ordinal);
        var secondIndex = xhtml.IndexOf("In the middle of difficulty", StringComparison.Ordinal);
        var thirdIndex = xhtml.IndexOf("special", StringComparison.Ordinal);

        Assert.True(firstIndex < secondIndex, "First highlight should appear before second");
        Assert.True(secondIndex < thirdIndex, "Second highlight should appear before third");
    }

    [Fact]
    public void Compose_HighlightsXhtml_IncludesSourceMetadata()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");

        // Each highlight should include book title and author
        Assert.Contains("Steve Jobs Biography", xhtml);
        Assert.Contains("Walter Isaacson", xhtml);
        Assert.Contains("Collected Works", xhtml);
        Assert.Contains("Albert Einstein", xhtml);
    }

    [Fact]
    public void Compose_HighlightsXhtml_EscapesSpecialCharacters()
    {
        var epub = EpubComposer.Compose(SampleHighlights, RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");

        // Special characters must be escaped
        Assert.Contains("&lt;special&gt;", xhtml);
        Assert.Contains("&amp;", xhtml);
        Assert.Contains("&quot;characters&quot;", xhtml);
        Assert.Contains("Book &amp; Title", xhtml);
        Assert.Contains("Author &lt;Name&gt;", xhtml);
    }

    [Fact]
    public void Compose_EmptyHighlights_ProducesValidEpubWithEmptyList()
    {
        var epub = EpubComposer.Compose([], RecapDate, "daily");

        using var stream = new MemoryStream(epub);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("OEBPS/highlights.xhtml"));
        var xhtml = ReadEntry(archive, "OEBPS/highlights.xhtml");
        Assert.Contains("<ul>", xhtml);
        Assert.Contains("</ul>", xhtml);
        Assert.DoesNotContain("<li>", xhtml);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
