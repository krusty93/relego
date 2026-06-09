using MimeKit;
using Relego.Server.Services;

namespace Relego.Tests.Services;

public sealed class HtmlEmailComposerTests
{
    private static readonly DateTimeOffset RecapDate = new(2026, 6, 8, 18, 0, 0, TimeSpan.Zero);
    private const string Cadence = "daily";
    private const string ToAddress = "user@example.com";
    private const string FromAddress = "relego@relego.app";

    private static readonly IReadOnlyList<SelectionCandidate> SampleHighlights =
    [
        new SelectionCandidate(1, "This is the first highlight text.", "Book One", "Author A", 3, null, DateTimeOffset.UtcNow.AddDays(-5), 10),
        new SelectionCandidate(2, "This is the second highlight text.", "Book One", "Author A", 4, null, DateTimeOffset.UtcNow.AddDays(-3), 8),
        new SelectionCandidate(3, "Another book highlight.", "Book Two", "Author B", 5, null, DateTimeOffset.UtcNow.AddDays(-1), 12),
    ];

    [Fact]
    public void Compose_ReturnsMultipartAlternative()
    {
        var message = HtmlEmailComposer.Compose(SampleHighlights, RecapDate, Cadence, ToAddress, FromAddress);

        Assert.NotNull(message);
        Assert.Equal("Your Relego Recap", message.Subject);
        Assert.Equal(ToAddress, message.To.Mailboxes.First().Address);
        Assert.Equal(FromAddress, message.From.Mailboxes.First().Address);

        Assert.IsType<MultipartAlternative>(message.Body);
        var body = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal(2, body.Count);
    }

    [Fact]
    public void Compose_HtmlPart_ContainsRequiredElements()
    {
        var message = HtmlEmailComposer.Compose(SampleHighlights, RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);

        // BodyBuilder puts text/plain first, text/html second (by MIME convention)
        var htmlPart = Assert.IsType<TextPart>(body[1]);

        Assert.Equal("text/html", htmlPart.ContentType.MimeType);
        var html = htmlPart.Text;

        // Brand header
        Assert.Contains("Relego", html);

        // Recap date
        Assert.Contains("June 8, 2026", html);

        // Book titles
        Assert.Contains("Book One", html);
        Assert.Contains("Book Two", html);

        // Author names
        Assert.Contains("Author A", html);
        Assert.Contains("Author B", html);

        // Highlight text
        Assert.Contains("This is the first highlight text.", html);
        Assert.Contains("This is the second highlight text.", html);
        Assert.Contains("Another book highlight.", html);

        // Footer
        Assert.Contains("Sent by Relego", html);
        Assert.Contains("https://relego.app", html);

        // Email-safe: no rgba() - Outlook doesn't support it
        Assert.DoesNotContain("rgba(", html);

        // Email-safe: no border-radius - Outlook doesn't support it
        Assert.DoesNotContain("border-radius", html);

        // Email-safe: no letter-spacing - Outlook doesn't support it
        Assert.DoesNotContain("letter-spacing", html);

        // Should contain MSO conditionals for Outlook
        Assert.Contains("[if mso]", html);
    }

    [Fact]
    public void Compose_PlainTextPart_ContainsRequiredElements()
    {
        var message = HtmlEmailComposer.Compose(SampleHighlights, RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);

        // BodyBuilder puts text/plain first
        var plainPart = Assert.IsType<TextPart>(body[0]);

        Assert.Equal("text/plain", plainPart.ContentType.MimeType);
        var plain = plainPart.Text;

        // Book titles
        Assert.Contains("Book One", plain);
        Assert.Contains("Book Two", plain);

        // Author names
        Assert.Contains("Author A", plain);
        Assert.Contains("Author B", plain);

        // Highlight text
        Assert.Contains("This is the first highlight text.", plain);
        Assert.Contains("Another book highlight.", plain);
    }

    [Fact]
    public void Compose_EmptyHighlights_ProducesNoHighlightsMessage()
    {
        var message = HtmlEmailComposer.Compose([], RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);

        var htmlPart = Assert.IsType<TextPart>(body[1]);
        Assert.Contains("No highlights", htmlPart.Text);

        var plainPart = Assert.IsType<TextPart>(body[0]);
        Assert.Contains("No highlights", plainPart.Text);
    }

    [Fact]
    public void Compose_UnicodeCharacters_ArePreserved()
    {
        var highlights = new List<SelectionCandidate>
        {
            new(1, "Unicode test: émoji 🔥, 中文, español, français", "Book Üñî", "Äuthör", 3, null, DateTimeOffset.UtcNow, 10),
        };

        var message = HtmlEmailComposer.Compose(highlights, RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);

        var plainPart = Assert.IsType<TextPart>(body[0]);
        Assert.Contains("émoji 🔥", plainPart.Text);
        Assert.Contains("中文", plainPart.Text);
        Assert.Contains("español", plainPart.Text);

        var htmlPart = Assert.IsType<TextPart>(body[1]);
        Assert.Contains("émoji 🔥", htmlPart.Text);
        Assert.Contains("中文", htmlPart.Text);
    }

    [Fact]
    public void Compose_MultipleBooks_AreGrouped()
    {
        var message = HtmlEmailComposer.Compose(SampleHighlights, RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);
        var htmlPart = Assert.IsType<TextPart>(body[1]);
        var html = htmlPart.Text;

        // Books should appear in order
        var bookOneIndex = html.IndexOf("Book One");
        var bookTwoIndex = html.IndexOf("Book Two");
        Assert.True(bookOneIndex < bookTwoIndex, "Book One should appear before Book Two");
    }

    [Fact]
    public void Compose_VeryLongHighlight_TruncatedInPlainText_FullInHtml()
    {
        var longText = new string('A', 2500);
        var highlights = new List<SelectionCandidate>
        {
            new(1, longText, "Book", "Author", 3, null, DateTimeOffset.UtcNow, 10),
        };

        var message = HtmlEmailComposer.Compose(highlights, RecapDate, Cadence, ToAddress, FromAddress);
        var body = Assert.IsType<MultipartAlternative>(message.Body);

        var plainPart = Assert.IsType<TextPart>(body[0]);
        Assert.Contains("[...]", plainPart.Text); // truncated marker
        Assert.DoesNotContain(new string('A', 2500), plainPart.Text); // not full text

        var htmlPart = Assert.IsType<TextPart>(body[1]);
        Assert.Contains(new string('A', 2500), htmlPart.Text); // full text in HTML
    }

    [Fact]
    public void Compose_NoAttachmentParts()
    {
        var message = HtmlEmailComposer.Compose(SampleHighlights, RecapDate, Cadence, ToAddress, FromAddress);

        // The top-level body should be MultipartAlternative, not MultipartMixed
        Assert.IsType<MultipartAlternative>(message.Body);

        // No attachment parts
        Assert.Empty(message.Attachments);
    }
}
