using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class HighlightEndpoints
{
    public static WebApplication MapHighlightEndpoints(this WebApplication app)
    {
        app.MapGet("/highlights", async (
            [FromServices] UserRepository userRepo,
            [FromServices] HighlightRepository highlightRepo,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? q = null,
            [FromQuery] int? bookId = null,
            [FromQuery] string? chapterId = null) =>
        {
            if (page < 1)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "page", ["page must be greater than or equal to 1."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (pageSize is < 1 or > 200)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "pageSize", ["pageSize must be between 1 and 200."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (bookId is <= 0)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "bookId", ["bookId must be greater than 0."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            int? chapterFilterId = null;
            var unknownChapter = false;

            if (!string.IsNullOrWhiteSpace(chapterId))
            {
                if (string.Equals(chapterId.Trim(), "unknown", StringComparison.OrdinalIgnoreCase))
                {
                    unknownChapter = true;
                }
                else if (int.TryParse(chapterId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedChapterId) && parsedChapterId > 0)
                {
                    chapterFilterId = parsedChapterId;
                }
                else
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { { "chapterId", ["chapterId must be a chapter id or the literal \"unknown\"."] } },
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }

            // Chapters belong to a book, so a chapter filter without one selects nothing extra.
            if (bookId is null)
            {
                chapterFilterId = null;
                unknownChapter = false;
            }

            var userId = await userRepo.EnsureUserAsync();
            var result = await highlightRepo.GetHighlightsAsync(userId, page, pageSize, q, bookId, chapterFilterId, unknownChapter);
            return Results.Ok(result);
        })
        .WithTags("Highlights")
        .WithSummary("List highlights.")
        .WithDescription("Returns a paginated, optionally filtered list of highlights. Filter by book with bookId, and narrow it to a chapter with chapterId (a chapter id or the literal unknown).")
        .Produces<HighlightsResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/highlights/{id:int}", async (
            int id,
            [FromServices] UserRepository userRepo,
            [FromServices] HighlightRepository highlightRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var deleted = await highlightRepo.DeleteHighlightAsync(userId, id);

            return deleted
                ? Results.NoContent()
                : Results.Problem(detail: $"Highlight {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Highlights")
        .WithSummary("Delete a highlight.")
        .WithDescription("Deletes a stored highlight for the implicit MVP user.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
