using Microsoft.AspNetCore.Mvc;
using Relego.Server.Data;
using Relego.Server.Services;

namespace Relego.Server.Endpoints;

public static class RecapEndpoints
{
    public static WebApplication MapRecapEndpoints(this WebApplication app)
    {
        app.MapPost("/recap/trigger", async (
            [FromServices] UserRepository userRepo,
            [FromServices] IRecapService recapService,
            CancellationToken ct) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var scheduledFor = DateTimeOffset.UtcNow;

            try
            {
                await recapService.ExecuteAsync(userId, scheduledFor, ct);
                return Results.Ok(new { status = "triggered", scheduledFor });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithSummary("Trigger a recap immediately.")
        .WithDescription("Executes the full recap pipeline synchronously for the implicit user.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}
