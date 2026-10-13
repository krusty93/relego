using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Models;
using Relego.Server.Sync;

namespace Relego.Server.Endpoints;

/// <summary>
/// Maps the cloud sync REST endpoints. Providers are resolved through
/// <see cref="IEnumerable{ICloudSyncProvider}"/>; nothing branches on a provider id.
/// </summary>
public static class CloudSyncEndpoints
{
    private const int MaxRequestBodyBytes = 2 * 1024 * 1024;
    private const int HeartbeatSeconds = 60;
    private const int MaxProfileVersionLength = 32;
    private const int MaxFailureCodeLength = 64;
    private const string NoCommand = "none";
    private const int DefaultJobsLimit = 20;
    private const int MaxJobsLimit = 50;

    private static readonly string[] AllowedPhases = ["discovering", "reading", "uploading", "done"];

    private static readonly string[] AllowedCommands = ["sync", "full_resync"];

    private static readonly JsonSerializerOptions BatchJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Registers the cloud sync endpoints on the application.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapCloudSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/sync/providers", ([FromServices] IEnumerable<ICloudSyncProvider> providers) =>
        {
            var summaries = providers
                .Select(provider => new SyncProviderSummary
                {
                    ProviderId = provider.Descriptor.Id,
                    DisplayName = provider.Descriptor.DisplayName,
                    Capabilities = new SyncProviderCapabilitiesDto
                    {
                        PushIngest = provider.Capabilities.HasFlag(CloudSyncCapabilities.PushIngest),
                        FullResync = provider.Capabilities.HasFlag(CloudSyncCapabilities.FullResync),
                        ProgressReporting = provider.Capabilities.HasFlag(CloudSyncCapabilities.ProgressReporting)
                    },
                    InactivityReminderDays = provider.InactivityReminderDays,
                    DisclosureVersion = provider.Disclosure.Version
                })
                .ToList();

            return Results.Ok(summaries);
        })
        .WithTags("Sync")
        .WithSummary("List registered cloud sync providers.")
        .WithDescription("Returns every registered cloud sync provider with its capabilities, disclosure version and inactivity reminder threshold.")
        .Produces<IReadOnlyList<SyncProviderSummary>>(StatusCodes.Status200OK);

        MapStatusEndpoint(app);
        MapConnectionEndpoints(app);
        MapPairingEndpoint(app);
        MapHeartbeatEndpoint(app);
        MapCommandEndpoint(app);
        MapJobEndpoints(app);

        return app;
    }

    private static void MapStatusEndpoint(WebApplication app)
    {
        app.MapGet("/sync/{providerId}", async (
            string providerId,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections,
            [FromServices] SyncJobRepository jobs,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();

            // The status the user sees is the recovery surface: a job whose browser went away is
            // swept here so it reads as interrupted instead of as a sync that never ends.
            await syncService.MarkStaleJobsAsync();

            var connection = await connections.GetLiveAsync(userId, providerId);
            var currentJob = connection is null ? null : await jobs.GetRunningAsync(connection.Id);
            var latestJob = connection is null ? null : await jobs.GetLatestTerminalAsync(connection.Id);
            var lastCompletedJob = connection is null ? null : await jobs.GetLatestCompletedAsync(connection.Id);

            var now = DateTimeOffset.UtcNow;
            var status = SyncStatusDerivation.Derive(connection, currentJob, latestJob, reminderDue: false, now);

            return Results.Ok(new SyncStatusResponse
            {
                ProviderId = provider.Descriptor.Id,
                Status = status,
                Connection = MapConnection(connection, provider, now),
                Schedule = MapSchedule(connection, provider),
                CurrentJob = MapJob(currentJob),
                LastCompletedJob = MapJob(lastCompletedJob),
                Failure = MapFailure(status, connection, latestJob),
                Reminder = null,
                Disclosure = MapDisclosure(provider.Disclosure),
                Prerequisites = provider.Prerequisites
            });
        })
        .WithTags("Sync")
        .WithSummary("Get the status of a cloud sync provider.")
        .WithDescription("Returns the derived status, connection, schedule, current and last completed job, failure report, reminder and the always-visible disclosures and prerequisites.")
        .Produces<SyncStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapConnectionEndpoints(WebApplication app)
    {
        app.MapPost("/sync/{providerId}/connection", async (
            string providerId,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            var token = SyncTokenService.GenerateToken();
            var now = DateTimeOffset.UtcNow.ToString("o");

            var existing = await connections.GetLiveAsync(userId, providerId);
            if (existing is null)
            {
                await connections.CreateAsync(userId, providerId, SyncTokenService.HashToken(token), provider.Disclosure.Version, now);
            }
            else
            {
                // Pairing again keeps history, region and frequency; only the token is replaced.
                await connections.RotateTokenAsync(existing.Id, SyncTokenService.HashToken(token));
            }

            return Results.Created(
                $"/sync/{providerId}",
                new CreateSyncConnectionResponse { ProviderId = provider.Descriptor.Id, Token = token });
        })
        .WithTags("Sync")
        .WithSummary("Create a cloud sync connection and return a one-time pairing token.")
        .WithDescription("Creates the connection in the pending_pairing state, or replaces the token of an existing one, and returns the pairing token exactly once. Only its hash is stored.")
        .Produces<CreateSyncConnectionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/sync/{providerId}/connection", async (
            string providerId,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            var connection = await connections.GetLiveAsync(userId, providerId);

            if (connection is not null)
                await connections.DisconnectAsync(connection.Id);

            return Results.NoContent();
        })
        .WithTags("Sync")
        .WithSummary("Disconnect a cloud sync provider.")
        .WithDescription("Revokes the pairing token and marks the connection disconnected. Imported highlights, books and authors are never touched.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapPairingEndpoint(WebApplication app)
    {
        app.MapPost("/sync/{providerId}/pair", async (
            HttpContext http,
            string providerId,
            SyncPairRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections,
            [FromServices] SyncTokenService tokens) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            var connection = await AuthenticateAsync(http, tokens, userId, providerId);
            if (connection is null)
                return Unauthorized();

            if (request is null)
                return ValidationProblem("body", "A JSON body with profileVersion and regionId is required.");

            var errors = new Dictionary<string, string[]>();
            var profileVersion = (request.ProfileVersion ?? string.Empty).Trim();

            if (profileVersion.Length == 0)
                errors["profileVersion"] = ["profileVersion must not be empty."];
            else if (profileVersion.Length > MaxProfileVersionLength)
                errors["profileVersion"] = [$"profileVersion must be at most {MaxProfileVersionLength} characters."];

            if (!provider.Profile.TryResolveRegion(request.RegionId, out var region))
                errors["regionId"] = [UnsupportedRegion(provider)];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            var now = DateTimeOffset.UtcNow.ToString("o");
            await connections.SetProfileVersionAsync(connection.Id, profileVersion);
            await connections.SetRegionAsync(connection.Id, region!.Id, now);
            await connections.MarkConnectedAsync(connection.Id, now);

            return Results.Ok(new SyncPairResponse
            {
                ProviderId = provider.Descriptor.Id,
                State = "active",
                IntervalMinutes = connection.SyncIntervalMinutes,
                AllowedIntervalMinutes = provider.Frequency.AllowedMinutes,
                Region = MapRegion(region)
            });
        })
        .WithTags("Sync")
        .WithSummary("Activate a connection with the region the extension reads.")
        .WithDescription("Exchanges the pairing token for an active connection, stores the parse-profile version and region, and returns the stored frequency so the extension can create its sync alarm.")
        .Produces<SyncPairResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapHeartbeatEndpoint(WebApplication app)
    {
        app.MapPost("/sync/{providerId}/heartbeat", async (
            HttpContext http,
            string providerId,
            SyncHeartbeatRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections,
            [FromServices] SyncTokenService tokens) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            var connection = await AuthenticateAsync(http, tokens, userId, providerId);
            if (connection is null)
                return Unauthorized();

            var heartbeat = request ?? new SyncHeartbeatRequest();
            var applied = heartbeat.AppliedIntervalMinutes is { } minutes && provider.Frequency.IsAllowed(minutes)
                ? minutes
                : (int?)null;

            await connections.RecordHeartbeatAsync(
                connection.Id,
                NormalizeText(heartbeat.ProfileVersion, MaxProfileVersionLength),
                applied,
                NormalizeTimestamp(heartbeat.NextSyncDueAt),
                DateTimeOffset.UtcNow.ToString("o"));

            var command = await connections.ConsumePendingCommandAsync(connection.Id) ?? NoCommand;

            return Results.Ok(new SyncHeartbeatResponse
            {
                Command = command,
                Region = ResolveRegion(provider, connection.RegionId),
                IntervalMinutes = connection.SyncIntervalMinutes,
                HeartbeatSeconds = HeartbeatSeconds
            });
        })
        .WithTags("Sync")
        .WithSummary("Report extension liveness and pick up the pending command.")
        .WithDescription("Records the heartbeat with the extension-reported schedule, consumes the single pending user-requested command and returns the stored frequency. A heartbeat never starts a sync.")
        .Produces<SyncHeartbeatResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapCommandEndpoint(WebApplication app)
    {
        app.MapPost("/sync/{providerId}/commands", async (
            string providerId,
            SyncCommandRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();

            if (request is null)
                return ValidationProblem("body", "A JSON body with command is required.");

            var command = (request.Command ?? string.Empty).Trim();

            if (!AllowedCommands.Contains(command))
                return ValidationProblem("command", $"command must be one of: {string.Join(", ", AllowedCommands)}.");

            var connection = await connections.GetLiveAsync(userId, providerId);

            if (connection is null)
                return Results.Problem(
                    detail: "Connect Kindle cloud before requesting a sync.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Not connected");

            if (connection.State == "pending_pairing")
                return Results.Problem(
                    detail: "Pair the browser extension before requesting a sync.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Extension not paired");

            await connections.SetPendingCommandAsync(connection.Id, command);

            // Read back what is actually queued: a sync request never downgrades a pending full
            // resync, so the answer can differ from the requested command.
            var pending = await connections.GetPendingCommandAsync(connection.Id) ?? NoCommand;

            return Results.Accepted(value: new SyncCommandResponse
            {
                ProviderId = provider.Descriptor.Id,
                PendingCommand = pending
            });
        })
        .WithTags("Sync")
        .WithSummary("Queue a user-requested sync command.")
        .WithDescription("Queues exactly one pending command the extension picks up on its next heartbeat. A sync request never overwrites a pending full resync. Nothing is scheduled server-side.")
        .Produces<SyncCommandResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapJobEndpoints(WebApplication app)
    {
        app.MapGet("/sync/{providerId}/jobs", async (
            string providerId,
            int? limit,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncConnectionRepository connections,
            [FromServices] SyncJobRepository jobs) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var requested = limit ?? DefaultJobsLimit;

            if (requested < 1 || requested > MaxJobsLimit)
                return ValidationProblem("limit", $"limit must be between 1 and {MaxJobsLimit}.");

            var userId = await userRepo.EnsureUserAsync();
            var connection = await connections.GetLiveAsync(userId, providerId);

            if (connection is null)
                return Results.Ok(Array.Empty<SyncJobDto>());

            var recent = await jobs.ListRecentAsync(connection.Id, requested);

            return Results.Ok(recent.Select(job => MapJob(job)!).ToList());
        })
        .WithTags("Sync")
        .WithSummary("List the most recent sync jobs.")
        .WithDescription("Returns the job history for the connection, newest first, with the failure code, detail and recovery steps of jobs that did not finish. An unconnected provider returns an empty list.")
        .Produces<IReadOnlyList<SyncJobDto>>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/sync/{providerId}/jobs", async (
            HttpContext http,
            string providerId,
            SyncJobStartRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncTokenService tokens,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            if (await AuthenticateAsync(http, tokens, userId, providerId) is null)
                return Unauthorized();

            if (request is null)
                return ValidationProblem("body", "A JSON body with idempotencyKey, mode and trigger is required.");

            return await GuardAsync(async () =>
            {
                var result = await syncService.StartJobAsync(userId, provider.Descriptor.Id, request);
                return Results.Ok(new SyncJobStartResponse { JobId = result.Job.Id, KnownBooks = result.KnownBooks });
            });
        })
        .WithTags("Sync")
        .WithSummary("Start a sync job, or return the running one.")
        .WithDescription("Single-flight: a start while another job is running returns that job. A repeated idempotency key returns the same job, and a full resync carries the known-book fingerprint.")
        .Produces<SyncJobStartResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/sync/{providerId}/jobs/{jobId:long}/batches", async (
            HttpContext http,
            string providerId,
            long jobId,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncTokenService tokens,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            if (await AuthenticateAsync(http, tokens, userId, providerId) is null)
                return Unauthorized();

            var (batch, error) = await ReadBatchAsync(http.Request);
            if (error is not null)
                return error;

            return await GuardAsync(async () =>
            {
                var result = await syncService.CommitBatchAsync(userId, provider.Descriptor.Id, jobId, batch!);
                return Results.Ok(new SyncBatchResponse
                {
                    BatchIndex = result.BatchIndex,
                    Replayed = result.Replayed,
                    NewHighlights = result.Response.NewHighlights,
                    DuplicateHighlights = result.Response.DuplicateHighlights,
                    NewBooks = result.Response.NewBooks,
                    NewAuthors = result.Response.NewAuthors
                });
            });
        })
        .WithTags("Sync")
        .WithSummary("Commit one batch of a running sync job.")
        .WithDescription("Idempotent per batchIndex: a replay returns the stored counts unchanged. Highlights are only ever inserted.")
        .Produces<SyncBatchResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        app.MapPost("/sync/{providerId}/jobs/{jobId:long}/progress", async (
            HttpContext http,
            string providerId,
            long jobId,
            SyncJobProgressRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncTokenService tokens,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            if (await AuthenticateAsync(http, tokens, userId, providerId) is null)
                return Unauthorized();

            if (request is null)
                return ValidationProblem("body", "A JSON body with phase is required.");

            var phase = (request.Phase ?? string.Empty).Trim();
            var errors = new Dictionary<string, string[]>();

            if (!AllowedPhases.Contains(phase, StringComparer.Ordinal))
                errors["phase"] = [$"phase must be one of: {string.Join(", ", AllowedPhases)}."];

            if (request.BooksTotal is < 0)
                errors["booksTotal"] = ["booksTotal must not be negative."];

            if (request.BooksDone is < 0)
                errors["booksDone"] = ["booksDone must not be negative."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            return await GuardAsync(async () =>
            {
                await syncService.UpdateProgressAsync(userId, jobId, phase, request.BooksTotal, request.BooksDone);
                return Results.NoContent();
            });
        })
        .WithTags("Sync")
        .WithSummary("Report the progress of a running sync job.")
        .WithDescription("Updates the phase and, when supplied, the book counters of a running job. Progress never completes a job.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/sync/{providerId}/jobs/{jobId:long}/complete", async (
            HttpContext http,
            string providerId,
            long jobId,
            SyncJobCompleteRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncTokenService tokens,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            if (await AuthenticateAsync(http, tokens, userId, providerId) is null)
                return Unauthorized();

            if (request is null)
                return ValidationProblem("body", "A JSON body with booksTotal, booksDone and truncatedBooks is required.");

            var errors = new Dictionary<string, string[]>();

            if (request.BooksTotal < 0)
                errors["booksTotal"] = ["booksTotal must not be negative."];

            if (request.BooksDone < 0)
                errors["booksDone"] = ["booksDone must not be negative."];

            if (request.TruncatedBooks < 0)
                errors["truncatedBooks"] = ["truncatedBooks must not be negative."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            return await GuardAsync(async () =>
            {
                var job = await syncService.CompleteAsync(userId, jobId, request.BooksTotal, request.BooksDone, request.TruncatedBooks);
                return Results.Ok(MapJob(job));
            });
        })
        .WithTags("Sync")
        .WithSummary("Complete a sync job explicitly.")
        .WithDescription("A job only reaches completed_with_changes or completed_no_changes through this call with booksTotal equal to booksDone; a partial run never reports success.")
        .Produces<SyncJobDto>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/sync/{providerId}/jobs/{jobId:long}/fail", async (
            HttpContext http,
            string providerId,
            long jobId,
            SyncJobFailRequest? request,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncTokenService tokens,
            [FromServices] SyncService syncService) =>
        {
            if (ResolveProvider(providers, providerId) is not { } provider)
                return UnknownProvider(providers, providerId);

            var userId = await userRepo.EnsureUserAsync();
            if (await AuthenticateAsync(http, tokens, userId, providerId) is null)
                return Unauthorized();

            if (request is null)
                return ValidationProblem("body", "A JSON body with code is required.");

            var code = (request.Code ?? string.Empty).Trim();
            var errors = new Dictionary<string, string[]>();

            if (code.Length == 0)
                errors["code"] = ["code must not be empty."];
            else if (code.Length > MaxFailureCodeLength)
                errors["code"] = [$"code must be at most {MaxFailureCodeLength} characters."];

            if (SyncFailureRecovery.ValidateDiagnostics(request.Diagnostics) is { } diagnosticsError)
                errors["diagnostics"] = [diagnosticsError];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            return await GuardAsync(async () =>
            {
                await syncService.FailAsync(
                    userId,
                    jobId,
                    code,
                    SyncFailureRecovery.DetailFor(code),
                    SyncFailureRecovery.StepsFor(code));

                return Results.NoContent();
            });
        })
        .WithTags("Sync")
        .WithSummary("Fail a sync job with a stable code.")
        .WithDescription("Records the failure code with sanitized detail and recovery steps. Diagnostics accept structural identifiers and counts only; page free text is rejected.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<SyncConnection?> AuthenticateAsync(
        HttpContext http,
        SyncTokenService tokens,
        int userId,
        string providerId) =>
        await tokens.VerifyAsync(userId, providerId, SyncTokenService.ExtractBearerToken(http.Request.Headers.Authorization.ToString()));

    private static ICloudSyncProvider? ResolveProvider(IEnumerable<ICloudSyncProvider> providers, string providerId) =>
        providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Id, providerId, StringComparison.Ordinal));

    private static IResult UnknownProvider(IEnumerable<ICloudSyncProvider> providers, string providerId) =>
        Results.Problem(
            detail: $"Unknown provider '{providerId}'. Known providers: {string.Join(", ", providers.Select(provider => provider.Descriptor.Id))}.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Unknown provider");

    private static IResult Unauthorized() =>
        Results.Problem(
            detail: "The pairing token is missing, invalid or revoked. Pair the extension again to continue.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            extensions: new Dictionary<string, object?> { ["recovery"] = "re-pair" });

    private static IResult ValidationProblem(string field, string message) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { { field, [message] } },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult PayloadTooLarge() =>
        Results.Problem(
            detail: $"A batch body may be at most {MaxRequestBodyBytes / (1024 * 1024)} MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Payload too large");

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SyncOperationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: exception.StatusCode,
                extensions: exception.Recovery is null
                    ? null
                    : new Dictionary<string, object?> { ["recovery"] = exception.Recovery });
        }
    }

    private static async Task<(CloudSyncBatch? Batch, IResult? Error)> ReadBatchAsync(HttpRequest request)
    {
        if (request.ContentLength is > MaxRequestBodyBytes)
            return (null, PayloadTooLarge());

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxRequestBodyBytes)
                return (null, PayloadTooLarge());

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
            return (null, ValidationProblem("body", "A JSON batch body is required."));

        try
        {
            var batch = JsonSerializer.Deserialize<CloudSyncBatch>(buffer.ToArray(), BatchJsonOptions);
            return batch is null
                ? (null, ValidationProblem("body", "A JSON batch body is required."))
                : (batch, null);
        }
        catch (JsonException)
        {
            return (null, ValidationProblem("body", "The request body is not a valid sync batch."));
        }
    }

    private static SyncConnectionDto MapConnection(SyncConnection? connection, ICloudSyncProvider provider, DateTimeOffset now) =>
        connection is null
            ? new SyncConnectionDto()
            : new SyncConnectionDto
            {
                State = connection.State == "disconnected" ? "not_connected" : connection.State,
                ConnectedAt = connection.ConnectedAt,
                LastHeartbeatAt = connection.LastHeartbeatAt,
                BrowserReporting = SyncStatusDerivation.IsBrowserReporting(connection, now),
                Region = ResolveRegion(provider, connection.RegionId)
            };

    private static SyncScheduleDto MapSchedule(SyncConnection? connection, ICloudSyncProvider provider)
    {
        if (connection is null)
        {
            return new SyncScheduleDto
            {
                IntervalMinutes = provider.Frequency.Default,
                AllowedIntervalMinutes = provider.Frequency.AllowedMinutes,
                NextRunAt = null,
                Applied = false,
                PendingCommand = NoCommand
            };
        }

        return new SyncScheduleDto
        {
            IntervalMinutes = connection.SyncIntervalMinutes,
            AllowedIntervalMinutes = provider.Frequency.AllowedMinutes,
            NextRunAt = connection.State == "active" ? connection.NextSyncDueAt : null,
            Applied = connection.AppliedIntervalMinutes == connection.SyncIntervalMinutes,
            PendingCommand = connection.PendingCommand ?? NoCommand
        };
    }

    private static SyncFailureDto? MapFailure(string status, SyncConnection? connection, SyncJobRecord? latestJob)
    {
        // A failure report belongs to the status the user is seeing: while a retry is running, the
        // previous failure is history, not a current problem.
        if (status is not ("failed" or "auth_expired"))
            return null;

        if (latestJob?.Status is "failed" or "auth_expired" or "interrupted")
        {
            var code = latestJob.FailureCode ?? latestJob.Status;

            return new SyncFailureDto
            {
                Code = code,
                Detail = latestJob.FailureDetail ?? SyncFailureRecovery.DetailFor(code),
                RecoverySteps = SyncFailureRecovery.ParseSteps(latestJob.RecoverySteps, code),
                OccurredAt = latestJob.EndedAt
            };
        }

        if (status == "auth_expired")
        {
            return new SyncFailureDto
            {
                Code = "auth_expired",
                Detail = SyncFailureRecovery.DetailFor("auth_expired"),
                RecoverySteps = SyncFailureRecovery.StepsFor("auth_expired"),
                OccurredAt = connection?.LastAuthExpiredAt
            };
        }

        return null;
    }

    private static SyncJobDto? MapJob(SyncJobRecord? job) =>
        job is null
            ? null
            : new SyncJobDto
            {
                Id = job.Id,
                Mode = job.Mode,
                Status = job.Status,
                Phase = job.Phase,
                BooksTotal = job.BooksTotal,
                BooksDone = job.BooksDone,
                HighlightsSeen = job.HighlightsSeen,
                HighlightsNew = job.HighlightsNew,
                HighlightsDuplicate = job.HighlightsDuplicate,
                TruncatedBooks = job.TruncatedBooks,
                FailureCode = job.FailureCode,
                FailureDetail = job.FailureDetail ?? SyncFailureRecovery.DetailFor(job.FailureCode),
                RecoverySteps = SyncFailureRecovery.ParseSteps(job.RecoverySteps, job.FailureCode),
                StartedAt = job.StartedAt,
                EndedAt = job.EndedAt
            };

    private static SyncDisclosureDto MapDisclosure(CoverageDisclosure disclosure) =>
        new()
        {
            Version = disclosure.Version,
            Items = disclosure.Items
                .Select(item => new SyncDisclosureItemDto { Kind = item.Kind, Title = item.Title, Body = item.Body })
                .ToList()
        };

    private static SyncRegionDto? ResolveRegion(ICloudSyncProvider provider, string? regionId) =>
        provider.Profile.TryResolveRegion(regionId, out var region) ? MapRegion(region) : null;

    private static SyncRegionDto MapRegion(KindleRegion region) =>
        new()
        {
            Id = region.Id,
            DisplayName = region.DisplayName,
            Host = region.NotebookHost
        };

    private static string UnsupportedRegion(ICloudSyncProvider provider) =>
        $"regionId must be one of: {string.Join(", ", provider.Profile.Regions.Select(region => region.Id))}. Hosts are never accepted.";

    private static string? NormalizeText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
            return null;

        return trimmed;
    }

    private static string? NormalizeTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out _)
            ? value
            : null;
    }
}
