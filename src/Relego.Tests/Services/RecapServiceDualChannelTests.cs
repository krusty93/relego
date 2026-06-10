using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Models;
using Relego.Server.Services;

namespace Relego.Tests.Services;

public sealed class RecapServiceDualChannelTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly RecapRepository _recapRepository;
    private readonly UserRepository _userRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly HighlightSelectionService _selectionService;
    private readonly FakeDualMailDeliveryService _fakeMailService;
    private readonly RecapService _sut;
    private int _userId;

    private static readonly DateTimeOffset ScheduledFor = new(2026, 6, 15, 18, 0, 0, TimeSpan.Zero);

    static RecapServiceDualChannelTests()
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }

    public RecapServiceDualChannelTests()
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
        _fakeMailService = new FakeDualMailDeliveryService();

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
        await _userRepository.UpdateKindleEmailAsync(_userId, "kindle@kindle.com");
        await _userRepository.UpdateDeliveryEmailAsync(_userId, "user@example.com");
        await _settingsRepository.UpsertAsync(new Settings { UserId = _userId, Count = 3 });
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_BothChannelsConfigured_DeliversBoth()
    {
        await SeedHighlightsAsync(3);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(1, _fakeMailService.KindleSendCount);
        Assert.Equal(1, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyKindleConfigured_DeliversKindleOnly()
    {
        await _userRepository.UpdateDeliveryEmailAsync(_userId, null);
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(1, _fakeMailService.KindleSendCount);
        Assert.Equal(0, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyEmailConfigured_DeliversEmailOnly()
    {
        await _userRepository.UpdateKindleEmailAsync(_userId, string.Empty);
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(0, _fakeMailService.KindleSendCount);
        Assert.Equal(1, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_NeitherConfigured_SkipsWithWarning()
    {
        await _userRepository.UpdateKindleEmailAsync(_userId, string.Empty);
        await _userRepository.UpdateDeliveryEmailAsync(_userId, null);
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(0, _fakeMailService.KindleSendCount);
        Assert.Equal(0, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_KindleFails_EmailStillDelivers()
    {
        _fakeMailService.FailKindle = true;
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        // 3 attempts = 1 initial + 2 retries
        Assert.Equal(3, _fakeMailService.KindleSendCount);
        Assert.Equal(1, _fakeMailService.EmailSendCount); // still succeeds

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_KindleStillDelivers()
    {
        _fakeMailService.FailEmail = true;
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(1, _fakeMailService.KindleSendCount);
        // 3 attempts = 1 initial + 2 retries
        Assert.Equal(3, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("delivered", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_BothChannelsFail_MarksFailed()
    {
        _fakeMailService.FailKindle = true;
        _fakeMailService.FailEmail = true;
        await SeedHighlightsAsync(2);

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        // 3 attempts each = 1 initial + 2 retries
        Assert.Equal(3, _fakeMailService.KindleSendCount);
        Assert.Equal(3, _fakeMailService.EmailSendCount);

        var job = await _recapRepository.GetJobBySlotAsync(_userId, ScheduledFor);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Status);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRecap_NoEmailsSent()
    {
        // No highlights seeded

        await _sut.ExecuteAsync(_userId, ScheduledFor);

        Assert.Equal(0, _fakeMailService.KindleSendCount);
        Assert.Equal(0, _fakeMailService.EmailSendCount);
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
}

internal sealed class FakeDualMailDeliveryService : IMailDeliveryService
{
    public int KindleSendCount { get; private set; }
    public int EmailSendCount { get; private set; }
    public bool FailKindle { get; set; }
    public bool FailEmail { get; set; }

    public Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default)
    {
        KindleSendCount++;
        if (FailKindle)
            throw new IOException("Simulated Kindle delivery failure");
        return Task.CompletedTask;
    }

    public Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SendHtmlRecapAsync(string toAddress, string htmlBody, string plainTextBody, string subject = "Your Relego Recap", CancellationToken cancellationToken = default)
    {
        EmailSendCount++;
        if (FailEmail)
            throw new IOException("Simulated email delivery failure");
        return Task.CompletedTask;
    }
}
