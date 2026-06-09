using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Server.Services;

namespace Relego.Tests.Api;

public sealed class SettingsTestEmailEndpointTests : IDisposable
{
    private readonly FakeMailDeliveryService _fakeMail = new();
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public SettingsTestEmailEndpointTests()
    {
        _factory = new RelegoTestApplicationFactory(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMailDeliveryService));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddSingleton<IMailDeliveryService>(_fakeMail);
            });
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task PostTestEmail_WithKindleEmail_SendsSuccessfully()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@kindle.com", _fakeMail.LastTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_WithoutKindleEmail_Returns422()
    {
        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("channel", body);
        Assert.Contains("No delivery email configured", body);
    }

    [Fact]
    public async Task PostTestEmail_WhenSmtpFails_Returns502()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrow = true;

        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SMTP delivery failed", body);
    }

    [Fact]
    public async Task PostTestEmail_WhenUnexpectedErrorOccurs_Returns500()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrowUnexpected = true;

        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task PostTestEmail_DeliveryChannel_SendsSuccessfully()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "delivery" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@example.com", _fakeMail.LastDeliveryTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_DeliveryChannel_WhenNotConfigured_Returns422()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "delivery" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostTestEmail_InvalidChannel_Returns422()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "invalid" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("channel", body);
    }

    [Fact]
    public async Task PostTestEmail_BothChannels_SendsToBoth()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "user@kindle.com",
            DeliveryEmail = "user@example.com"
        });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "both" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@kindle.com", _fakeMail.LastTestEmailAddress);
        Assert.Equal("user@example.com", _fakeMail.LastDeliveryTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_NoChannelAutoDetect_WhenBothConfigured_SendsToBoth()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "user@kindle.com",
            DeliveryEmail = "user@example.com"
        });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@kindle.com", _fakeMail.LastTestEmailAddress);
        Assert.Equal("user@example.com", _fakeMail.LastDeliveryTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_NoChannelAutoDetect_WhenOnlyDelivery_SendsToDelivery()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            DeliveryEmail = "user@example.com"
        });

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@example.com", _fakeMail.LastDeliveryTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_NoBody_BackwardCompatible_SendsToKindle()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        // No body — same as existing call pattern
        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@kindle.com", _fakeMail.LastTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_BothChannels_KindleFails_DeliveryStillSucceeds()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "user@kindle.com",
            DeliveryEmail = "user@example.com"
        });
        _fakeMail.ShouldThrow = true; // Kindle fails

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "both" });

        // "both" returns 502 when both fail, but 200 when at least one succeeds
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@example.com", _fakeMail.LastDeliveryTestEmailAddress);
    }

    [Fact]
    public async Task PostTestEmail_BothChannels_BothFail_Returns502()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "user@kindle.com",
            DeliveryEmail = "user@example.com"
        });
        _fakeMail.ShouldThrow = true;
        _fakeMail.ShouldThrowDelivery = true;

        var response = await _client.PostAsJsonAsync("/settings/test-email",
            new TestEmailRequest { Channel = "both" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private sealed class FakeMailDeliveryService : IMailDeliveryService
    {
        public string? LastTestEmailAddress { get; private set; }
        public string? LastDeliveryTestEmailAddress { get; private set; }
        public bool ShouldThrow { get; set; }
        public bool ShouldThrowUnexpected { get; set; }
        public bool ShouldThrowDelivery { get; set; }

        public Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowUnexpected)
                throw new InvalidOperationException("Unexpected configuration error.");

            if (ShouldThrow)
                throw new System.Net.Sockets.SocketException(10061);

            LastTestEmailAddress = toAddress;
            return Task.CompletedTask;
        }

        public Task SendHtmlRecapAsync(MimeKit.MimeMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendDeliveryTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowDelivery)
                throw new System.Net.Sockets.SocketException(10061);

            LastDeliveryTestEmailAddress = toAddress;
            return Task.CompletedTask;
        }
    }
}
