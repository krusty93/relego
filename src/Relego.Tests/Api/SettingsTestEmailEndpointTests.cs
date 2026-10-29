using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Server.Infrastructure.Smtp;
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

    // ── test-kindle-email ──────────────────────────────────────────

    [Fact]
    public async Task TestKindleEmail_WithKindleConfigured_SendsSuccessfully()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });

        var response = await _client.PostAsync("/settings/test-kindle-email", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("user@kindle.com", _fakeMail.SentAddresses);
    }

    [Fact]
    public async Task TestKindleEmail_WithoutKindle_Returns422()
    {
        var response = await _client.PostAsync("/settings/test-kindle-email", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("kindleEmail", body);
    }

    [Fact]
    public async Task TestKindleEmail_SmtpFails_Returns502()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrow = true;

        var response = await _client.PostAsync("/settings/test-kindle-email", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task TestKindleEmail_UnexpectedError_Returns500()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { KindleEmail = "user@kindle.com" });
        _fakeMail.ShouldThrowUnexpected = true;

        var response = await _client.PostAsync("/settings/test-kindle-email", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── test-recap-email ───────────────────────────────────────────

    [Fact]
    public async Task TestRecapEmail_WithDeliveryConfigured_SendsSuccessfully()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });

        var response = await _client.PostAsync("/settings/test-recap-email", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("user@example.com", _fakeMail.SentAddresses);
    }

    [Fact]
    public async Task TestRecapEmail_WithoutDelivery_Returns422()
    {
        var response = await _client.PostAsync("/settings/test-recap-email", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("deliveryEmail", body);
    }

    [Fact]
    public async Task TestRecapEmail_SmtpFails_Returns502()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });
        _fakeMail.ShouldThrow = true;

        var response = await _client.PostAsync("/settings/test-recap-email", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task TestRecapEmail_UnexpectedError_Returns500()
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { DeliveryEmail = "user@example.com" });
        _fakeMail.ShouldThrowUnexpected = true;

        var response = await _client.PostAsync("/settings/test-recap-email", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Theory]
    [InlineData("test-kindle-email", "draft@kindle.com", false)]
    [InlineData("test-recap-email", "draft@example.com", false)]
    [InlineData("test-kindle-email", "draft@kindle.com", true)]
    [InlineData("test-recap-email", "draft@example.com", true)]
    public async Task TestEmail_ExplicitAddress_UsesDraftWithoutPersisting(string endpoint, string address, bool fails)
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "saved@kindle.com",
            DeliveryEmail = "saved@example.com",
        });
        _fakeMail.ShouldThrow = fails;

        var response = await _client.PostAsJsonAsync($"/settings/{endpoint}", new DeliveryTestRequest { ToAddress = $" {address} " });

        Assert.Equal(fails ? HttpStatusCode.BadGateway : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(address, _fakeMail.LastAddress);
        var saved = await _client.GetFromJsonAsync<SettingsResponse>("/settings");
        Assert.NotNull(saved);
        Assert.Equal("saved@kindle.com", saved.KindleEmail);
        Assert.Equal("saved@example.com", saved.DeliveryEmail);
    }

    [Theory]
    [InlineData("test-kindle-email", "draft@kindle.com")]
    [InlineData("test-recap-email", "draft@example.com")]
    public async Task TestEmail_NoSavedAddress_CanTestDraft(string endpoint, string address)
    {
        var response = await _client.PostAsJsonAsync($"/settings/{endpoint}", new DeliveryTestRequest { ToAddress = address });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(address, _fakeMail.LastAddress);
        var saved = await _client.GetFromJsonAsync<SettingsResponse>("/settings");
        Assert.NotNull(saved);
        Assert.Empty(saved.KindleEmail);
        Assert.Null(saved.DeliveryEmail);
    }

    [Theory]
    [InlineData("test-kindle-email", "")]
    [InlineData("test-kindle-email", "invalid")]
    [InlineData("test-kindle-email", null)]
    [InlineData("test-recap-email", "")]
    [InlineData("test-recap-email", "invalid")]
    [InlineData("test-recap-email", null)]
    public async Task TestEmail_InvalidDraft_DoesNotFallBackToSaved(string endpoint, string? address)
    {
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest
        {
            KindleEmail = "saved@kindle.com",
            DeliveryEmail = "saved@example.com",
        });

        var response = await _client.PostAsJsonAsync($"/settings/{endpoint}", new DeliveryTestRequest { ToAddress = address });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Null(_fakeMail.LastAddress);
    }

    [Theory]
    [InlineData(null, "saved-secret", false)]
    [InlineData("", "", false)]
    [InlineData("draft-secret", "draft-secret", false)]
    [InlineData("draft-secret", "draft-secret", true)]
    public async Task TestSmtp_Overrides_UsesDraftAndPreservesStoredSettings(string? password, string expectedPassword, bool fails)
    {
        await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "saved.example.com",
            Port = 587,
            FromAddress = "saved@example.com",
            Username = "saved-user",
            Password = "saved-secret",
        });
        var before = await _client.GetFromJsonAsync<SmtpSettingsResponse>("/settings/smtp");
        _fakeMail.ShouldThrow = fails;

        var response = await _client.PostAsJsonAsync("/settings/smtp/test", new SmtpTestRequest
        {
            ToAddress = "test@example.com",
            Settings = new UpdateSmtpSettingsRequest
            {
                Host = " draft.example.com ",
                Port = 2525,
                FromAddress = " draft@example.com ",
                Username = "draft-user",
                Password = password,
            },
        });

        Assert.Equal(fails ? HttpStatusCode.BadGateway : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test@example.com", _fakeMail.LastAddress);
        var tested = _fakeMail.LastSmtpSettings;
        Assert.NotNull(tested);
        Assert.Equal("draft.example.com", tested.Host);
        Assert.Equal(2525, tested.Port);
        Assert.Equal("draft@example.com", tested.FromAddress);
        Assert.Equal("draft-user", tested.Username);
        Assert.Equal(expectedPassword, tested.Password);
        Assert.Equal(before, await _client.GetFromJsonAsync<SmtpSettingsResponse>("/settings/smtp"));
        var effective = await _factory.Services.GetRequiredService<SmtpConfigurationService>().GetEffectiveAsync();
        Assert.Equal("saved-secret", effective.Settings.Password);
    }

    [Fact]
    public async Task TestSmtp_NoSavedSettings_TestsDraftWithoutCreatingConfiguration()
    {
        var before = await _client.GetFromJsonAsync<SmtpSettingsResponse>("/settings/smtp");
        var response = await _client.PostAsJsonAsync("/settings/smtp/test", new SmtpTestRequest
        {
            Settings = new UpdateSmtpSettingsRequest
            {
                Host = "draft.example.com",
                Port = 2525,
                FromAddress = "draft@example.com",
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("draft@example.com", _fakeMail.LastAddress);
        Assert.Equal(before, await _client.GetFromJsonAsync<SmtpSettingsResponse>("/settings/smtp"));
    }

    [Fact]
    public async Task TestSmtp_NoBody_UsesEffectiveConfiguration()
    {
        await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "saved.example.com",
            FromAddress = "saved@example.com",
        });

        var response = await _client.PostAsync("/settings/smtp/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("saved@example.com", _fakeMail.LastAddress);
        Assert.Equal("saved.example.com", _fakeMail.LastSmtpSettings?.Host);
    }

    [Theory]
    [InlineData("", 587, "sender@example.com")]
    [InlineData("smtp.example.com", 0, "sender@example.com")]
    [InlineData("smtp.example.com", 65536, "sender@example.com")]
    [InlineData("smtp.example.com", 587, "invalid")]
    public async Task TestSmtp_InvalidDraft_Returns422WithoutSending(string host, int port, string fromAddress)
    {
        var response = await _client.PostAsJsonAsync("/settings/smtp/test", new SmtpTestRequest
        {
            Settings = new UpdateSmtpSettingsRequest { Host = host, Port = port, FromAddress = fromAddress },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Null(_fakeMail.LastAddress);
    }

    private sealed class FakeMailDeliveryService : IMailDeliveryService
    {
        public List<string> SentAddresses { get; } = [];
        public string? LastAddress { get; private set; }
        public SmtpSettings? LastSmtpSettings { get; private set; }
        public bool ShouldThrow { get; set; }
        public bool ShouldThrowUnexpected { get; set; }

        public Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default, SmtpSettings? smtpSettings = null)
        {
            LastAddress = toAddress;
            LastSmtpSettings = smtpSettings;
            if (ShouldThrowUnexpected)
                throw new InvalidOperationException("Unexpected configuration error.");

            if (ShouldThrow)
                throw new System.Net.Sockets.SocketException(10061);

            SentAddresses.Add(toAddress);
            return Task.CompletedTask;
        }

        public Task SendHtmlRecapAsync(string toAddress, string htmlBody, string plainTextBody, string subject = "Your Relego Recap", CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
