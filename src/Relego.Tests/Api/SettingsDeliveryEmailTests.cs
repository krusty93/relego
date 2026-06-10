using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class SettingsDeliveryEmailTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public SettingsDeliveryEmailTests()
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
    public async Task GetSettings_DeliveryEmail_NotSet_ReturnsNull()
    {
        var response = await _client.GetAsync("/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(result);
        Assert.Null(result.DeliveryEmail);
    }

    [Fact]
    public async Task PatchSettings_DeliveryEmail_Valid_IsPersisted()
    {
        var patchResponse = await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var getResponse = await _client.GetAsync("/settings");
        var result = await getResponse.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(result);
        Assert.Equal("user@example.com", result.DeliveryEmail);
    }

    [Fact]
    public async Task PatchSettings_DeliveryEmail_EmptyString_ClearsField()
    {
        // First set a value
        await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        // Clear with empty string
        var clearResponse = await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "" });

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var getResponse = await _client.GetAsync("/settings");
        var result = await getResponse.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(result);
        Assert.Null(result.DeliveryEmail);
    }

    [Fact]
    public async Task PatchSettings_DeliveryEmail_Null_ClearsExistingValue()
    {
        // First set a value
        await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        // Send patch with null (field absent) — current behaviour: clears delivery email
        var nullResponse = await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { Schedule = "weekly" });

        Assert.Equal(HttpStatusCode.OK, nullResponse.StatusCode);

        var getResponse = await _client.GetAsync("/settings");
        var result = await getResponse.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(result);
        Assert.Null(result.DeliveryEmail);
    }

    [Fact]
    public async Task PatchSettings_DeliveryEmail_Invalid_Returns422()
    {
        var response = await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "not-an-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("deliveryEmail", body);
        Assert.Contains("Invalid email format", body);
    }

    [Fact]
    public async Task PatchSettings_KindleEmail_EmptyString_Returns422()
    {
        // First set a value
        await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        // Empty string is rejected as an invalid email
        var clearResponse = await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { KindleEmail = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, clearResponse.StatusCode);

        // Original value is unchanged
        var getResponse = await _client.GetAsync("/settings");
        var result = await getResponse.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(result);
        Assert.Equal("user@kindle.com", result.KindleEmail);
    }

    [Fact]
    public async Task GetStatus_DeliveryEmailConfigured_True_WhenSet()
    {
        await _client.PatchAsJsonAsync("/settings",
            new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        var response = await _client.GetAsync("/status");
        var result = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.NotNull(result);
        Assert.True(result.DeliveryEmailConfigured);
    }

    [Fact]
    public async Task GetStatus_DeliveryEmailConfigured_False_WhenNotSet()
    {
        var response = await _client.GetAsync("/status");
        var result = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.NotNull(result);
        Assert.False(result.DeliveryEmailConfigured);
    }
}
