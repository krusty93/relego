using System.Globalization;
using System.Text;

namespace Relego.Server.Services;

public static class HtmlEmailComposer
{
    private const int PlainTextMaxLength = 2000;
    private const string AccentHex = "#b56b39";

    public static (string HtmlBody, string PlainTextBody) Compose(
        IReadOnlyList<SelectionCandidate> highlights,
        DateTimeOffset recapDate)
    {
        var formattedDate = recapDate.ToLocalTime().ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

        string htmlBody = BuildHtmlBody(highlights, formattedDate);
        string plainTextBody = BuildPlainTextBody(highlights, formattedDate);

        return (htmlBody, plainTextBody);
    }

    private static string BuildHtmlBody(IReadOnlyList<SelectionCandidate> highlights, string formattedDate)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"en\" xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">");
        sb.Append("<head>");
        sb.Append("<meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        sb.Append("<title>Relego Recap</title>");
        sb.Append("<!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:AllowPNG/><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->");
        sb.Append("<!--[if mso]><style type=\"text/css\">table { border-collapse: collapse; }</style><![endif]-->");
        sb.Append("</head>");
        sb.Append("<body style=\"margin:0;padding:0;background-color:#f7f1e8;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;\">");

        // Outlook conditional wrapper for centered layout
        sb.Append("<!--[if mso]><table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"600\" align=\"center\"><tr><td><![endif]-->");

        // Outer table for centering
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background-color:#ffffff;\" align=\"center\">");
        sb.Append("<tbody>");

        // Header
        sb.Append("<tr><td style=\"padding:30px 24px;background-color:");
        sb.Append(AccentHex);
        sb.Append(";\">");
        sb.Append("<h1 style=\"margin:0;color:#ffffff;font-size:28px;font-weight:700;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">Relego</h1>");
        // Use solid #e6d5c8 as rgba(255,255,255,0.85) approximation for Outlook compat
        sb.Append("<p style=\"margin:4px 0 0;color:#ffffff;font-size:14px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">Your Kindle Highlight Recap</p>");
        sb.Append("</td></tr>");

        // Spacing row
        sb.Append("<tr><td style=\"padding:8px 24px;\">&nbsp;</td></tr>");

        // Date
        sb.Append("<tr><td style=\"padding:0 24px 8px;\">");
        sb.Append($"<p style=\"margin:0;color:#5b4f47;font-size:14px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">{formattedDate}</p>");
        sb.Append("</td></tr>");

        if (highlights.Count == 0)
        {
            sb.Append("<tr><td style=\"padding:24px;\">");
            sb.Append("<p style=\"margin:0;color:#5b4f47;font-size:16px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">No highlights were selected for this recap period.</p>");
            sb.Append("</td></tr>");
        }
        else
        {
            // Group highlights by book
            var grouped = highlights
                .GroupBy(h => (h.BookTitle, h.AuthorName))
                .ToList();

            foreach (var group in grouped)
            {
                // Book separator for visual grouping
                sb.Append("<tr><td style=\"padding:0 24px;\"><table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\"><tbody><tr><td style=\"border-top:1px solid #dcd6ce;\"></td></tr></tbody></table></td></tr>");
                sb.Append("<tr><td style=\"padding:16px 24px 8px;\">");
                sb.Append($"<h2 style=\"margin:0;color:#171311;font-size:18px;font-weight:600;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">{EscapeHtml(group.Key.BookTitle)}</h2>");
                sb.Append($"<p style=\"margin:2px 0 0;color:#5b4f47;font-size:13px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">{EscapeHtml(group.Key.AuthorName)}</p>");
                sb.Append("</td></tr>");

                foreach (var highlight in group)
                {
                    sb.Append("<tr><td style=\"padding:4px 24px 4px 28px;\">");
                    sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\">");
                    sb.Append("<tbody><tr>");
                    sb.Append($"<td width=\"3\" style=\"width:3px;background-color:{AccentHex};\"></td>");
                    sb.Append("<td style=\"padding:0 0 0 12px;\">");
                    sb.Append($"<p style=\"margin:0;color:#171311;font-size:15px;line-height:1.5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">{EscapeHtml(highlight.Text)}</p>");
                    sb.Append("</td></tr></tbody></table>");
                    sb.Append("</td></tr>");
                }
            }
        }

        // Spacing row before footer
        sb.Append("<tr><td style=\"padding:12px 24px;\">&nbsp;</td></tr>");

        // Footer
        sb.Append("<tr><td style=\"padding:24px;border-top:1px solid #dcd6ce;\">");
        sb.Append("<p style=\"margin:0;color:#5b4f47;font-size:12px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">Sent by Relego &mdash; <a href=\"https://relego.app\" style=\"color:#b56b39;text-decoration:underline;\">relego.app</a></p>");
        sb.Append("</td></tr>");

        sb.Append("</tbody>");
        sb.Append("</table>");

        // Close Outlook conditional wrapper
        sb.Append("<!--[if mso]></td></tr></table><![endif]-->");

        sb.Append("</body></html>");

        return sb.ToString();
    }

    private static string BuildPlainTextBody(IReadOnlyList<SelectionCandidate> highlights, string formattedDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RELEGO RECAP");
        sb.AppendLine(new string('=', 40));
        sb.AppendLine();
        sb.AppendLine(formattedDate);
        sb.AppendLine();

        if (highlights.Count == 0)
        {
            sb.AppendLine("No highlights were selected for this recap period.");
            return sb.ToString();
        }

        var grouped = highlights
            .GroupBy(h => (h.BookTitle, h.AuthorName))
            .ToList();

        foreach (var group in grouped)
        {
            sb.AppendLine(group.Key.BookTitle);
            sb.AppendLine(new string('=', group.Key.BookTitle.Length));
            sb.AppendLine($"by {group.Key.AuthorName}");
            sb.AppendLine();

            foreach (var highlight in group)
            {
                var text = highlight.Text;
                if (text.Length > PlainTextMaxLength)
                {
                    text = text[..PlainTextMaxLength] + "[...]";
                }
                sb.AppendLine($"> {text}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine("Sent by Relego (https://relego.app)");

        return sb.ToString();
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
