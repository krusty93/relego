using Relego.Server.Services;

namespace Relego.Tests.Services;

public sealed class HtmlEmailComposerTests
{
    private static readonly DateTimeOffset RecapDate = new(2026, 6, 8, 18, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<SelectionCandidate> SampleHighlights =
    [
        new SelectionCandidate(1, "This is the first highlight text.", "Book One", "Author A", 3, null, DateTimeOffset.UtcNow.AddDays(-5), 10),
        new SelectionCandidate(2, "This is the second highlight text.", "Book One", "Author A", 4, null, DateTimeOffset.UtcNow.AddDays(-3), 8),
        new SelectionCandidate(3, "Another book highlight.", "Book Two", "Author B", 5, null, DateTimeOffset.UtcNow.AddDays(-1), 12),
    ];

    [Fact]
    public void Compose_ReturnsHtmlAndPlainText()
    {
        var (htmlBody, plainTextBody) = HtmlEmailComposer.Compose(SampleHighlights, RecapDate);

        Assert.False(string.IsNullOrEmpty(htmlBody));
        Assert.False(string.IsNullOrEmpty(plainTextBody));
    }

    [Fact]
    public void Compose_HtmlBody_ContainsRequiredElements()
    {
        var (htmlBody, _) = HtmlEmailComposer.Compose(SampleHighlights, RecapDate);

        // Brand header
        Assert.Contains("Relego", htmlBody);

        // Recap date
        Assert.Contains("June 8, 2026", htmlBody);

        // Book titles
        Assert.Contains("Book One", htmlBody);
        Assert.Contains("Book Two", htmlBody);

        // Author names
        Assert.Contains("Author A", htmlBody);
        Assert.Contains("Author B", htmlBody);

        // Highlight text
        Assert.Contains("This is the first highlight text.", htmlBody);
        Assert.Contains("This is the second highlight text.", htmlBody);
        Assert.Contains("Another book highlight.", htmlBody);

        // Footer
        Assert.Contains("Sent by Relego", htmlBody);
        Assert.Contains("https://relego.app", htmlBody);

        // Email-safe: no rgba() - Outlook doesn't support it
        Assert.DoesNotContain("rgba(", htmlBody);

        // Email-safe: no border-radius - Outlook doesn't support it
        Assert.DoesNotContain("border-radius", htmlBody);

        // Email-safe: no letter-spacing - Outlook doesn't support it
        Assert.DoesNotContain("letter-spacing", htmlBody);

        // Should contain MSO conditionals for Outlook
        Assert.Contains("[if mso]", htmlBody);
    }

    [Fact]
    public void Compose_PlainTextBody_ContainsRequiredElements()
    {
        var (_, plainTextBody) = HtmlEmailComposer.Compose(SampleHighlights, RecapDate);

        // Book titles
        Assert.Contains("Book One", plainTextBody);
        Assert.Contains("Book Two", plainTextBody);

        // Author names
        Assert.Contains("Author A", plainTextBody);
        Assert.Contains("Author B", plainTextBody);

        // Highlight text
        Assert.Contains("This is the first highlight text.", plainTextBody);
        Assert.Contains("Another book highlight.", plainTextBody);
    }

    [Fact]
    public void Compose_EmptyHighlights_ProducesNoHighlightsMessage()
    {
        var (htmlBody, plainTextBody) = HtmlEmailComposer.Compose([], RecapDate);

        Assert.Contains("No highlights", htmlBody);
        Assert.Contains("No highlights", plainTextBody);
    }

    [Fact]
    public void Compose_UnicodeCharacters_ArePreserved()
    {
        var highlights = new List<SelectionCandidate>
        {
            new(1, "Unicode test: émoji 🔥, 中文, español, français", "Book Üñî", "Äuthör", 3, null, DateTimeOffset.UtcNow, 10),
        };

        var (htmlBody, plainTextBody) = HtmlEmailComposer.Compose(highlights, RecapDate);

        Assert.Contains("émoji 🔥", plainTextBody);
        Assert.Contains("中文", plainTextBody);
        Assert.Contains("español", plainTextBody);

        Assert.Contains("émoji 🔥", htmlBody);
        Assert.Contains("中文", htmlBody);
    }

    [Fact]
    public void Compose_MultipleBooks_AreGrouped()
    {
        var (htmlBody, _) = HtmlEmailComposer.Compose(SampleHighlights, RecapDate);

        // Books should appear in order
        var bookOneIndex = htmlBody.IndexOf("Book One");
        var bookTwoIndex = htmlBody.IndexOf("Book Two");
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

        var (htmlBody, plainTextBody) = HtmlEmailComposer.Compose(highlights, RecapDate);

        Assert.Contains("[...]", plainTextBody); // truncated marker
        Assert.DoesNotContain(new string('A', 2500), plainTextBody); // not full text

        Assert.Contains(new string('A', 2500), htmlBody); // full text in HTML
    }

    [Fact]
    public void Compose_NoMimeKitDependency()
    {
        // Verify the method returns raw strings, not MIME objects
        var result = HtmlEmailComposer.Compose(SampleHighlights, RecapDate);

        Assert.IsType<(string HtmlBody, string PlainTextBody)>(result);
    }
}
