using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class SyncEndpoints
{
    public static WebApplication MapSyncEndpoints(this WebApplication app)
    {
        app.MapPost("/highlights/import", async (SyncRequest? request, [FromServices] UserRepository userRepo, [FromServices] SyncRepository syncRepo) =>
        {
            if (request is null || request.Books is null)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "books", ["Books must not be null."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var errors = new Dictionary<string, string[]>();

            for (var i = 0; i < request.Books.Count; i++)
            {
                var book = request.Books[i];

                if (string.IsNullOrWhiteSpace(book.Title))
                    errors[$"books[{i}].title"] = ["Book title must not be empty."];

                if (book.Highlights is null || book.Highlights.Count == 0)
                {
                    errors[$"books[{i}].highlights"] = ["Book must have at least one highlight."];
                }
                else
                {
                    for (var j = 0; j < book.Highlights.Count; j++)
                    {
                        if (string.IsNullOrWhiteSpace(book.Highlights[j].Text))
                            errors[$"books[{i}].highlights[{j}].text"] = ["Highlight text must not be empty or whitespace."];
                    }
                }
            }

            if (errors.Count > 0)
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);

            var userId = await userRepo.EnsureUserAsync();
            var response = await syncRepo.ImportAsync(userId, request);
            return Results.Ok(response);
        })
        .WithTags("Sync")
        .WithSummary("Import parsed Kindle highlights from a client.")
        .WithDescription("Accepts a parsed clippings payload, persists authors, books, and highlights, deduplicates existing highlights, and returns an import summary for the implicit MVP user.")
        .Accepts<SyncRequest>("application/json")
        .Produces<SyncResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
