using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Services;

namespace Relego.Server.Endpoints;

public static class ImportEndpoints
{
    public static WebApplication MapImportEndpoints(this WebApplication app)
    {
        app.MapPost("/imports", async (
            HttpRequest request,
            [FromServices] UserRepository userRepo,
            [FromServices] SyncRepository syncRepo,
            [FromServices] UploadImportService importService,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = ["Send the export as multipart/form-data with a 'file' field."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            IFormFile? file;
            try
            {
                var form = await request.ReadFormAsync(cancellationToken);
                file = form.Files["file"] ?? form.Files.FirstOrDefault();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = ["The upload could not be read. Send the export as multipart/form-data with a 'file' field."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (file is null || file.Length == 0)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = ["Attach a Kindle 'My Clippings.txt' or a Kobo 'KoboReader.sqlite'."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (file.Length > UploadImportService.MaxUploadBytes)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["file"] = [$"The file is larger than {UploadImportService.MaxUploadBytes / (1024 * 1024)} MB."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            UploadImportResult parsed;
            try
            {
                await using var content = file.OpenReadStream();
                parsed = await importService.ParseAsync(content, file.FileName, cancellationToken);
            }
            catch (UploadImportException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = [ex.Message] },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var parseResult = parsed.ParseResult;
            var highlightsParsed = parseResult.Books.Sum(book => book.Highlights.Count);

            var response = new ImportResponse
            {
                Source = parsed.Source.Id,
                SourceName = parsed.Source.DisplayName,
                FileName = Path.GetFileName(file.FileName),
                BooksParsed = parseResult.Books.Count,
                HighlightsParsed = highlightsParsed,
                EntriesProcessed = parseResult.TotalEntriesProcessed,
                DuplicatesInFile = parseResult.DuplicatesRemoved,
            };

            if (parseResult.Books.Count > 0)
            {
                var userId = await userRepo.EnsureUserAsync();
                var syncResponse = await syncRepo.ImportAsync(
                    userId,
                    UploadImportService.ToSyncRequest(parseResult));

                response.NewHighlights = syncResponse.NewHighlights;
                response.DuplicateHighlights = syncResponse.DuplicateHighlights;
                response.NewBooks = syncResponse.NewBooks;
                response.NewAuthors = syncResponse.NewAuthors;
            }

            return Results.Ok(response);
        })
        .DisableAntiforgery()
        .WithTags("Import")
        .WithSummary("Import highlights from an uploaded export file.")
        .WithDescription(
            "Accepts a Kindle 'My Clippings.txt' or a Kobo 'KoboReader.sqlite' as multipart/form-data, " +
            "detects the format from the file's contents, parses it server-side, and stores the result. " +
            "Highlights that already exist are reported as duplicates rather than re-imported.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
