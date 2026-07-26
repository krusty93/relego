using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class BookEndpoints
{
    public static WebApplication MapBookEndpoints(this WebApplication app)
    {
        app.MapGet("/books", async (
            [FromServices] UserRepository userRepo,
            [FromServices] BookRepository bookRepo,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            [FromQuery] string? q = null) =>
        {
            if (page < 1)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "page", ["page must be greater than or equal to 1."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (pageSize is < 1 or > 500)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "pageSize", ["pageSize must be between 1 and 500."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var userId = await userRepo.EnsureUserAsync();
            var result = await bookRepo.GetBooksAsync(userId, page, pageSize, q);
            return Results.Ok(result);
        })
        .WithTags("Books")
        .WithSummary("List books.")
        .WithDescription("Returns a paginated, optionally filtered list of books with highlight counts and exclusion state.")
        .Produces<BooksResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/books/{id:int}/title", async (
            int id,
            [FromBody] RenameBookRequest request,
            [FromServices] UserRepository userRepo,
            [FromServices] BookRepository bookRepo) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "title", ["title must not be empty."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var userId = await userRepo.EnsureUserAsync();
            var result = await bookRepo.RenameAsync(userId, id, request.Title.Trim());

            return result switch
            {
                true => Results.NoContent(),
                false => Results.Problem(detail: $"Book {id} not found.", statusCode: StatusCodes.Status404NotFound),
                null => Results.Problem(detail: $"A book titled \"{request.Title.Trim()}\" by the same author already exists.", statusCode: StatusCodes.Status409Conflict),
            };
        })
        .WithTags("Books")
        .WithSummary("Rename a book.")
        .WithDescription("Updates the title of a book. Returns 409 Conflict when a book by the same author already has the requested title.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
