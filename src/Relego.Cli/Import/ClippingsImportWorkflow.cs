using Microsoft.Extensions.Logging;
using Relego.Cli.Infrastructure;
using Relego.Cli.Parsing;
using Relego.Core.Contracts;

namespace Relego.Cli.Import;

public sealed class ClippingsImportWorkflow(RelegoHttpClient client, ILogger<ClippingsImportWorkflow> logger)
{
    public async Task<ClippingsImportOutcome> ExecuteAsync(ClippingsImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var filePath = await ResolveFilePathAsync(options, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ClippingsImportOutcome.Cancelled();
        }

        filePath = filePath.Trim();
        logger.LogDebug("Resolved clippings path: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            return ClippingsImportOutcome.FileNotFound(filePath);
        }

        ParseResult parseResult;

        try
        {
            var parseAsync = options.ParseAsync;
            parseResult = parseAsync is null
                ? await ClippingsParser.ParseAsync(filePath, options.ParserLogger).ConfigureAwait(false)
                : await parseAsync(filePath, options.ParserLogger).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse clippings file {FilePath}", filePath);
            return ClippingsImportOutcome.ParseFailed(filePath, ex);
        }

        if (parseResult.Books.Count == 0)
        {
            return ClippingsImportOutcome.NoHighlightsFound(filePath, parseResult);
        }

        var request = CreateSyncRequest(parseResult);
        logger.LogDebug(
            "Sending {BookCount} books with {HighlightCount} highlights to server",
            request.Books.Count,
            request.Books.Sum(book => book.Highlights.Count));

        try
        {
            var response = await client.PostSyncAsync(request, cancellationToken).ConfigureAwait(false);
            return ClippingsImportOutcome.Succeeded(filePath, parseResult, response);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to import clippings from {FilePath}", filePath);
            return ClippingsImportOutcome.ServerError(filePath, parseResult, ex);
        }
    }

    private static async Task<string?> ResolveFilePathAsync(ClippingsImportOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            return options.FilePath;
        }

        var detectedPath = KindleDetector.DetectClippingsPath();
        if (options.ResolvePathAsync is not null)
        {
            return await options.ResolvePathAsync(new ClippingsImportPathPromptRequest(detectedPath), cancellationToken).ConfigureAwait(false);
        }

        return detectedPath;
    }

    internal static SyncRequest CreateSyncRequest(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SyncRequest
        {
            Books = result.Books.Select(book => new SyncBookRequest
            {
                Title = book.Title,
                Author = book.Author,
                Highlights = book.Highlights.Select(highlight => new SyncHighlightRequest
                {
                    Text = highlight.Text,
                    AddedOn = highlight.AddedOn
                }).ToList()
            }).ToList()
        };
    }
}

public sealed record ClippingsImportOptions
{
    public string? FilePath { get; init; }

    public Func<ClippingsImportPathPromptRequest, CancellationToken, ValueTask<string?>>? ResolvePathAsync { get; init; }

    public Func<string, ILogger?, Task<ParseResult>>? ParseAsync { get; init; }

    public ILogger? ParserLogger { get; init; }
}

public sealed record ClippingsImportPathPromptRequest(string? DetectedPath);

public enum ClippingsImportStatus
{
    Cancelled,
    FileNotFound,
    ParseFailed,
    NoHighlightsFound,
    ServerError,
    Succeeded
}

public sealed record ClippingsImportOutcome
{
    public required ClippingsImportStatus Status { get; init; }

    public string? FilePath { get; init; }

    public string? Message { get; init; }

    public ParseResult? ParseResult { get; init; }

    public SyncResponse? Response { get; init; }

    public Exception? Error { get; init; }

    public int TotalHighlightsParsed => ParseResult?.Books.Sum(book => book.Highlights.Count) ?? 0;

    public bool IsSuccessful => Status is ClippingsImportStatus.NoHighlightsFound or ClippingsImportStatus.Succeeded;

    public static ClippingsImportOutcome Cancelled() => new()
    {
        Status = ClippingsImportStatus.Cancelled,
        Message = "Import cancelled."
    };

    public static ClippingsImportOutcome FileNotFound(string filePath) => new()
    {
        Status = ClippingsImportStatus.FileNotFound,
        FilePath = filePath,
        Message = $"File not found: {filePath}"
    };

    public static ClippingsImportOutcome ParseFailed(string filePath, Exception error) => new()
    {
        Status = ClippingsImportStatus.ParseFailed,
        FilePath = filePath,
        Message = error.Message,
        Error = error
    };

    public static ClippingsImportOutcome NoHighlightsFound(string filePath, ParseResult parseResult) => new()
    {
        Status = ClippingsImportStatus.NoHighlightsFound,
        FilePath = filePath,
        Message = "No highlights found in the clippings file.",
        ParseResult = parseResult
    };

    public static ClippingsImportOutcome ServerError(string filePath, ParseResult parseResult, HttpRequestException error) => new()
    {
        Status = ClippingsImportStatus.ServerError,
        FilePath = filePath,
        Message = error.Message,
        ParseResult = parseResult,
        Error = error
    };

    public static ClippingsImportOutcome Succeeded(string filePath, ParseResult parseResult, SyncResponse response) => new()
    {
        Status = ClippingsImportStatus.Succeeded,
        FilePath = filePath,
        ParseResult = parseResult,
        Response = response
    };
}
