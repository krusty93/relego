using Relego.Server.Sync;

namespace Relego.Tests.Services;

public sealed class KindleCloudProviderTests
{
    private static readonly KindleCloudProvider Provider = new();

    [Theory]
    [InlineData("us", "read.amazon.com")]
    [InlineData("ca", "read.amazon.ca")]
    [InlineData("uk", "read.amazon.co.uk")]
    [InlineData("de", "lesen.amazon.de")]
    [InlineData("fr", "lire.amazon.fr")]
    [InlineData("it", "leggi.amazon.it")]
    [InlineData("es", "leer.amazon.es")]
    [InlineData("br", "ler.amazon.com.br")]
    public void TryResolveRegion_ResolvesAllowlistedRegion(string regionId, string expectedHost)
    {
        var resolved = Provider.Profile.TryResolveRegion(regionId, out var region);

        Assert.True(resolved);
        Assert.NotNull(region);
        Assert.Equal(regionId, region!.Id);
        Assert.Equal(expectedHost, region.NotebookHost);
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("cn")]
    [InlineData("dk")]
    [InlineData("ie")]
    [InlineData("pl")]
    [InlineData("read.amazon.com")]
    [InlineData("https://read.amazon.com")]
    [InlineData("us/uk")]
    [InlineData("read.amazon.co.uk")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryResolveRegion_RejectsUnsupportedOrHostShapedInput(string? regionId)
    {
        var resolved = Provider.Profile.TryResolveRegion(regionId, out var region);

        Assert.False(resolved);
        Assert.Null(region);
    }

    [Fact]
    public void Profile_ExposesExactlyTheEightAllowlistedRegions()
    {
        Assert.Equal(8, Provider.Profile.Regions.Count);
        Assert.DoesNotContain(Provider.Profile.Regions, region => region.Id is "jp" or "cn" or "dk" or "ie" or "pl");
    }

    [Fact]
    public void FrequencyPolicy_ExposesExactlyTenValuesAndDefaultOfSixHours()
    {
        Assert.Equal(10, Provider.Frequency.AllowedMinutes.Count);
        Assert.Equal(new[] { 15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440 }, Provider.Frequency.AllowedMinutes.ToArray());
        Assert.Equal(360, Provider.Frequency.Default);
        Assert.True(Provider.Frequency.IsAllowed(15));
        Assert.True(Provider.Frequency.IsAllowed(1440));
        Assert.False(Provider.Frequency.IsAllowed(11));
        Assert.False(Provider.Frequency.IsAllowed(0));
    }

    [Fact]
    public void DescriptorAndCapabilities_AreDeclared()
    {
        Assert.Equal("kindle-cloud", Provider.Descriptor.Id);
        Assert.Equal("Kindle cloud", Provider.Descriptor.DisplayName);
        Assert.Equal(45, Provider.InactivityReminderDays);
        Assert.Equal("1", Provider.Disclosure.Version);
        Assert.True(Provider.Capabilities.HasFlag(CloudSyncCapabilities.PushIngest));
        Assert.True(Provider.Capabilities.HasFlag(CloudSyncCapabilities.FullResync));
        Assert.True(Provider.Capabilities.HasFlag(CloudSyncCapabilities.ProgressReporting));
    }

    [Fact]
    public void Normalize_AcceptsValidBatch_AndTrimsValues()
    {
        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books =
            [
                new CloudSyncBook
                {
                    ExternalKey = "B00EXAMPLE",
                    Title = "  A Book  ",
                    Author = "  An Author  ",
                    Highlights =
                    [
                        new CloudSyncHighlight { ExternalId = "h1", Text = "  A highlight  " }
                    ]
                }
            ]
        };

        var result = Provider.Normalize(batch);

        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedBatch);
        var book = Assert.Single(result.NormalizedBatch!.Books);
        Assert.Equal("A Book", book.Title);
        Assert.Equal("An Author", book.Author);
        var highlight = Assert.Single(book.Highlights);
        Assert.Equal("A highlight", highlight.Text);

        var request = Provider.ToSyncRequest(result);
        var requestBook = Assert.Single(request.Books);
        Assert.Equal("A Book", requestBook.Title);
        Assert.Equal("An Author", requestBook.Author);
        Assert.Equal("A highlight", Assert.Single(requestBook.Highlights).Text);
    }

    [Fact]
    public void Normalize_PreservesUnknownOptionalValuesAsNull()
    {
        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books =
            [
                new CloudSyncBook
                {
                    Title = "A Book",
                    Highlights = [new CloudSyncHighlight { Text = "A highlight" }]
                }
            ]
        };

        var result = Provider.Normalize(batch);

        Assert.True(result.IsValid);
        var book = Assert.Single(result.NormalizedBatch!.Books);
        Assert.Null(book.Author);
        Assert.Null(book.ExternalKey);
        var highlight = Assert.Single(book.Highlights);
        Assert.Null(highlight.ExternalId);
        Assert.Null(highlight.Note);
        Assert.Null(highlight.Location);
        Assert.Null(highlight.Color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsEmptyHighlightText(string text)
    {
        var result = Provider.Normalize(BuildBatchWithHighlight(text));

        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedBatch);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Normalize_RejectsOversizeHighlightText()
    {
        var result = Provider.Normalize(BuildBatchWithHighlight(new string('a', 20_001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_AcceptsHighlightTextAtMaximumLength()
    {
        var result = Provider.Normalize(BuildBatchWithHighlight(new string('a', 20_000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Normalize_RejectsOversizeTitle()
    {
        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books =
            [
                new CloudSyncBook
                {
                    Title = new string('a', 1_001),
                    Highlights = [new CloudSyncHighlight { Text = "A highlight" }]
                }
            ]
        };

        var result = Provider.Normalize(batch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_RejectsOversizeAuthor()
    {
        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books =
            [
                new CloudSyncBook
                {
                    Title = "A Book",
                    Author = new string('a', 501),
                    Highlights = [new CloudSyncHighlight { Text = "A highlight" }]
                }
            ]
        };

        var result = Provider.Normalize(batch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("author", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_RejectsNegativeBatchIndex()
    {
        var batch = new CloudSyncBatch
        {
            BatchIndex = -1,
            Books =
            [
                new CloudSyncBook
                {
                    Title = "A Book",
                    Highlights = [new CloudSyncHighlight { Text = "A highlight" }]
                }
            ]
        };

        var result = Provider.Normalize(batch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("batchIndex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_RejectsMoreThanThousandHighlights()
    {
        var highlights = Enumerable.Range(0, 1_001)
            .Select(index => new CloudSyncHighlight { Text = $"Highlight {index}" })
            .ToList();

        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books = [new CloudSyncBook { Title = "A Book", Highlights = highlights }]
        };

        var result = Provider.Normalize(batch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("1000", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalize_AcceptsExactlyThousandHighlights()
    {
        var highlights = Enumerable.Range(0, 1_000)
            .Select(index => new CloudSyncHighlight { Text = $"Highlight {index}" })
            .ToList();

        var batch = new CloudSyncBatch
        {
            BatchIndex = 0,
            Books = [new CloudSyncBook { Title = "A Book", Highlights = highlights }]
        };

        var result = Provider.Normalize(batch);

        Assert.True(result.IsValid);
    }

    private static CloudSyncBatch BuildBatchWithHighlight(string text) =>
        new()
        {
            BatchIndex = 0,
            Books =
            [
                new CloudSyncBook
                {
                    Title = "A Book",
                    Author = "An Author",
                    Highlights = [new CloudSyncHighlight { Text = text }]
                }
            ]
        };
}
