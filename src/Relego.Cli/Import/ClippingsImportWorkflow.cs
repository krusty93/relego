using Microsoft.Extensions.Logging;
using Relego.Cli.Infrastructure;
using Relego.Core.Parsing;
using Relego.Core.Sources;
using Relego.Core.Contracts;

namespace Relego.Cli.Import;

public sealed class ClippingsImportWorkflow(
    RelegoHttpClient client,
    HighlightSourceResolver resolver,
    ILogger<ClippingsImportWorkflow> logger)
{
    public async Task<ClippingsImportOutcome> ExecuteAsync(ClippingsImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (resolution, terminal) = await ResolveAsync(options, cancellationToken).ConfigureAwait(false);
        if (terminal is not null)
        {
            return terminal;
        }

        var outcomes = new List<ClippingsImportOutcome>(resolution!.Sources.Count);
        foreach (var resolved in resolution.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await ImportSourceAsync(resolved, options, cancellationToken).ConfigureAwait(false));
        }

        return outcomes.Count == 1 ? outcomes[0] : BuildAggregate(outcomes);
    }

    private async Task<(SourceResolution? Resolution, ClippingsImportOutcome? Terminal)> ResolveAsync(
        ClippingsImportOptions options,
        CancellationToken cancellationToken)
    {
        var userPath = string.IsNullOrWhiteSpace(options.FilePath) ? null : options.FilePath.Trim();

        var resolution = resolver.Resolve(userPath);
        if (resolution.Found)
        {
            return (resolution, null);
        }

        // Nothing detected — give the interactive prompt (CLI/TUI) a chance to supply a path.
        if (options.ResolvePathAsync is not null)
        {
            var prompted = await options
                .ResolvePathAsync(new ClippingsImportPathPromptRequest(DetectedPath: null), cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(prompted))
            {
                return (null, ClippingsImportOutcome.Cancelled());
            }

            prompted = prompted.Trim();
            var promptedResolution = resolver.Resolve(prompted);
            return promptedResolution.Found
                ? (promptedResolution, null)
                : (null, NotFoundOutcome(prompted, promptedResolution));
        }

        return (null, NotFoundOutcome(userPath, resolution));
    }

    private static ClippingsImportOutcome NotFoundOutcome(string? userPath, SourceResolution resolution)
    {
        // An explicit path the user named but that does not exist → actionable "file not found".
        if (!string.IsNullOrWhiteSpace(userPath) && !File.Exists(userPath) && !Directory.Exists(userPath))
        {
            return ClippingsImportOutcome.FileNotFound(userPath);
        }

        return ClippingsImportOutcome.NotDetected(resolution.ProbedLocations);
    }

    private async Task<ClippingsImportOutcome> ImportSourceAsync(
        ResolvedSource resolved,
        ClippingsImportOptions options,
        CancellationToken cancellationToken)
    {
        var path = resolved.ResolvedPath;
        var descriptor = resolved.Source.Descriptor;
        logger.LogDebug("Reading {Source} highlights from {Path}", descriptor.DisplayName, path);

        ParseResult parseResult;
        try
        {
            parseResult = options.ParseAsync is not null
                ? await options.ParseAsync(path, options.ParserLogger).ConfigureAwait(false)
                : await resolved.Source.ReadAsync(path, options.ParserLogger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read {Source} source at {Path}", descriptor.DisplayName, path);
            return ClippingsImportOutcome.ParseFailed(path, ex) with { Source = descriptor };
        }

        if (parseResult.Books.Count == 0)
        {
            return ClippingsImportOutcome.NoHighlightsFound(path, parseResult) with { Source = descriptor };
        }

        var request = CreateSyncRequest(parseResult);
        logger.LogDebug(
            "Sending {BookCount} books with {HighlightCount} highlights to server",
            request.Books.Count,
            request.Books.Sum(book => book.Highlights.Count));

        try
        {
            var response = await client.PostSyncAsync(request, cancellationToken).ConfigureAwait(false);
            return ClippingsImportOutcome.Succeeded(path, parseResult, response) with { Source = descriptor };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to import {Source} highlights from {Path}", descriptor.DisplayName, path);
            return ClippingsImportOutcome.ServerError(path, parseResult, ex) with { Source = descriptor };
        }
    }

    private static ClippingsImportOutcome BuildAggregate(IReadOnlyList<ClippingsImportOutcome> outcomes)
    {
        var anySucceeded = outcomes.Any(o => o.Status == ClippingsImportStatus.Succeeded);

        var mergedBooks = outcomes
            .Where(o => o.ParseResult is not null)
            .SelectMany(o => o.ParseResult!.Books)
            .ToList();

        var mergedParse = new ParseResult(
            mergedBooks,
            outcomes.Sum(o => o.ParseResult?.TotalEntriesProcessed ?? 0),
            outcomes.Sum(o => o.ParseResult?.DuplicatesRemoved ?? 0));

        var successResponses = outcomes
            .Where(o => o.Status == ClippingsImportStatus.Succeeded && o.Response is not null)
            .Select(o => o.Response!)
            .ToList();

        SyncResponse? mergedResponse = successResponses.Count == 0 ? null : new SyncResponse
        {
            NewHighlights = successResponses.Sum(r => r.NewHighlights),
            DuplicateHighlights = successResponses.Sum(r => r.DuplicateHighlights),
            NewBooks = successResponses.Sum(r => r.NewBooks),
            NewAuthors = successResponses.Sum(r => r.NewAuthors)
        };

        // Success if any source imported; otherwise surface the first failure.
        var status = anySucceeded ? ClippingsImportStatus.Succeeded : outcomes[0].Status;

        return new ClippingsImportOutcome
        {
            Status = status,
            ParseResult = mergedParse,
            Response = mergedResponse,
            SourceOutcomes = outcomes,
            Message = anySucceeded ? null : outcomes[0].Message
        };
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

    /// <summary>
    /// The source that produced this outcome, when known. <see langword="null"/> for an
    /// aggregate outcome that combines several sources or a pre-import terminal result.
    /// </summary>
    public SourceDescriptor? Source { get; init; }

    /// <summary>
    /// The per-source outcomes when more than one source was imported in a single run
    /// (both devices connected). Empty for the common single-source case.
    /// </summary>
    public IReadOnlyList<ClippingsImportOutcome> SourceOutcomes { get; init; } = [];

    /// <summary>
    /// Every location that detection checked, populated on a not-detected outcome so the
    /// error names exactly what was looked for and where (FR-009).
    /// </summary>
    public IReadOnlyList<string> ProbedLocations { get; init; } = [];

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

    public static ClippingsImportOutcome NotDetected(IReadOnlyList<string> probedLocations) => new()
    {
        Status = ClippingsImportStatus.FileNotFound,
        Message = probedLocations.Count == 0
            ? "No Kindle or Kobo source detected."
            : "No Kindle or Kobo source detected. Checked: " + string.Join(", ", probedLocations),
        ProbedLocations = probedLocations
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
