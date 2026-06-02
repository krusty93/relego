using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Endpoints;

public static class WeightEndpoints
{
    public static WebApplication MapWeightEndpoints(this WebApplication app)
    {
        app.MapPut("/highlights/{id:int}/weight", async (int id, SetWeightRequest? request, [FromServices] UserRepository userRepo, [FromServices] WeightRepository weightRepo) =>
        {
            if (request is null)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "body", ["Request body must not be null."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (request.Weight is < 1 or > 5)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { { "weight", ["Weight must be between 1 and 5."] } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var userId = await userRepo.EnsureUserAsync();
            var updated = await weightRepo.SetWeightAsync(userId, id, request.Weight);

            return updated
                ? Results.NoContent()
                : Results.Problem(detail: $"Highlight {id} not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithTags("Weights")
        .WithSummary("Set a highlight weight.")
        .WithDescription("Applies a custom recap weight from 1 to 5 to a specific highlight.")
        .Accepts<SetWeightRequest>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/highlights/weights", async ([FromServices] UserRepository userRepo, [FromServices] WeightRepository weightRepo) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var weights = await weightRepo.GetWeightedHighlightsAsync(userId);
            return Results.Ok(weights);
        })
        .WithTags("Weights")
        .WithSummary("List weighted highlights.")
        .WithDescription("Returns highlights whose recap weight differs from the default value of 3.")
        .Produces<List<WeightedHighlightDto>>(StatusCodes.Status200OK);

        return app;
    }
}
