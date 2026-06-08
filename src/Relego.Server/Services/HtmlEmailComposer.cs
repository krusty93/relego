using System.Globalization;
using System.Text;
using MimeKit;

namespace Relego.Server.Services;

public static class HtmlEmailComposer
{
    private const int PlainTextMaxLength = 2000;
    private const string AccentHex = "#b56b39";

    public static MimeMessage Compose(
        IReadOnlyList<SelectionCandidate> highlights,
        DateTimeOffset recapDate,
        string _cadence,
        string toAddress,
        string fromAddress)
    {
        _ = _cadence; // unused parameter — signature matches EpubComposer.Compose

        var bodyBuilder = new BodyBuilder();

        var formattedDate = recapDate.ToLocalTime().ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

        // Build HTML part
        bodyBuilder.HtmlBody = BuildHtmlBody(highlights, formattedDate);

        // Build plain-text part
        bodyBuilder.TextBody = BuildPlainTextBody(highlights, formattedDate);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Relego", fromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = "Your Relego Recap";
        message.Body = bodyBuilder.ToMessageBody();

        return message;
    }

    private static string BuildHtmlBody(IReadOnlyList<SelectionCandidate> highlights, string formattedDate)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"en\">");
        sb.Append("<head>");
        sb.Append("<meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append("</head>");
        sb.Append($"<body style=\"margin:0;padding:0;background-color:#f7f1e8;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">");

        // Outer table for centering
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;background-color:#f7f1e8;\">");
        sb.Append("<tr><td align=\"center\" style=\"padding:20px 10px;\">");

        // Main container
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;width:100%;background-color:#ffffff;border-radius:8px;overflow:hidden;\">");

        // Header
        sb.Append("<tr><td style=\"padding:30px 24px;background-color:")
          .Append(AccentHex)
          .Append(";\">");
        sb.Append("<h1 style=\"margin:0;color:#ffffff;font-size:28px;font-weight:700;\">Relego</h1>");
        sb.Append("<p style=\"margin:4px 0 0;color:rgba(255,255,255,0.85);font-size:14px;\">Your Kindle Highlight Recap</p>");
        sb.Append("</td></tr>");

        // Date
        sb.Append("<tr><td style=\"padding:20px 24px 8px;\">");
        sb.Append($"<p style=\"margin:0;color:#5b4f47;font-size:14px;text-transform:uppercase;letter-spacing:0.5px;\">{formattedDate}</p>");
        sb.Append("</td></tr>");

        if (highlights.Count == 0)
        {
            sb.Append("<tr><td style=\"padding:24px;\">");
            sb.Append("<p style=\"margin:0;color:#5b4f47;font-size:16px;\">No highlights were selected for this recap period.</p>");
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
                sb.Append("<tr><td style=\"padding:16px 24px 8px;\">");
                sb.Append($"<h2 style=\"margin:0;color:#171311;font-size:18px;font-weight:600;\">{EscapeHtml(group.Key.BookTitle)}</h2>");
                sb.Append($"<p style=\"margin:2px 0 0;color:#5b4f47;font-size:13px;\">{EscapeHtml(group.Key.AuthorName)}</p>");
                sb.Append("</td></tr>");

                foreach (var highlight in group)
                {
                    sb.Append("<tr><td style=\"padding:4px 24px 4px 28px;\">");
                    sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;\">");
                    sb.Append("<tr>");
                    sb.Append($"<td style=\"width:3px;background-color:{AccentHex};border-radius:2px;\"></td>");
                    sb.Append("<td style=\"padding:0 0 0 12px;\">");
                    sb.Append($"<p style=\"margin:0;color:#171311;font-size:15px;line-height:1.5;\">{EscapeHtml(highlight.Text)}</p>");
                    sb.Append("</td></tr></table>");
                    sb.Append("</td></tr>");
                }
            }
        }

        // Footer
        sb.Append("<tr><td style=\"padding:24px;border-top:1px solid #dcd6ce;\">");
        sb.Append("<p style=\"margin:0;color:#5b4f47;font-size:12px;\">Sent by Relego &mdash; <a href=\"https://relego.app\" style=\"color:#b56b39;text-decoration:underline;\">relego.app</a></p>");
        sb.Append("</td></tr>");

        sb.Append("</table>");
        sb.Append("</td></tr></table>");
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
