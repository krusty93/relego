using System.IO.Compression;
using System.Reflection;
using System.Text;
using Relego.Core.Branding;

namespace Relego.Server.Services;

public static class EpubComposer
{
    private const string CoverImageResourceName = "Relego.Server.Assets.epub-cover.png";
    private static readonly byte[] CoverImageBytes = ReadEmbeddedBinary(CoverImageResourceName);

    public static byte[] Compose(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string cadence)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype must be first entry, stored uncompressed
            var mimetypeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetypeEntry.Open(), Encoding.ASCII))
            {
                writer.Write("application/epub+zip");
            }

            AddEntry(archive, "META-INF/container.xml", BuildContainerXml());
            AddBinaryEntry(archive, "OEBPS/cover.png", CoverImageBytes);
            AddEntry(archive, "OEBPS/cover.xhtml", BuildCoverXhtml());
            AddEntry(archive, "OEBPS/content.opf", BuildContentOpf(recapDate));
            AddEntry(archive, "OEBPS/toc.ncx", BuildTocNcx());
            AddEntry(archive, "OEBPS/highlights.xhtml", BuildHighlightsXhtml(highlights, recapDate, cadence));
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void AddBinaryEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static string BuildContainerXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static string BuildCoverXhtml()
    {
        var accentColor = ToHex(BrandColors.Light.Accent);

        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
            <title>Cover</title>
            <style type="text/css">
              @page {
                margin: 0;
              }
              html, body {
                margin: 0;
                padding: 0;
                width: 100%;
                height: 100%;
                overflow: hidden;
                background: {{accentColor}};
              }
              body {
                line-height: 0;
              }
              img {
                display: block;
                width: 100%;
                height: 100%;
                object-fit: cover;
              }
            </style>
            </head>
            <body>
            <img src="cover.png" alt="Relego cover"/>
            </body>
            </html>
            """;
    }

    private static string BuildContentOpf(DateTimeOffset recapDate) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="2.0">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>Notes Recap ({recapDate:yyyy-MM-dd HH:mm})</dc:title>
            <dc:creator>Relego</dc:creator>
            <dc:subject>relego.io</dc:subject>
            <dc:identifier id="BookId">relego-recap-{recapDate:yyyyMMdd-HHmmss}</dc:identifier>
            <dc:language>en</dc:language>
            <meta name="cover" content="cover-image"/>
          </metadata>
          <manifest>
            <item id="cover-image" href="cover.png" media-type="image/png"/>
            <item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>
            <item id="highlights" href="highlights.xhtml" media-type="application/xhtml+xml"/>
            <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
          </manifest>
          <spine toc="ncx">
            <itemref idref="cover" linear="no"/>
            <itemref idref="highlights"/>
          </spine>
          <guide>
            <reference type="cover" title="Cover" href="cover.xhtml"/>
          </guide>
        </package>
        """;

    private static string BuildTocNcx() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
          <head><meta name="dtb:uid" content="relego-recap"/></head>
          <docTitle><text>Relego Recap</text></docTitle>
          <navMap>
            <navPoint id="navpoint-1" playOrder="1">
              <navLabel><text>Highlights</text></navLabel>
              <content src="highlights.xhtml"/>
            </navPoint>
          </navMap>
        </ncx>
        """;

    private static string BuildHighlightsXhtml(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string cadence)
    {
        var cadenceLabel = cadence.Equals("weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" : "Daily";
        var sb = new StringBuilder();
        sb.AppendLine("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>Highlights</title></head>
            <body>
            """);
        sb.AppendLine($"<h1>Relego {cadenceLabel} Recap ({recapDate:yyyy-MM-dd HH:mm})</h1>");
        sb.AppendLine("<ul>");

        foreach (var h in highlights)
        {
            sb.AppendLine($"<li><blockquote>{EscapeXml(h.Text)}</blockquote><p><em>{EscapeXml(h.BookTitle)}</em> by {EscapeXml(h.AuthorName)}</p></li>");
        }

        sb.AppendLine("</ul>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static byte[] ReadEmbeddedBinary(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static string ToHex(BrandRgb color) =>
        $"#{color.R:x2}{color.G:x2}{color.B:x2}";
}
