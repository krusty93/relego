using System.Net;
using System.Net.Http.Json;

namespace Relego.Tests.Api;

public sealed class RecapEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public RecapEndpointTests()
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
    public async Task PostRecaps_NoDeliveryDestination_Returns422()
    {
        // Default seeded user has no Kindle or inbox email configured.
        var response = await _client.PostAsync("/recaps", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No delivery destination configured", body);
    }

    [Fact]
    public async Task PostRecaps_KindleEmailConfigured_DoesNotReturn422()
    {
        // Set a Kindle email so the destination guard passes.
        await _client.PatchAsJsonAsync("/settings", new { kindleEmail = "test@kindle.com" });

        var response = await _client.PostAsync("/recaps", null);

        // 200 OK or 500 (recap may fail due to no highlights/SMTP in test) — but NOT 422.
        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostRecaps_InboxEmailConfigured_DoesNotReturn422()
    {
        // Set an inbox email (no Kindle) so the destination guard passes.
        await _client.PatchAsJsonAsync("/settings", new { deliveryEmail = "user@example.com" });

        var response = await _client.PostAsync("/recaps", null);

        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
