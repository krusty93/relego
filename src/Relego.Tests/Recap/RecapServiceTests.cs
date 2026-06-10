using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Models;
using Relego.Server.Services;

namespace Relego.Tests.Recap;

public sealed class RecapServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly RecapRepository _recapRepository;
    private readonly UserRepository _userRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly HighlightSelectionService _selectionService;
    private readonly FakeMailDeliveryService _fakeMailService;
    private readonly RecapService _sut;
    private int _userId;

    private static readonly DateTimeOffset ScheduledFor = new(2026, 4, 15, 18, 0, 0, TimeSpan.Zero);

    static RecapServiceTests()
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }

    public RecapServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        _recapRepository = new RecapRepository(_connection);
        _userRepository = new UserRepository(_connection);
        _settingsRepository = new SettingsRepository(_connection);
        _selectionService = new HighlightSelectionService(_recapRepository);
        _fakeMailService = new FakeMailDeliveryService();

        _sut = new RecapService(
            _selectionService,
            _fakeMailService,
            _recapRepository,
            _userRepository,
            _settingsRepository,
            NullLogger<RecapService>.Instance);
    }

    public async Task InitializeAsync()
    {
        await new SchemaBootstrap().ApplyAsync(_connection);
        _userId = await _userRepository.EnsureUserAsync();
        await _connection.ExecuteAsync(
            "UPDATE users SET kindle_email = @Email WHERE id = @Id",
            new { Email = "test@kindle.com", Id = _userId });
        await _settingsRepository.UpsertAsync(new Settings { UserId = _userId, Count = 3 });
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_ImmediateSuccess_UpdatesJobAndHighlights()
    {
        await SeedHighlightsAsync(3);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(1, _fakeMailService.SendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
        Assert.Equal(1, job.AttemptCount);

        var highlights = await GetHighlightsAsync();
        Assert.All(highlights, h => Assert.Equal(1, h.DeliveryCount));
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_RetriesAndDelivers()
    {
        await SeedHighlightsAsync(2);
        _fakeMailService.FailuresBeforeSuccess = 2; // Fail first 2 attempts, succeed on 3rd

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(3, _fakeMailService.SendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
        Assert.Equal(3, job.AttemptCount);

        var highlights = await GetHighlightsAsync();
        Assert.All(highlights, h => Assert.Equal(1, h.DeliveryCount));
    }

    [Fact]
    public async Task ExecuteAsync_AllAttemptsFail_MarksJobFailedAndDoesNotUpdateHistory()
    {
        await SeedHighlightsAsync(2);
        _fakeMailService.FailuresBeforeSuccess = 10; // Always fail

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        // 3 kindle attempts = 1 initial + 2 retries
        Assert.Equal(3, _fakeMailService.SendCount);
        Assert.Equal(0, _fakeMailService.HtmlSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Status);
        // Total attempts: 3 kindle + 0 email
        Assert.Equal(3, job.AttemptCount);
        Assert.Contains("Kindle delivery failed", job.ErrorMessage);

        var highlights = await GetHighlightsAsync();
        Assert.All(highlights, h => Assert.Equal(0, h.DeliveryCount));
    }

    [Fact]
    public async Task ExecuteAsync_NoEligibleHighlights_SkipsDelivery()
    {
        // No highlights seeded

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(0, _fakeMailService.SendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Status);
        Assert.Contains("No eligible highlights", job.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_NoEmailChannel_SkipsDelivery()
    {
        await _connection.ExecuteAsync(
            "UPDATE users SET kindle_email = '' WHERE id = @Id",
            new { Id = _userId });
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(0, _fakeMailService.SendCount);
        Assert.Equal(0, _fakeMailService.HtmlSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Status);
        Assert.Contains("No delivery channel", job.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnRetry_UpdatesHistoryOnlyOnce()
    {
        await SeedHighlightsAsync(1);
        _fakeMailService.FailuresBeforeSuccess = 1; // Fail once, then succeed

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        var highlights = await GetHighlightsAsync();
        // delivery_count should be exactly 1, not incremented per retry
        Assert.All(highlights, h => Assert.Equal(1, h.DeliveryCount));
    }

    private async Task SeedHighlightsAsync(int count)
    {
        var authorId = await _connection.QuerySingleAsync<int>(
            "INSERT INTO authors (name) VALUES ('Author A'); SELECT last_insert_rowid();");
        var bookId = await _connection.QuerySingleAsync<int>(
            "INSERT INTO books (user_id, author_id, title) VALUES (@UserId, @AuthorId, 'Test Book'); SELECT last_insert_rowid();",
            new { UserId = _userId, AuthorId = authorId });

        for (var i = 0; i < count; i++)
        {
            await _connection.ExecuteAsync(
                "INSERT INTO highlights (user_id, book_id, text, weight, excluded, delivery_count, created_at) VALUES (@UserId, @BookId, @Text, 3, 0, 0, @CreatedAt)",
                new { UserId = _userId, BookId = bookId, Text = $"Highlight {i + 1}", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10).ToString("o") });
        }
    }

    private async Task<IReadOnlyList<HighlightRow>> GetHighlightsAsync()
    {
        var rows = await _connection.QueryAsync<HighlightRow>(
            "SELECT id AS Id, delivery_count AS DeliveryCount, last_seen AS LastSeen FROM highlights WHERE user_id = @UserId",
            new { UserId = _userId });
        return rows.ToList();
    }

    private sealed class HighlightRow
    {
        public int Id { get; set; }
        public int DeliveryCount { get; set; }
        public string? LastSeen { get; set; }
    }
}

internal sealed class FakeMailDeliveryService : IMailDeliveryService
{
    public int SendCount { get; private set; }
    public int FailuresBeforeSuccess { get; set; }
    public int HtmlSendCount { get; set; }
    public int HtmlFailuresBeforeSuccess { get; set; }

    public Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
    {
        SendCount++;

        if (SendCount <= FailuresBeforeSuccess)
        {
            throw new IOException("Simulated transient SMTP failure");
        }

        return Task.CompletedTask;
    }

    public Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SendHtmlRecapAsync(IReadOnlyList<SelectionCandidate> highlights, DateTimeOffset recapDate, string toAddress, CancellationToken cancellationToken = default)
    {
        HtmlSendCount++;

        if (HtmlSendCount <= HtmlFailuresBeforeSuccess)
        {
            throw new IOException("Simulated transient HTML delivery failure");
        }

        return Task.CompletedTask;
    }
}
