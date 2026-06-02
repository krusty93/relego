using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class HighlightEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public HighlightEndpointTests()
    {
        _factory = new RelegoTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetHighlights_Empty_ReturnsTotalZero()
    {
        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHighlights_SeededData_ReturnsAllItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task GetHighlights_ItemsHaveExpectedFields()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        var item = result.Items.First();
        Assert.True(item.Id > 0);
        Assert.True(item.BookId > 0);
        Assert.True(item.AuthorId > 0);
        Assert.NotEmpty(item.Text);
        Assert.NotEmpty(item.BookTitle);
        Assert.NotEmpty(item.AuthorName);
    }

    [Fact]
    public async Task DeleteHighlight_RemovesStoredHighlight()
    {
        await SeedHighlightsAsync();

        var beforeDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        var highlightId = beforeDelete!.Items[0].Id;

        var deleteResponse = await _client.DeleteAsync($"/highlights/{highlightId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        Assert.NotNull(afterDelete);
        Assert.Equal(beforeDelete.Total - 1, afterDelete.Total);
        Assert.DoesNotContain(afterDelete.Items, item => item.Id == highlightId);
    }

    [Fact]
    public async Task DeleteHighlight_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync("/highlights/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHighlights_Pagination_ReturnsCorrectPage()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?page=2&pageSize=2");

        Assert.NotNull(result);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetHighlights_FilterByText_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=courage");

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Contains("courage", result.Items[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHighlights_FilterByBookTitle_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=Book+Alpha");

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("Book Alpha", item.BookTitle));
    }

    [Fact]
    public async Task GetHighlights_FilterByAuthor_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=Author+Beta");

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.All(result.Items, item => Assert.Equal("Author Beta", item.AuthorName));
    }

    [Fact]
    public async Task GetHighlights_FilterNoMatch_ReturnsEmptyList()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=nomatch_xyz");

        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHighlights_PageZero_Returns422()
    {
        var response = await _client.GetAsync("/highlights?page=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("page", body);
    }

    [Fact]
    public async Task GetHighlights_PageSizeZero_Returns422()
    {
        var response = await _client.GetAsync("/highlights?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pageSize", body);
    }

    [Fact]
    public async Task GetHighlights_PageSizeOver200_Returns422()
    {
        var response = await _client.GetAsync("/highlights?pageSize=201");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pageSize", body);
    }

    private async Task SeedHighlightsAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Alpha",
                    Author = "Author Alpha",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "The secret of getting ahead is getting started." },
                        new SyncHighlightRequest { Text = "Courage is resistance to fear." }
                    ]
                },
                new SyncBookRequest
                {
                    Title = "Book Beta",
                    Author = "Author Beta",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "In the middle of every difficulty lies opportunity." }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }
}
