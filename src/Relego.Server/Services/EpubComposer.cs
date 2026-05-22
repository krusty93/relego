using System.IO.Compression;
using System.Text;

namespace Relego.Server.Services;

public static class EpubComposer
{
    private const string CoverSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="Relego favicon">
        	<rect width="64" height="64" rx="16" fill="#16110f" />
        	<circle cx="32" cy="32" r="22" fill="#f4ecdf" opacity="0.12" />
        	<path fill="#f4ecdf" d="M21 18h12c7.18 0 11.5 3.79 11.5 10.12 0 5.33-2.92 8.84-7.88 9.73L45 46h-7.76l-7.73-7.58H28V46h-7V18Zm7 14.66h4.31c3.56 0 5.46-1.46 5.46-4.16 0-2.72-1.9-4.13-5.46-4.13H28v8.29Z" />
        </svg>
        """;

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
            AddEntry(archive, "OEBPS/cover.svg", CoverSvg);
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

    private static string BuildContainerXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static string BuildCoverXhtml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
        <html xmlns="http://www.w3.org/1999/xhtml">
        <head><title>Cover</title></head>
        <body>
        <div style="text-align:center;">
          <img src="cover.svg" alt="Relego" style="max-width:100%;max-height:100%;"/>
        </div>
        </body>
        </html>
        """;

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
            <item id="cover-image" href="cover.svg" media-type="image/svg+xml"/>
            <item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>
            <item id="highlights" href="highlights.xhtml" media-type="application/xhtml+xml"/>
            <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
          </manifest>
          <spine toc="ncx">
            <itemref idref="cover" linear="no"/>
            <itemref idref="highlights"/>
          </spine>
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
}
