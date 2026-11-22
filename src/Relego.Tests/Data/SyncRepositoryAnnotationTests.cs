using System.Globalization;
using Microsoft.Data.Sqlite;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;

namespace Relego.Tests.Data;

/// <summary>
/// Covers the annotation identity and the additive re-import rule: importing the same file twice
/// must leave the stored annotation untouched, and refreshing a note must not reset what Relego
/// learned about the highlight (weight, exclusion, delivery count, last seen).
/// </summary>
public sealed class SyncRepositoryAnnotationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset AddedOn = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SyncRepository _repository;
    private readonly UserRepository _users;
    private int _userId;

    public SyncRepositoryAnnotationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        _repository = new SyncRepository(_connection);
        _users = new UserRepository(_connection);
    }

    public async Task InitializeAsync()
    {
        await new SchemaBootstrap().ApplyAsync(_connection);
        _userId = await _users.EnsureUserAsync();
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportAsync_NewNote_StoresItOnThePassage()
    {
        var response = await ImportAsync(Highlight("The passage.", "My remark.", "Location 100-110"));

        Assert.Equal(1, response.NewHighlights);
        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal("The passage.", row.Text);
        Assert.Equal("My remark.", row.Note);
        Assert.Equal("Location 100-110", row.SourcePosition);
        Assert.Equal(100, row.PositionStart);
        Assert.Equal("location", row.PositionKind);
    }

    [Fact]
    public async Task ImportAsync_SameFileTwice_ChangesNoCounts()
    {
        var request = Request(Highlight("The passage.", "My remark.", "Location 100-110"));

        var first = await _repository.ImportAsync(_userId, request);
        var second = await _repository.ImportAsync(_userId, Clone(request));

        Assert.Equal(1, first.NewHighlights);
        Assert.Equal(0, first.DuplicateHighlights);
        Assert.Equal(0, second.NewHighlights);
        Assert.Equal(1, second.DuplicateHighlights);
        Assert.Single(await ReadAnnotationsAsync());
    }

    [Fact]
    public async Task ImportAsync_SameFileTwice_PreservesIdAndReaderState()
    {
        var request = Request(Highlight("The passage.", "My remark.", "Location 100-110"));
        await _repository.ImportAsync(_userId, request);
        await MarkReaderStateAsync(weight: 5, excluded: 1, deliveryCount: 4, lastSeen: "2026-03-01T00:00:00Z");

        var before = Assert.Single(await ReadAnnotationsAsync());
        await _repository.ImportAsync(_userId, Clone(request));
        var after = Assert.Single(await ReadAnnotationsAsync());

        Assert.Equal(before.Id, after.Id);
        Assert.Equal(5, after.Weight);
        Assert.Equal(1, after.Excluded);
        Assert.Equal(4, after.DeliveryCount);
        Assert.Equal("2026-03-01T00:00:00Z", after.LastSeen);
    }

    [Fact]
    public async Task ImportAsync_ChangedNote_UpdatesTheSameRow()
    {
        await _repository.ImportAsync(_userId, Request(Highlight("The passage.", "First wording.", "Location 100-110")));
        var original = Assert.Single(await ReadAnnotationsAsync());

        await _repository.ImportAsync(_userId, Request(Highlight("The passage.", "Final wording.", "Location 100-110")));
        var updated = Assert.Single(await ReadAnnotationsAsync());

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Final wording.", updated.Note);
    }

    [Fact]
    public async Task ImportAsync_MarkersRemoved_ClearsTheVocabularyAndKeepsTheRow()
    {
        await _repository.ImportAsync(_userId, Request(
            Highlight("First fragment.", ".h1 .c1 .prob", "Location 100-110")));
        await MarkReaderStateAsync(weight: 5, excluded: 1, deliveryCount: 3, lastSeen: "2026-03-01T00:00:00Z");
        var original = Assert.Single(await ReadAnnotationsAsync());

        await _repository.ImportAsync(_userId, Request(
            Highlight("First fragment.", "Just a remark.", "Location 100-110")));
        var updated = Assert.Single(await ReadAnnotationsAsync());

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Just a remark.", updated.Note);
        Assert.Null(updated.HeadingLevel);
        Assert.Null(updated.StitchLevel);
        Assert.Null(updated.Tags);
        Assert.Equal(5, updated.Weight);
        Assert.Equal(1, updated.Excluded);
        Assert.Equal(3, updated.DeliveryCount);
    }

    [Fact]
    public async Task ImportAsync_NoteMarkers_AreStoredAsVocabulary()
    {
        await ImportAsync(Highlight(
            "The passage.", ".h2 .c1 .prob .review A summary with markers.", "Location 100-110"));

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal("A summary with markers.", row.Note);
        Assert.Equal(2, row.HeadingLevel);
        Assert.Equal(1, row.StitchLevel);
        Assert.Equal("prob review", row.Tags);
    }

    [Fact]
    public async Task ImportAsync_NoteOnly_KeepsAnEmptyPassageAndItsNote()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = string.Empty,
            Position = "Location 200",
            Note = "A standalone thought.",
            AddedOn = AddedOn,
        });

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal(string.Empty, row.Text);
        Assert.Equal("A standalone thought.", row.Note);
    }

    [Fact]
    public async Task ImportAsync_NoteOnlyTwice_IsNotDuplicated()
    {
        var request = Request(new SyncHighlightRequest
        {
            Text = string.Empty,
            Position = "Location 200",
            Note = "A standalone thought.",
            AddedOn = AddedOn,
        });

        await _repository.ImportAsync(_userId, request);
        var second = await _repository.ImportAsync(_userId, Clone(request));

        Assert.Equal(0, second.NewHighlights);
        Assert.Equal(1, second.DuplicateHighlights);
        Assert.Single(await ReadAnnotationsAsync());
    }

    [Fact]
    public async Task ImportAsync_MarkersOnlyNote_StillLeavesACarriedNote()
    {
        await ImportAsync(Highlight("The passage.", ".h1", "Location 100-110"));

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal("The passage.", row.Text);
        Assert.Null(row.Note);
        Assert.Equal(1, row.HeadingLevel);
    }

    [Fact]
    public async Task ImportAsync_LegacyNotePrefix_IsStoredVerbatimWithoutMarkers()
    {
        await ImportAsync(Highlight("The passage.", "[my note] legacy comment", "Location 100-110"));

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal("[my note] legacy comment", row.Note);
        Assert.Null(row.HeadingLevel);
        Assert.Null(row.Tags);
    }

    [Fact]
    public async Task ImportAsync_NewRow_StampsCreatedAtWithTheImportTime()
    {
        await ImportAsync(Highlight("The passage.", "My remark.", "Location 100-110"));

        var row = Assert.Single(await ReadAnnotationsAsync());
        var createdAt = DateTimeOffset.Parse(row.CreatedAt, CultureInfo.InvariantCulture);

        Assert.True(
            Math.Abs((createdAt - DateTimeOffset.UtcNow).TotalMinutes) < 5,
            $"created_at '{row.CreatedAt}' is not close to the import time.");
        Assert.NotEqual(AddedOn, createdAt);
        Assert.NotEqual(AddedOn.ToString("o"), row.CreatedAt);
    }

    [Fact]
    public async Task ImportAsync_ExistingRow_KeepsItsCreatedAt()
    {
        var request = Request(Highlight("The passage.", "My remark.", "Location 100-110"));
        await _repository.ImportAsync(_userId, request);

        const string pinnedCreatedAt = "2020-01-01T00:00:00.0000000+00:00";
        await SetCreatedAtAsync(pinnedCreatedAt);

        await _repository.ImportAsync(_userId, Clone(request));
        var afterDuplicate = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal(pinnedCreatedAt, afterDuplicate.CreatedAt);

        await _repository.ImportAsync(_userId, Request(Highlight("The passage.", "Final wording.", "Location 100-110")));
        var afterUpdate = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal(pinnedCreatedAt, afterUpdate.CreatedAt);
        Assert.NotEqual(AddedOn.ToString("o"), afterUpdate.CreatedAt);
    }

    [Fact]
    public async Task ImportAsync_SourceWithoutAddedOn_LeavesAddedOnNull()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = "The passage.",
            Position = "Location 100-110",
            Note = "My remark.",
            AddedOn = null,
        });

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Null(row.AddedOn);
        Assert.NotNull(row.CreatedAt);
    }

    [Fact]
    public async Task ImportAsync_SourceWithAddedOn_StoresOnlyWhatTheSourceReported()
    {
        await ImportAsync(Highlight("The passage.", "My remark.", "Location 100-110"));

        var row = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal(AddedOn.ToString("o"), row.AddedOn);
        Assert.Equal(AddedOn, DateTimeOffset.Parse(row.AddedOn!, CultureInfo.InvariantCulture));
    }

    private static SyncRequest Request(params SyncHighlightRequest[] highlights) => new()
    {
        Books =
        [
            new SyncBookRequest
            {
                Title = "A Book",
                Author = "An Author",
                Highlights = [.. highlights],
            },
        ],
    };

    private static SyncHighlightRequest Highlight(string text, string? note, string? position) => new()
    {
        Text = text,
        Note = note,
        Position = position,
        AddedOn = AddedOn,
    };

    private static SyncRequest Clone(SyncRequest request) => new()
    {
        Books =
        [
            .. request.Books.Select(book => new SyncBookRequest
            {
                Title = book.Title,
                Author = book.Author,
                Highlights = [.. book.Highlights.Select(highlight => new SyncHighlightRequest
                {
                    Text = highlight.Text,
                    Note = highlight.Note,
                    Position = highlight.Position,
                    AddedOn = highlight.AddedOn,
                })],
            }),
        ],
    };

    private async Task<SyncResponse> ImportAsync(SyncHighlightRequest highlight)
        => await _repository.ImportAsync(_userId, Request(highlight));

    private async Task MarkReaderStateAsync(int weight, int excluded, int deliveryCount, string lastSeen)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE highlights
            SET weight = $weight, excluded = $excluded, delivery_count = $deliveryCount, last_seen = $lastSeen;
            """;
        command.Parameters.AddWithValue("$weight", weight);
        command.Parameters.AddWithValue("$excluded", excluded);
        command.Parameters.AddWithValue("$deliveryCount", deliveryCount);
        command.Parameters.AddWithValue("$lastSeen", lastSeen);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetCreatedAtAsync(string createdAt)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE highlights SET created_at = $createdAt;";
        command.Parameters.AddWithValue("$createdAt", createdAt);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<AnnotationRow>> ReadAnnotationsAsync()
    {
        var rows = new List<AnnotationRow>();

        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, note, heading_level, stitch_level, tags, source_position, position_start,
                   position_kind, weight, excluded, delivery_count, last_seen, created_at, added_on
            FROM highlights
            ORDER BY id;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AnnotationRow(
                Id: reader.GetInt64(0),
                Text: reader.GetString(1),
                Note: reader.IsDBNull(2) ? null : reader.GetString(2),
                HeadingLevel: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                StitchLevel: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Tags: reader.IsDBNull(5) ? null : reader.GetString(5),
                SourcePosition: reader.IsDBNull(6) ? null : reader.GetString(6),
                PositionStart: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                PositionKind: reader.IsDBNull(8) ? null : reader.GetString(8),
                Weight: reader.GetInt32(9),
                Excluded: reader.GetInt32(10),
                DeliveryCount: reader.GetInt32(11),
                LastSeen: reader.IsDBNull(12) ? null : reader.GetString(12),
                CreatedAt: reader.GetString(13),
                AddedOn: reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return rows;
    }

    private sealed record AnnotationRow(
        long Id,
        string Text,
        string? Note,
        int? HeadingLevel,
        int? StitchLevel,
        string? Tags,
        string? SourcePosition,
        int? PositionStart,
        string? PositionKind,
        int Weight,
        int Excluded,
        int DeliveryCount,
        string? LastSeen,
        string CreatedAt,
        string? AddedOn);
}
