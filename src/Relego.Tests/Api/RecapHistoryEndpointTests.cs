using System.Data;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class RecapHistoryEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public RecapHistoryEndpointTests()
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
    public async Task GetRecaps_NoDeliveries_ReturnsEmptyList()
    {
        var response = await _client.GetFromJsonAsync<RecapHistoryResponse>("/recaps");

        Assert.NotNull(response);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetRecaps_ReturnsNewestFirstWithStatusAndError()
    {
        var userId = await EnsureUserAsync();

        await InsertJobAsync(userId, "2026-01-01T18:00:00.0000000Z", "delivered", 1, null, "2026-01-01T18:00:04.0000000Z");
        await InsertJobAsync(userId, "2026-01-08T18:00:00.0000000Z", "failed", 3, "Connection refused", null);

        var response = await _client.GetFromJsonAsync<RecapHistoryResponse>("/recaps");

        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);

        var newest = response.Items[0];
        Assert.Equal("failed", newest.Status);
        Assert.Equal(3, newest.AttemptCount);
        Assert.Equal("Connection refused", newest.ErrorMessage);
        Assert.Null(newest.DeliveredAt);

        var oldest = response.Items[1];
        Assert.Equal("delivered", oldest.Status);
        Assert.NotNull(oldest.DeliveredAt);
    }

    [Fact]
    public async Task GetRecaps_RespectsLimit()
    {
        var userId = await EnsureUserAsync();

        for (var day = 1; day <= 5; day++)
            await InsertJobAsync(userId, $"2026-02-0{day}T18:00:00.0000000Z", "delivered", 1, null, null);

        var response = await _client.GetFromJsonAsync<RecapHistoryResponse>("/recaps?limit=2");

        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
    }

    [Theory]
    [InlineData("/recaps?limit=0")]
    [InlineData("/recaps?limit=101")]
    public async Task GetRecaps_InvalidLimit_Returns422(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<int> EnsureUserAsync()
    {
        // /status creates the implicit user as a side effect.
        (await _client.GetAsync("/status")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<int>("SELECT id FROM users LIMIT 1");
    }

    private async Task InsertJobAsync(
        int userId,
        string scheduledFor,
        string status,
        int attemptCount,
        string? errorMessage,
        string? deliveredAt)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        await connection.ExecuteAsync(
            """
            INSERT INTO recap_jobs (user_id, scheduled_for, status, attempt_count, error_message, created_at, delivered_at)
            VALUES (@UserId, @ScheduledFor, @Status, @AttemptCount, @ErrorMessage, @CreatedAt, @DeliveredAt)
            """,
            new
            {
                UserId = userId,
                ScheduledFor = scheduledFor,
                Status = status,
                AttemptCount = attemptCount,
                ErrorMessage = errorMessage,
                CreatedAt = scheduledFor,
                DeliveredAt = deliveredAt,
            });
    }
}
