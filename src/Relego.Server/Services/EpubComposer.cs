using System.IO.Compression;
using System.Text;

namespace Relego.Server.Services;

public static class EpubComposer
{
    private const string CoverSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="Relego favicon">
        	<rect width="64" height="64" rx="16" fill="#16110f" />
        	<path fill="#f4ecdf" d="M27.84 51.77Q23.30 51.77 17.56 51.92L17.56 51.92L14.16 52.00L14.16 46.71L18.31 46.33Q20.58 46.10 21.91 45.46Q23.23 44.82 23.83 43.46Q24.44 42.09 24.44 39.75L24.44 39.75L24.44 32.64L31.24 32.64L31.24 39.75Q31.24 42.09 31.92 43.42Q32.60 44.74 34.12 45.42Q35.63 46.10 38.28 46.33L38.28 46.33L42.43 46.71L42.43 52.00L38.73 51.92Q32.68 51.77 27.84 51.77L27.84 51.77L27.84 51.77M24.44 25.38Q24.44 23.04 23.83 21.68Q23.23 20.32 21.91 19.67Q20.58 19.03 18.31 18.81L18.31 18.81L14.16 18.43L14.16 13.13Q16.88 13.29 19.30 13.29L19.30 13.29Q26.56 13.29 31.24 12.23L31.24 12.23L31.24 13.13L31.24 32.64L24.44 32.64L24.44 25.38M44.85 26.67Q43.12 26.67 42.06 25.72Q41.00 24.78 41.00 23.34L41.00 23.34Q41.00 22.66 41.26 21.98Q41.53 21.30 41.83 20.70L41.83 20.70Q42.06 20.32 42.25 19.90Q42.43 19.49 42.43 19.11L42.43 19.11Q42.43 18.50 41.87 18.12Q41.30 17.75 40.32 17.75L40.32 17.75Q38.65 17.75 36.57 18.92Q34.50 20.09 32.91 21.91Q31.32 23.72 30.94 25.61L30.94 25.61L30.49 20.09Q32.76 16.08 35.67 14.04Q38.58 12.00 41.98 12.00L41.98 12.00Q45.53 12.00 47.69 14.27Q49.84 16.54 49.84 20.24L49.84 20.24Q49.84 23.12 48.48 24.89Q47.12 26.67 44.85 26.67L44.85 26.67" />
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
