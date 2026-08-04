using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class SmtpSettingsEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmtpSettingsEndpointTests()
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
    public async Task GetSmtp_BeforeAnySave_ReportsNonDatabaseSourceAndNoPassword()
    {
        var response = await _client.GetFromJsonAsync<SmtpSettingsResponse>("/settings/smtp");

        Assert.NotNull(response);
        Assert.NotEqual("database", response.Source);
        Assert.False(response.PasswordSet);
        Assert.Null(response.UpdatedAt);
    }

    [Fact]
    public async Task PutSmtp_SavesValuesAndSwitchesSourceToDatabase()
    {
        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "smtp.example.com",
            Port = 465,
            FromAddress = "relego@example.com",
            Username = "relego",
            Password = "hunter2",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = await response.Content.ReadFromJsonAsync<SmtpSettingsResponse>();
        Assert.NotNull(saved);
        Assert.Equal("smtp.example.com", saved.Host);
        Assert.Equal(465, saved.Port);
        Assert.Equal("relego@example.com", saved.FromAddress);
        Assert.Equal("database", saved.Source);
        Assert.True(saved.PasswordSet);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task GetSmtp_NeverReturnsThePassword()
    {
        await SaveValidSettingsAsync();

        var raw = await _client.GetStringAsync("/settings/smtp");

        Assert.DoesNotContain("hunter2", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutSmtp_OmittedPassword_KeepsTheStoredOne()
    {
        await SaveValidSettingsAsync();

        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "smtp2.example.com",
        });

        var saved = await response.Content.ReadFromJsonAsync<SmtpSettingsResponse>();
        Assert.NotNull(saved);
        Assert.Equal("smtp2.example.com", saved.Host);
        Assert.Equal("relego@example.com", saved.FromAddress);
        Assert.True(saved.PasswordSet);
    }

    [Fact]
    public async Task PutSmtp_EmptyPassword_ClearsIt()
    {
        await SaveValidSettingsAsync();

        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Password = string.Empty,
        });

        var saved = await response.Content.ReadFromJsonAsync<SmtpSettingsResponse>();
        Assert.NotNull(saved);
        Assert.False(saved.PasswordSet);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task PutSmtp_PortOutOfRange_Returns422(int port)
    {
        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Port = port,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PutSmtp_InvalidFromAddress_Returns422()
    {
        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            FromAddress = "not-an-email",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PutSmtp_EmptyHost_Returns422()
    {
        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "   ",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostSmtpTest_WithoutConfiguredHost_Returns422()
    {
        // The test environment configures no SMTP host, so the guard must fire before any send.
        var response = await _client.PostAsJsonAsync("/settings/smtp/test", new SmtpTestRequest());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task SaveValidSettingsAsync()
    {
        var response = await _client.PutAsJsonAsync("/settings/smtp", new UpdateSmtpSettingsRequest
        {
            Host = "smtp.example.com",
            Port = 465,
            FromAddress = "relego@example.com",
            Username = "relego",
            Password = "hunter2",
        });

        response.EnsureSuccessStatusCode();
    }
}
