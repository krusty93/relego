using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class ExclusionEndpoints
{
    public static WebApplication MapExclusionEndpoints(this WebApplication app)
    {
        app.MapPost("/highlights/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var excluded = await exclusionRepo.ExcludeHighlightAsync(userId, id);
            return excluded
                ? Results.NoContent()
                : Results.Problem(detail: $"Highlight {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Exclude a highlight.")
        .WithDescription("Marks a specific highlight as individually excluded from future recap selection.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/highlights/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var included = await exclusionRepo.IncludeHighlightAsync(userId, id);
            return included
                ? Results.NoContent()
                : Results.Problem(detail: $"Highlight {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Re-include a highlight.")
        .WithDescription("Removes the individual exclusion flag from a highlight.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/books/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var excluded = await exclusionRepo.ExcludeBookAsync(userId, id);
            return excluded
                ? Results.NoContent()
                : Results.Problem(detail: $"Book {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Exclude a book.")
        .WithDescription("Adds a book-level exclusion so every highlight in that book is skipped during recap selection.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/books/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var included = await exclusionRepo.IncludeBookAsync(userId, id);
            return included
                ? Results.NoContent()
                : Results.Problem(detail: $"Book {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Re-include a book.")
        .WithDescription("Removes a book-level exclusion.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/authors/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var excluded = await exclusionRepo.ExcludeAuthorAsync(userId, id);
            return excluded
                ? Results.NoContent()
                : Results.Problem(detail: $"Author {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Exclude an author.")
        .WithDescription("Adds an author-level exclusion so every book by that author is skipped during recap selection.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/authors/{id:int}/exclusions", async (int id, [FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var included = await exclusionRepo.IncludeAuthorAsync(userId, id);
            return included
                ? Results.NoContent()
                : Results.Problem(detail: $"Author {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Exclusions")
        .WithSummary("Re-include an author.")
        .WithDescription("Removes an author-level exclusion.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/exclusions", async ([FromServices] UserRepository userRepo, [FromServices] ExclusionRepository exclusionRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var response = await exclusionRepo.GetExclusionsAsync(userId);
            return Results.Ok(response);
        })
        .WithTags("Exclusions")
        .WithSummary("List exclusions.")
        .WithDescription("Returns all current highlight-, book-, and author-level exclusions for the implicit MVP user.")
        .Produces<ExclusionsResponse>(StatusCodes.Status200OK);

        return app;
    }
}
