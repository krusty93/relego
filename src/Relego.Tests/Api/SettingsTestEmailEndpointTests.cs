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
        await _client.PutAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

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
        Assert.Contains("kindleEmail", body);
    }

    [Fact]
    public async Task PostTestEmail_WhenSmtpFails_Returns502()
    {
        await _client.PutAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrow = true;

        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SMTP delivery failed", body);
    }

    [Fact]
    public async Task PostTestEmail_WhenUnexpectedErrorOccurs_Returns500()
    {
        await _client.PutAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrowUnexpected = true;

        var response = await _client.PostAsync("/settings/test-email", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private sealed class FakeMailDeliveryService : IMailDeliveryService
    {
        public string? LastTestEmailAddress { get; private set; }
        public bool ShouldThrow { get; set; }
        public bool ShouldThrowUnexpected { get; set; }

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
    }
}
