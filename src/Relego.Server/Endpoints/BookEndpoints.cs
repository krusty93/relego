using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class BookEndpoints
{
    public static WebApplication MapBookEndpoints(this WebApplication app)
    {
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
